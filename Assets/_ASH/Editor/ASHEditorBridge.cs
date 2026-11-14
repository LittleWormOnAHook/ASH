using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// File-based automation bridge so an external agent can drive the already-open Editor.
    /// Drop a line "id|verb|arg" into &lt;project&gt;/AshBridge/command.txt; the result is written to result.txt,
    /// a heartbeat to status.txt, and warnings/errors (plus "[ASH]" logs) to console.log.
    /// Verbs: ping, refresh, compile, menu, exec (static Namespace.Type.Method), open, save, play, stop, clearlog, shot.
    /// Editor-only; never ships in a build.
    /// </summary>
    [InitializeOnLoad]
    public static class ASHEditorBridge
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "AshBridge"));
        private static string CmdPath => Path.Combine(Root, "command.txt");
        private static string ResultPath => Path.Combine(Root, "result.txt");
        private static string StatusPath => Path.Combine(Root, "status.txt");
        private static string LogPath => Path.Combine(Root, "console.log");
        private static readonly object LogLock = new object();
        private static double _nextPoll;
        private static double _nextStatus;
        private static string _lastCompile = "none";

        static ASHEditorBridge()
        {
            Directory.CreateDirectory(Root);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            Application.logMessageReceivedThreaded -= OnLog;
            Application.logMessageReceivedThreaded += OnLog;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompiled;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            Append($"BRIDGE domain loaded playing={EditorApplication.isPlaying}");
        }

        private static string Stamp => DateTime.Now.ToString("HH:mm:ss.fff");

        private static void Append(string line)
        {
            try { lock (LogLock) File.AppendAllText(LogPath, $"[{Stamp}] {line}\n"); } catch { /* never throw from logging */ }
        }

        private static void OnLog(string condition, string stackTrace, LogType type)
        {
            bool important = type == LogType.Error || type == LogType.Exception || type == LogType.Assert || type == LogType.Warning;
            if (!important && !condition.StartsWith("[ASH]", StringComparison.Ordinal)) return;
            string trace = type == LogType.Exception || type == LogType.Error
                ? "\n    " + string.Join("\n    ", (stackTrace ?? "").Split('\n').Take(6))
                : "";
            Append($"{type.ToString().ToUpperInvariant()}: {condition}{trace}");
        }

        private static void OnAssemblyCompiled(string assembly, CompilerMessage[] messages)
        {
            int errors = messages.Count(m => m.type == CompilerMessageType.Error);
            int warnings = messages.Count(m => m.type == CompilerMessageType.Warning);
            _lastCompile = $"{Path.GetFileName(assembly)} errors={errors} warnings={warnings} at {Stamp}";
            foreach (var m in messages)
                Append($"COMPILE {m.type.ToString().ToUpperInvariant()}: {m.message}");
        }

        private static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now >= _nextStatus)
            {
                _nextStatus = now + 1.0;
                WriteStatus();
            }

            if (now < _nextPoll) return;
            _nextPoll = now + 0.25;
            if (!File.Exists(CmdPath)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            string raw;
            try { raw = File.ReadAllText(CmdPath).Trim(); File.Delete(CmdPath); }
            catch { return; }
            if (string.IsNullOrEmpty(raw)) return;

            string[] parts = raw.Split(new[] { '|' }, 3);
            string id = parts.Length > 0 ? parts[0] : "?";
            string verb = parts.Length > 1 ? parts[1].Trim().ToLowerInvariant() : "";
            string arg = parts.Length > 2 ? parts[2].Trim() : "";
            string result;
            try { result = Execute(verb, arg); }
            catch (Exception e) { result = "error|" + e.GetType().Name + ": " + e.Message; }
            try { File.WriteAllText(ResultPath, $"{id}|{result}\n"); } catch { }
            Append($"CMD {id} {verb} {arg} -> {result}");
        }

        private static void WriteStatus()
        {
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                File.WriteAllText(StatusPath,
                    $"time={DateTime.Now:O}\ncompiling={EditorApplication.isCompiling}\nupdating={EditorApplication.isUpdating}\n" +
                    $"playing={EditorApplication.isPlaying}\npaused={EditorApplication.isPaused}\nscene={scene.path}\ndirty={scene.isDirty}\n" +
                    $"lastCompile={_lastCompile}\n");
            }
            catch { }
        }

        private static string Execute(string verb, string arg)
        {
            switch (verb)
            {
                case "ping":
                    return "ok|pong";
                case "refresh":
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    return "ok|refreshed";
                case "compile":
                    AssetDatabase.Refresh();
                    CompilationPipeline.RequestScriptCompilation();
                    return "ok|compile requested";
                case "menu":
                    return EditorApplication.ExecuteMenuItem(arg) ? "ok|menu executed" : "error|menu not found: " + arg;
                case "exec":
                    return InvokeStatic(arg);
                case "open":
                    if (EditorApplication.isPlaying) return "error|cannot open scene while playing";
                    EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
                    EditorSceneManager.OpenScene(arg, OpenSceneMode.Single);
                    return "ok|opened " + arg;
                case "save":
                    EditorSceneManager.SaveOpenScenes();
                    AssetDatabase.SaveAssets();
                    return "ok|saved";
                case "play":
                    if (!EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
                    return "ok|entering play";
                case "stop":
                    if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
                    return "ok|exiting play";
                case "clearlog":
                    lock (LogLock) File.WriteAllText(LogPath, "");
                    return "ok|log cleared";
                case "shot":
                    return ASHBridgeScreenshot.Capture(arg);
                default:
                    return "error|unknown verb " + verb;
            }
        }

        private static string InvokeStatic(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            if (dot <= 0) return "error|expected Namespace.Type.Method";
            string typeName = fullName.Substring(0, dot);
            string methodName = fullName.Substring(dot + 1);
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, false))
                .FirstOrDefault(t => t != null);
            if (type == null) return "error|type not found: " + typeName;
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (method == null) return "error|method not found: " + methodName;
            object value = method.Invoke(null, null);
            return "ok|" + (value?.ToString() ?? "invoked");
        }
    }
}

#if UNITY_EDITOR
using System.IO;
using System.Text;
using ASH.Audio;
using ASH.EditorTools.Systems;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: audio settings asset and clip wiring from Assets/_ASH/Audio/Background Music and Combat. Does not save the open scene.</summary>
    public static class ASHAudioSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_AudioSettings.asset";
        const string MusicDir = "Assets/_ASH/Audio/Background Music and Combat";

        [MenuItem("ASH/Systems/Audio/Setup Music (idempotent)", false, 280)]
        public static void Setup()
        {
            var log = new StringBuilder("[ASH] Music setup: ");
            var s = ASHMenuInstaller.Ensure<ASHAudioSettings>(SettingsPath);
            if (s.background == null) s.background = new System.Collections.Generic.List<AudioClip>();
            s.background.Clear();
            s.combat = null;
            foreach (var g in AssetDatabase.FindAssets("t:AudioClip", new[] { MusicDir }))
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (!clip) continue;
                var n = Path.GetFileNameWithoutExtension(path).Replace(" ", "").ToLowerInvariant();
                if (n == "forward2") s.combat = clip;
                else s.background.Add(clip);
            }
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            Selection.activeObject = s;
            log.Append(s.background.Count + " background track(s)");
            log.Append(s.combat ? "; combat = " + s.combat.name : "; Forward2 not found");
            log.Append(". Audio manager creates itself at runtime (no scene save). Volumes: Settings > Audio.");
            Debug.Log(log.ToString());
        }

        [MenuItem("ASH/Systems/Audio/Select Audio Settings", false, 281)]
        public static void Select()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHAudioSettings>(SettingsPath);
            if (!s) Setup();
            s = AssetDatabase.LoadAssetAtPath<ASHAudioSettings>(SettingsPath);
            Selection.activeObject = s;
            EditorGUIUtility.PingObject(s);
        }
    }
}
#endif

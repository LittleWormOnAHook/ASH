using ASH.World.Weather;
using UnityEditor;

namespace ASH.EditorTools
{
    /// <summary>Edit mode: bind the dummy _EnviroRemovalZones buffer for Scene/Game view drawing of Enviro particle materials
    /// (see ASHEnviroBufferFallback); released before each domain reload.</summary>
    [InitializeOnLoad]
    static class ASHEnviroBufferFallbackEditor
    {
        static ASHEnviroBufferFallbackEditor()
        {
            EditorApplication.delayCall += () => { if (!EditorApplication.isPlayingOrWillChangePlaymode) ASHEnviroBufferFallback.BindIfNeeded(); };
            AssemblyReloadEvents.beforeAssemblyReload -= ASHEnviroBufferFallback.Release;
            AssemblyReloadEvents.beforeAssemblyReload += ASHEnviroBufferFallback.Release;
        }
    }
}

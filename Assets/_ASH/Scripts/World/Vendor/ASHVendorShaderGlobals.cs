using UnityEngine;

namespace ASH.World.Vendor
{
    /// <summary>
    /// Default values for vendor shader globals that are normally pushed by a vendor "global settings" prefab we don't put
    /// in ASH scenes. ANGRY MESH shaders multiply their per-material top layer (moss/snow/sand) by AGP_* globals, which are
    /// 0 without the AG Global Snow prefab, so the top layer vanished. Strings only: compiles without the vendor pack.
    /// A vendor AG Global Settings/Snow component in a scene still wins (it sets the same globals after us).
    /// </summary>
    public static class ASHVendorShaderGlobals
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Apply()
        {
            // ANGRY MESH (AGGlobalSnow defaults)
            Shader.SetGlobalFloat("AGP_SnowIntensity", 1f);
            Shader.SetGlobalFloat("AGP_SnowOffset", 1f);
            Shader.SetGlobalFloat("AGP_SnowContrast", 1f);
            Shader.SetGlobalFloat("AGH_SnowMinimumHeight", -100f);
            Shader.SetGlobalFloat("AGH_SnowFadeHeight", 1f);
            Shader.SetGlobalVector("AGW_WindDirection", new Vector4(1f, 0f, 0f, 0f));
            Shader.SetGlobalFloat("AGW_WindToggle", 1f);
            Shader.SetGlobalFloat("AGW_WindGrassAmplitude", 0.5f);
            Shader.SetGlobalFloat("AGW_WindGrassSpeed", 4f);
            Shader.SetGlobalFloat("AGW_WindGrassScale", 0.5f);
            Shader.SetGlobalFloat("AGW_WindGrassStiffness", 0.5f);
            Shader.SetGlobalFloat("AG_TintToggle", 0f);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod] static void EditorApply() => Apply();
#endif
    }
}

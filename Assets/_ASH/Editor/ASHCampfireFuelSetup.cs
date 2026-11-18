using System.Collections.Generic;
using ASH.Gameplay;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: creates Assets/_ASH/Resources/ASH_CampfireSettings.asset if missing and fills empty audio slots
    /// from the local audio library (Assets/Audio). Never touches scenes, prefabs or existing tuning values.</summary>
    public static class ASHCampfireFuelSetup
    {
        const string AssetPath = "Assets/_ASH/Resources/ASH_CampfireSettings.asset";

        [MenuItem("ASH/Systems/Campfire Fuel Settings (create or update)")]
        public static void Run()
        {
            var s = AssetDatabase.LoadAssetAtPath<ASHCampfireSettings>(AssetPath);
            if (!s)
            {
                s = ScriptableObject.CreateInstance<ASHCampfireSettings>();
                AssetDatabase.CreateAsset(s, AssetPath);
                Debug.Log("[ASH] Created " + AssetPath);
            }
            if (s.addFuelClips == null || s.addFuelClips.Length == 0 || System.Array.TrueForAll(s.addFuelClips, c => !c))
            {
                var clips = new List<AudioClip>();
                foreach (var g in AssetDatabase.FindAssets("t:AudioClip Break Wood", new[] { "Assets/Audio" }))
                {
                    var c = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(g));
                    if (c) clips.Add(c);
                }
                s.addFuelClips = clips.ToArray();
                Debug.Log($"[ASH] Campfire add-fuel clips: {clips.Count} from Assets/Audio");
            }
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssetIfDirty(s);
        }
    }
}

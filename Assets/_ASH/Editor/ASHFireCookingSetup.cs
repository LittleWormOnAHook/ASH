using System.Collections.Generic;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>Idempotent: creates Resources/ASH_CookingSettings, assigns the cooking/boiling loops, and gives Campfire recipes
    /// a game-clock cook time (only where it is still 0, so hand-tuned values are kept). No scene changes.</summary>
    public static class ASHFireCookingSetup
    {
        const string AssetPath = "Assets/_ASH/Resources/ASH_CookingSettings.asset";
        const string AudioDir = "Assets/_ASH/Audio/Cooking";
        static readonly Dictionary<string, float> Minutes = new Dictionary<string, float>
        { { "boilwater", 20f }, { "cookfish", 20f }, { "cookmeat", 30f }, { "drychokecherries", 90f } };
        const float OtherCampfireMinutes = 30f;

        [MenuItem("ASH/Systems/Fire Cooking (create or update)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Fire Cooking: stop Play mode first."); return; }
            var s = AssetDatabase.LoadAssetAtPath<ASHCookingSettings>(AssetPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHCookingSettings>(); AssetDatabase.CreateAsset(s, AssetPath); Debug.Log("[ASH] Created " + AssetPath); }
            if (!s.cookLoop) s.cookLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/ASH_Cook_Simmer_Loop.ogg");
            if (!s.boilLoop) s.boilLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(AudioDir + "/ASH_Boil_Loop.ogg");
            if (s.startClips == null || s.startClips.Length == 0)
            {
                var c = AssetDatabase.LoadAssetAtPath<ASHCampfireSettings>("Assets/_ASH/Resources/ASH_CampfireSettings.asset");
                if (c && c.addFuelClips != null) s.startClips = c.addFuelClips; // same wood/pot clunk as feeding the fire
            }
            EditorUtility.SetDirty(s); AssetDatabase.SaveAssetIfDirty(s);

            int set = 0, kept = 0;
            foreach (var g in AssetDatabase.FindAssets("t:ASHRecipe", new[] { "Assets/_ASH" }))
            {
                var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(AssetDatabase.GUIDToAssetPath(g));
                if (!r || r.station != ASHCraftStation.Campfire) continue;
                if (r.cookGameMinutes > 0f) { kept++; continue; }
                r.cookGameMinutes = Minutes.TryGetValue(r.recipeId, out var m) ? m : OtherCampfireMinutes;
                EditorUtility.SetDirty(r); AssetDatabase.SaveAssetIfDirty(r); set++;
            }
            Debug.Log($"[ASH] Fire Cooking: settings ok (cook loop {(s.cookLoop ? "set" : "MISSING")}, boil loop {(s.boilLoop ? "set" : "MISSING")}); campfire recipes timed {set}, kept {kept}.");
        }
    }
}

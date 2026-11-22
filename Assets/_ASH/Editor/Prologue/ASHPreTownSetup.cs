using System.Collections.Generic;
using ASH.Prologue;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Prologue
{
    /// <summary>t617: pre-town loop setup. Creates Resources/ASH_PreTownSettings, wires the optional barricade sign (vendor prefab, by path),
    /// applies the pre-town steps to ASH_PrologueChain and updates Grandpa's truck page (gj_02). Assets only: the scene is not touched or
    /// saved (part copies, the lookout extras and the Red Rock block all run at Play time).</summary>
    public static class ASHPreTownSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_PreTownSettings.asset";
        const string SignPath = "Assets/HPA/Other/Prefabs_Basic/Bastion/Signboard_Wood/Signboard_Wood_01.prefab";
        const string PageText = "The Ford still has some life in her, if you treat her right. Tires are shot - I've got one spare by the shed and I left the other at the old camp. Took the battery down to the old quarry to run the pump. The fan belt's at the ranger station - Hal was matching me a new one. Twenty litres in the can I left at the washout will get you to Red Rock.";
        const string PagePoints = "Shed and old campsite (tires), Old Quarry (battery), Ranger Station (fan belt), washout cache (fuel)";

        [MenuItem("ASH/Systems/Prologue/Set Up Pre-Town Loop (idempotent)", false, 305)]
        public static void Run()
        {
            var log = new List<string>();
            var s = AssetDatabase.LoadAssetAtPath<ASHPreTownSettings>(SettingsPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHPreTownSettings>(); s.FillDefaults(); AssetDatabase.CreateAsset(s, SettingsPath); log.Add("created " + SettingsPath); }
            if (s.barricadeProps.Count == 0) { var sign = AssetDatabase.LoadAssetAtPath<GameObject>(SignPath); if (sign) { s.barricadeProps.Add(sign); log.Add("barricade sign wired"); } }
            EditorUtility.SetDirty(s);

            var chain = ASHObjectiveChain.I; // OnEnable upgrades an older asset
            if (chain)
            {
                int before = chain.steps.Count; chain.ApplyPreTown(); if (chain.contentVersion < 5) chain.contentVersion = 5;
                EditorUtility.SetDirty(chain); AssetDatabase.SaveAssetIfDirty(chain);
                log.Add($"chain: {chain.steps.Count} steps (+{chain.steps.Count - before} this run), v{chain.contentVersion}");
            }

            var j = Resources.Load<ASHJournalCollection>("ASH_GrandpasJournal");
            if (j && j.pages != null)
                foreach (var p in j.pages)
                    if (p != null && p.id == "gj_02" && p.text != PageText) { p.text = PageText; p.pointsTo = PagePoints; EditorUtility.SetDirty(j); AssetDatabase.SaveAssetIfDirty(j); log.Add("gj_02 truck page updated"); }

            AssetDatabase.SaveAssetIfDirty(s);
            Debug.Log("[ASH] Pre-town setup: " + string.Join("; ", log) + ". Scene not touched.");
        }
    }
}

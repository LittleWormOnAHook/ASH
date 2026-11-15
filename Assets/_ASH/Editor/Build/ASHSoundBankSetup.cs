using System.Text;
using ASH.Gameplay;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>ASH/Build/Sound Bank: fills Resources/ASH_SoundBank from the audio library in Assets/Audio (referenced in place,
    /// not copied; the library is kept out of git). Only empty slots are filled unless overwrite is set.</summary>
    public static class ASHSoundBankSetup
    {
        const string Lib = "Assets/Audio/", BankPath = "Assets/_ASH/Resources/ASH_SoundBank.asset";
        static readonly StringBuilder _log = new StringBuilder();

        [MenuItem("ASH/Build/Sound Bank")]
        public static void Run() => Build(false);
        public static void RunOverwrite() => Build(true);

        static void Build(bool overwrite)
        {
            _log.Clear();
            if (!AssetDatabase.IsValidFolder("Assets/_ASH/Resources")) AssetDatabase.CreateFolder("Assets/_ASH", "Resources");
            var b = AssetDatabase.LoadAssetAtPath<ASHSoundBank>(BankPath);
            if (!b) { b = ScriptableObject.CreateInstance<ASHSoundBank>(); AssetDatabase.CreateAsset(b, BankPath); }
            void Set(string field, ref AudioClip[] slot, params string[] paths)
            {
                if (!overwrite && slot != null && System.Array.Exists(slot, c => c)) { _log.Append($"{field}: kept ({slot.Length})\n"); return; }
                var list = new System.Collections.Generic.List<AudioClip>();
                foreach (var p in paths) { var c = AssetDatabase.LoadAssetAtPath<AudioClip>(p.StartsWith("Assets/") ? p : Lib + p); if (c) list.Add(c); else _log.Append($"  missing {p}\n"); }
                slot = list.ToArray(); _log.Append($"{field}: {string.Join(", ", paths)} -> {list.Count}\n");
            }
            Set("bowDraw", ref b.bowDraw, "Weapons/Reload/Load Arrow.wav");
            Set("bowRelease", ref b.bowRelease, "Weapons/Shoots/Shoot Arrow.wav");
            Set("arrowFlight", ref b.arrowFlight, "Close Combat Sounds Effects/Swing On Air/Close Combat Swing Sound 47.wav", "Close Combat Sounds Effects/Swing On Air/Close Combat Swing Sound 50.wav", "Close Combat Sounds Effects/Swing On Air/Close Combat Swing Sound 51.wav");
            Set("arrowImpactWood", ref b.arrowImpactWood, "Weapons/Others/Arrow Impact.wav");
            Set("arrowImpactSnow", ref b.arrowImpactSnow, "Foot Steps/Snow/snowFootstep02.wav", "Foot Steps/Snow/snowFootstep03.wav");
            Set("arrowImpactRock", ref b.arrowImpactRock, "Others/Stone Impact.wav");
            Set("rifleShot", ref b.rifleShot, "Weapons/Shoots/Sniper Rifle Shoot.wav");
            Set("rifleBolt", ref b.rifleBolt, "Weapons/Others/Sniper Bolt.wav");
            Set("pistolShot", ref b.pistolShot, "Weapons/Shoots/machineGunShoot.wav");
            Set("dryFire", ref b.dryFire, "Weapons/Others/outOfAmmo.wav");
            Set("axeChop", ref b.axeChop, "Assets/_ASH/Audio/Harvest/ASH_Chop_01.wav", "Assets/_ASH/Audio/Harvest/ASH_Chop_02.wav", "Assets/_ASH/Audio/Harvest/ASH_Chop_03.wav", "Assets/_ASH/Audio/Harvest/ASH_Chop_04.wav");
            Set("treeCrack", ref b.treeCrack, "Others/Break Wood Effect.wav");
            Set("treeFall", ref b.treeFall, "Assets/_ASH/Audio/Harvest/ASH_TreeFall_01.wav");
            Set("torchOn", ref b.torchOn, "Others/Flamethrower End.wav");
            Set("flashlightOn", ref b.flashlightOn, "Weapons/Others/Flashlight Turn On.mp3");
            Set("flashlightOff", ref b.flashlightOff, "Weapons/Others/Flashlight Turn Off.mp3");
            Set("lanternOn", ref b.lanternOn, "Devices/simpleSwitch.mp3");
            Set("lanternOff", ref b.lanternOff, "Devices/simpleSwitch.mp3");
            EditorUtility.SetDirty(b); AssetDatabase.SaveAssets();
            Debug.Log("[ASH] SOUNDBANK built " + BankPath + "\n" + _log);
        }
    }
}

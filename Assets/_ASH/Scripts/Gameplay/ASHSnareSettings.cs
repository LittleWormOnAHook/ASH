using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Play-editable rabbit-snare rules. Resources/ASH_SnareSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/Gameplay/Snare Settings", fileName = "ASH_SnareSettings")]
    public sealed class ASHSnareSettings : ScriptableObject
    {
        [Header("Catch (game hours)")]
        [Tooltip("Game hours after placing or resetting before the first catch roll.")]
        [Range(0.1f, 48f)] public float firstCheckHours = 2f;
        [Tooltip("If the first roll misses, try again this often.")]
        [Range(0.1f, 24f)] public float retryHours = 1f;
        [Tooltip("Chance a waiting snare catches a rabbit on each check (0-1).")]
        [Range(0f, 1f)] public float catchChance = 0.65f;
        [Tooltip("Snares inside a building or the cabin never catch.")]
        public bool requireOutdoors = true;

        [Header("Use")]
        [Range(0.2f, 8f)] public float collectHoldSeconds = 1.2f;
        [Range(0.2f, 8f)] public float packHoldSeconds = 1.2f;
        public string setPrompt = "Snare is set";
        public string collectPrompt = "Collect rabbit";
        public string packPrompt = "Pack up snare";
        public string noSpaceMessage = "No room in the pack.";
        public string indoorMessage = "Too close to a building";
        public string caughtToast = "The snare caught a rabbit.";

        [Header("Placement")]
        [Range(0.4f, 8f)] public float footprintX = 0.8f;
        [Range(0.2f, 4f)] public float footprintY = 0.5f;
        [Range(0.4f, 8f)] public float footprintZ = 0.8f;
        [Range(0f, 45f)] public float maxSlope = 22f;

        static ASHSnareSettings _i;
        public static ASHSnareSettings I
        {
            get
            {
                if (!_i) { _i = Resources.Load<ASHSnareSettings>("ASH_SnareSettings"); if (!_i) _i = CreateInstance<ASHSnareSettings>(); }
                return _i;
            }
        }
    }
}

using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Put on any object (a stove prefab, fireplace) to make it a cook station at runtime: same fuel, cook menu,
    /// cooking and warmth as a campfire. Objects whose names match ASH_CookingSettings.stoveNamePatterns get one automatically.</summary>
    [DisallowMultipleComponent]
    public sealed class ASHCookStove : MonoBehaviour
    {
        public string stationName = "Stove";
        [Tooltip("Starts lit (needs startFuelHours > 0).")]
        public bool startLit = false;
        [Range(0f, 48f)] public float startFuelHours = 0f;
        void Awake() => ASHCookStoves.Convert(gameObject, stationName, startLit, startFuelHours);
    }
}

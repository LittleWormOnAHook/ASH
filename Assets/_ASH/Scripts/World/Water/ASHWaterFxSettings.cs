using UnityEngine;

namespace ASH.World.Water
{
    /// <summary>pt635u: water FX switches. Editable in Play (the runtime driver re-applies on change). Asset: Resources/ASH_WaterFxSettings.</summary>
    [CreateAssetMenu(menuName = "ASH/Settings/Water FX Settings", fileName = "ASH_WaterFxSettings")]
    public class ASHWaterFxSettings : ScriptableObject
    {
        public const string ResPath = "ASH_WaterFxSettings";
        static ASHWaterFxSettings _i;
        public static ASHWaterFxSettings I { get { if (!_i) { _i = Resources.Load<ASHWaterFxSettings>(ResPath); if (!_i) _i = CreateInstance<ASHWaterFxSettings>(); } return _i; } }

        [Tooltip("Gaia underwater effects (fog, caustics, post FX, underwater audio). Off = the camera never switches to the underwater look.")]
        public bool underwaterEffects = false;
        [Tooltip("When off, also disable the Gaia Underwater Effects component itself (not just its effects).")]
        public bool disableComponentToo = true;
        [Tooltip("Seconds between checks for a changed setting / newly spawned underwater component.")] [Range(0.1f, 10f)] public float recheckInterval = 1f;
    }
}

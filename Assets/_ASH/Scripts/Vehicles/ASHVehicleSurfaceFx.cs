using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>t405/t406: NWH's own wheel smoke and surface-dust prefabs plus the ash/snow dust tint, read by the runtime
    /// GroundDetection (Resources/ASH_VehicleSurfaceFx). Edit the asset to tune; it is created automatically if missing.</summary>
    [CreateAssetMenu(menuName = "ASH/Vehicles/Surface FX")]
    public sealed class ASHVehicleSurfaceFx : ScriptableObject
    {
        public const string ResPath = "ASH_VehicleSurfaceFx";
        [Tooltip("NWH SkidSmoke.prefab (wheel-slip smoke).")] public GameObject smokePrefab;
        [Tooltip("NWH SurfaceDust.prefab (dust kicked up while rolling).")] public GameObject dustPrefab;
        [Tooltip("Dust colour: ash / snow white-grey.")] public Color dustColor = new Color(0.86f, 0.86f, 0.88f, 0.55f);
        [Tooltip("Dust amount on terrain (ash / snow). 0 = none.")] [Range(0f, 20f)] public float terrainDustIntensity = 3f;
        [Tooltip("Dust amount on other ground colliders.")] [Range(0f, 20f)] public float groundDustIntensity = 1f;
        [Tooltip("Wheel-slip smoke amount (NWH default 30). Powdery snow spray rather than tyre smoke at low values.")] [Range(0f, 60f)] public float smokeIntensity = 12f;
    }
}

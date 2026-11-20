using ASH.Interaction;
using UnityEngine;

namespace ASH.Vehicles
{
    /// <summary>On a fuel can world pickup: rolls how full it is the first time the scene starts.</summary>
    [RequireComponent(typeof(ASHItemPickup))]
    public sealed class ASHFuelCanPickup : MonoBehaviour
    {
        [Range(0f, 1f)] public float minFill = 0.25f;
        [Range(0f, 1f)] public float maxFill = 0.7f;
        void Awake() { GetComponent<ASHItemPickup>().SetCondition(Random.Range(Mathf.Min(minFill, maxFill), Mathf.Max(minFill, maxFill))); }
    }
}

using ASH.Inventory;
using ASH.Survival;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>
    /// World objects the player can use with Interact (E). Mirrors Genesis Project.Interaction.IWorldUsable
    /// (GetUsePriority + TryUse) and adds the prompt/hold data the Genesis hold-harvest path carried.
    /// </summary>
    public interface IASHUsable
    {
        /// <summary>Negative = not usable now.</summary>
        float GetUsePriority(in ASHUseContext context);
        string GetPrompt(in ASHUseContext context);
        /// <summary>Seconds Interact must be held (0 = tap).</summary>
        float GetHoldDuration(in ASHUseContext context);
        bool TryUse(in ASHUseContext context);
        Transform UseTransform { get; }
    }

    /// <summary>Snapshot passed to usables. Same idea as Genesis WorldUseContext, without the gatherer/AI fields.</summary>
    public readonly struct ASHUseContext
    {
        public Transform PlayerTransform { get; }
        public Vector3 PlayerPosition { get; }
        public Camera ViewCamera { get; }
        public ASHInventory Inventory { get; }
        public ASHSurvivalStats Stats { get; }
        public float UseRange { get; }
        public Ray ViewRay { get; }
        public RaycastHit? AimHit { get; }

        public ASHUseContext(Transform player, Camera cam, ASHInventory inv, ASHSurvivalStats stats, float useRange, Ray ray, RaycastHit? hit)
        {
            PlayerTransform = player; PlayerPosition = player.position; ViewCamera = cam; Inventory = inv; Stats = stats;
            UseRange = useRange; ViewRay = ray; AimHit = hit;
        }
    }
}

using ASH.Gameplay;
using ASH.Survival;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Keeps Invector's motor stamina (which gates sprint and jump) configured from ASH tuning and mirrors it
    /// into <see cref="ASHSurvivalStats"/> for the HUD. Same role as Genesis PioneerInvectorInputBridge.SyncStamina,
    /// reduced to one direction (motor -> stats) so there is a single source of truth.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class ASHInvectorStaminaLink : MonoBehaviour
    {
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private ASHSurvivalStats stats;

        /// <summary>Extra stamina drain per real second (ash exposure, ASH.Survival.ASHExposure).</summary>
        public static float ExtraDrainPerSecond;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetHooks() => ExtraDrainPerSecond = 0f;

        private void Start() => ApplyTuning();

        public void ApplyTuning()
        {
            if (controller == null || stats == null) return;
            var t = stats.Tuning;
            ApplyClothing();
            controller.jumpStamina = t.jumpStaminaCost;
            controller.currentStamina = Mathf.Min(controller.currentStamina <= 0f ? controller.maxStamina : controller.currentStamina, controller.maxStamina);
        }

        /// <summary>Max stamina (+ clothing bonus) and sprint drain (x clothing stamina-drain multiplier), every FixedUpdate.</summary>
        void ApplyClothing()
        {
            var t = stats.Tuning; var cl = ASHClothing.Current;
            controller.maxStamina = Mathf.Max(1f, t.maxStamina + cl.bonusStamina);
            controller.sprintStamina = t.sprintDrainPerSecond * cl.staminaDrainMul * ASHSurvivalTuning.DrainRate; // Invector drains this per second (accumulative)
            if (controller.currentStamina > controller.maxStamina) controller.currentStamina = controller.maxStamina;
        }

        private void FixedUpdate()
        {
            if (controller == null || stats == null) return;
            ApplyClothing();
            // Invector adds staminaRecovery once per FixedUpdate.
            controller.staminaRecovery = stats.Tuning.staminaRecoveryPerSecond * stats.StaminaRecoveryMultiplier * Time.fixedDeltaTime;
            if (ExtraDrainPerSecond > 0f && controller.currentStamina > 0f) controller.currentStamina = Mathf.Max(0f, controller.currentStamina - ExtraDrainPerSecond * ASHSurvivalTuning.DrainRate * Time.fixedDeltaTime);
        }

        private void LateUpdate()
        {
            if (controller == null || stats == null) return;
#if UNITY_EDITOR
            if (Time.frameCount % 30 == 0) ApplyTuning(); // ASH: live tuning - Play-mode edits to ASH_SurvivalTuning reach Invector
#endif
            stats.SetStaminaFromMotor(controller.currentStamina, controller.maxStamina);
            stats.IsSprinting = controller.isSprinting && controller.input.sqrMagnitude > 0.01f;
        }
    }
}

using System.Collections;
using ASH.Core;
using ASH.Survival;
using Invector.vCharacterController;
using UnityEngine;

namespace ASH.Player
{
    /// <summary>
    /// Minimal fail state: on death, lock input, show the death panel, then respawn at the spawn point with
    /// reset needs. Reduced from Genesis PlayerDeathHandler (no ragdoll/loot/autosave).
    /// </summary>
    public sealed class ASHPlayerDeath : MonoBehaviour
    {
        [SerializeField] private ASHSurvivalStats stats;
        [SerializeField] private vThirdPersonController controller;
        [SerializeField] private Transform respawnPoint;
        [Range(1f, 20f)] [SerializeField] private float respawnDelay = 5f;

        public Transform RespawnPoint { get => respawnPoint; set => respawnPoint = value; }

        private void OnEnable() { if (stats != null) stats.Died += HandleDied; }
        private void OnDisable() { if (stats != null) stats.Died -= HandleDied; }

        private void HandleDied()
        {
            if (controller != null) { controller.input = Vector3.zero; controller.isSprinting = false; }
            // with the save system: fade + "You didn't make it" + Load Last Save / Main Menu (respawn = newest save)
            if (ASHSaveSystem.Instance != null) { ASH.UI.ASHDeathScreen.Show(); return; }
            StartCoroutine(Respawn());
        }

        private IEnumerator Respawn()
        {
            ASHUIState.SetOpen(ASHPanels.Death, true);
            if (controller != null) { controller.input = Vector3.zero; controller.isSprinting = false; }
            yield return new WaitForSeconds(respawnDelay);
            if (respawnPoint != null)
            {
                var rb = GetComponent<Rigidbody>();
                if (rb != null) rb.linearVelocity = Vector3.zero;
                transform.SetPositionAndRotation(respawnPoint.position, respawnPoint.rotation);
            }
            stats.ResetStats();
            ASHUIState.SetOpen(ASHPanels.Death, false);
        }
    }
}

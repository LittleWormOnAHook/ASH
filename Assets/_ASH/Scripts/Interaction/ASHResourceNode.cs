using System.Collections;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>
    /// Hold-to-gather resource (deadwood, stones, berry bush). Lite port of Genesis ResourceNode's
    /// HoldHarvest mode: hold duration, prompt, yield per gather, limited gathers, depletion and optional
    /// respawn. Laser mining waves, scanner highlight and loot-attract VFX removed.
    /// </summary>
    public sealed class ASHResourceNode : MonoBehaviour, IASHUsable
    {
        [SerializeField] private ASHItemDefinition item;
        [SerializeField] private string verb = "Gather";
        [Range(1, 10)] [SerializeField] private int yieldMin = 1;
        [Range(1, 10)] [SerializeField] private int yieldMax = 2;
        [Range(1, 20)] [SerializeField] private int gathers = 2;
        [Range(0f, 10f)] [SerializeField] private float holdSeconds = 1.1f;
        [Tooltip("0 = never respawns.")]
        [Range(0f, 1800f)] [SerializeField] private float respawnSeconds = 0f;
        [Header("Tools")]
        [Tooltip("Tool that must be carried to harvest at all (e.g. hatchet for crates). None = bare hands.")]
        [SerializeField] private ASHToolType requiredTool = ASHToolType.None;
        [Tooltip("Tool that speeds up and increases the yield (e.g. hatchet on deadwood).")]
        [SerializeField] private ASHToolType bonusTool = ASHToolType.None;
        [Range(1f, 4f)] [SerializeField] private float bonusYieldMultiplier = 2f;
        [Range(0.1f, 1f)] [SerializeField] private float bonusHoldMultiplier = 0.5f;
        [Tooltip("Optional extra item per gather (e.g. scrap metal from a crate's nails).")]
        [SerializeField] private ASHItemDefinition extraItem;
        [Range(0f, 1f)] [SerializeField] private float extraChance = 0.35f;
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private AudioClip gatherClip;

        private int _remaining;
        private Collider[] _colliders;
        private Vector3 _baseScale;

        public ASHItemDefinition Item => item;
        public Transform UseTransform => transform;
        public bool Depleted => _remaining <= 0;

        public void Configure(ASHItemDefinition def, string verbText, int minYield, int maxYield, int gatherCount, float hold)
        {
            item = def; verb = verbText; yieldMin = minYield; yieldMax = maxYield; gathers = gatherCount; holdSeconds = hold;
        }

        public void ConfigureTools(ASHToolType required, ASHToolType bonus, float yieldMul = 2f, float holdMul = 0.5f, ASHItemDefinition extra = null, float chance = 0.35f)
        {
            requiredTool = required; bonusTool = bonus; bonusYieldMultiplier = yieldMul; bonusHoldMultiplier = holdMul; extraItem = extra; extraChance = chance;
        }

        bool HasBonus(in ASHUseContext c) => bonusTool != ASHToolType.None && c.Inventory != null && c.Inventory.HasTool(bonusTool);

        private void Awake()
        {
            _remaining = gathers;
            _colliders = GetComponentsInChildren<Collider>();
            if (visualRoot == null) visualRoot = gameObject;
            _baseScale = visualRoot.transform.localScale;
        }

        public float GetUsePriority(in ASHUseContext c) => Depleted || item == null ? -1f : 50f;
        public ASHToolType RequiredTool => requiredTool;
        public float GetHoldDuration(in ASHUseContext c) => holdSeconds * (HasBonus(c) ? bonusHoldMultiplier : 1f);

        public string GetPrompt(in ASHUseContext c)
        {
            if (item == null) return null;
            if (requiredTool != ASHToolType.None && (c.Inventory == null || !c.Inventory.HasTool(requiredTool))) return $"Needs {ASHCrafting.ToolName(requiredTool)} to {verb.ToLowerInvariant()}";
            if (c.Inventory != null && !c.Inventory.HasSpaceFor(item, 1)) return $"Pack full ({item.displayName})";
            return HasBonus(c) ? $"{verb} {item.displayName} (hatchet)" : $"{verb} {item.displayName}";
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (Depleted || item == null || c.Inventory == null) return false;
            if (requiredTool != ASHToolType.None && !c.Inventory.HasTool(requiredTool)) return false;
            int amount = Random.Range(yieldMin, yieldMax + 1);
            if (HasBonus(c)) amount = Mathf.CeilToInt(amount * bonusYieldMultiplier);
            if (extraItem != null && Random.value < extraChance) c.Inventory.AddItem(extraItem, 1);
            int added = c.Inventory.AddItem(item, amount);
            if (added <= 0) return false;
            _remaining--;
            if (gatherClip != null) AudioSource.PlayClipAtPoint(gatherClip, transform.position, 0.8f);
            StopAllCoroutines();
            StartCoroutine(_remaining <= 0 ? Deplete() : Punch());
            return true;
        }

        private IEnumerator Punch()
        {
            var t = visualRoot.transform;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.25f)
            {
                t.localScale = _baseScale * (1f - 0.08f * Mathf.Sin(k * Mathf.PI));
                yield return null;
            }
            t.localScale = _baseScale;
        }

        private IEnumerator Deplete()
        {
            var t = visualRoot.transform;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.35f)
            {
                t.localScale = Vector3.Lerp(_baseScale, _baseScale * 0.05f, k * k);
                yield return null;
            }
            foreach (var col in _colliders) if (col != null) col.enabled = false;
            visualRoot.SetActive(false);
            if (respawnSeconds <= 0f) yield break;
            yield return new WaitForSeconds(respawnSeconds);
            t.localScale = _baseScale;
            visualRoot.SetActive(true);
            foreach (var col in _colliders) if (col != null) col.enabled = true;
            _remaining = gathers;
        }
    }
}

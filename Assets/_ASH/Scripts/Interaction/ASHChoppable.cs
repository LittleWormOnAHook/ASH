using System.Collections;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Interaction
{
    public enum ASHChopKind { StandingTree, DownedTree, Log, Stump }

    /// <summary>
    /// Axe harvesting (dead trees, downed trees, logs, stumps). Hold Interact with a hatchet (any Axe-role item) in the pack:
    /// each completed hold is one chop. When the hits run out a standing tree falls (animated, away from the player) and is
    /// replaced by log pieces; downed trees split into log pieces; logs and stumps give their wood and disappear.
    /// Same IASHUsable flow as ASHResourceNode (prompt, hold, tool requirement). Never gets ash/snow accumulation.
    /// </summary>
    public sealed class ASHChoppable : MonoBehaviour, IASHUsable
    {
        [SerializeField] private ASHChopKind kind = ASHChopKind.Log;
        [SerializeField] private string displayName = "Log";
        [SerializeField] private ASHToolType requiredTool = ASHToolType.Axe;
        [Range(1, 40)] [SerializeField] private int hits = 3;
        [Tooltip("Seconds of held Interact per chop.")]
        [Range(0.1f, 5f)] [SerializeField] private float holdPerHit = 0.7f;

        [Header("Yield")]
        [Tooltip("Given when the last chop lands (logs, stumps; standing trees give it when they fall).")]
        [SerializeField] private List<ASHRecipeIngredient> finalYield = new List<ASHRecipeIngredient>();
        [Tooltip("Random extra per chop (e.g. kindling chips).")]
        [SerializeField] private ASHItemDefinition chipItem;
        [Range(0f, 1f)] [SerializeField] private float chipChance = 0.25f;

        [Header("Fall + split (standing / downed trees)")]
        [SerializeField] private GameObject logPrefab;
        [Tooltip("Log piece length in metres along its local X (prefab pivot on the ground).")]
        [Range(0.5f, 6f)] [SerializeField] private float logLength = 2.5f;
        [Range(0, 8)] [SerializeField] private int maxLogs = 4;
        [Tooltip("Share of the tree height (downed trees: length) that becomes logs.")]
        [Range(0.1f, 1f)] [SerializeField] private float usableHeight = 0.7f;
        [SerializeField] private GameObject stumpPrefab;
        [Range(0.5f, 8f)] [SerializeField] private float fallSeconds = 2.4f;

        [Header("Respawn")]
        [Tooltip("0 = never comes back.")]
        [Range(0f, 7200f)] [SerializeField] private float respawnSeconds = 0f;

        [Header("Presentation")]
        [SerializeField] private GameObject visualRoot;
        [SerializeField] private AudioClip[] chopClips;
        [SerializeField] private AudioClip fallClip;
        [Range(0f, 1f)] [SerializeField] private float volume = 0.85f;

        int _left; bool _busy, _done; Collider[] _cols; Vector3 _baseScale; Quaternion _baseRot; Vector3 _basePos; float _height = 3f, _span = 2f;
        public readonly List<GameObject> Spawned = new List<GameObject>();
        public static event System.Action<ASHChoppable> Felled;

        public ASHChopKind Kind => kind;
        public bool Done => _done;
        public bool Busy => _busy;
        public int HitsLeft => _left;
        /// <summary>Standing trees don't carve the ash surface (their bounds are huge).</summary>
        public bool BlocksSurface => kind != ASHChopKind.StandingTree;
        public Transform UseTransform => transform;

        public void Configure(ASHChopKind k, string name, int hitCount, float hold, ASHItemDefinition chip, float chance, params ASHRecipeIngredient[] yield)
        { kind = k; displayName = name; hits = hitCount; holdPerHit = hold; chipItem = chip; chipChance = chance; finalYield = new List<ASHRecipeIngredient>(yield); }
        public void ConfigureSplit(GameObject log, float length, int max, float usable, GameObject stump, float fall)
        { logPrefab = log; logLength = length; maxLogs = max; usableHeight = usable; stumpPrefab = stump; fallSeconds = fall; }
        public void ConfigurePresentation(GameObject visual, AudioClip[] chops, AudioClip fall) { visualRoot = visual; chopClips = chops; fallClip = fall; }

        void Awake()
        {
            _left = hits; _cols = GetComponentsInChildren<Collider>(true);
            if (!visualRoot) visualRoot = gameObject;
            _baseScale = visualRoot.transform.localScale; _baseRot = visualRoot.transform.localRotation; _basePos = visualRoot.transform.localPosition;
            bool has = false; Bounds b = default;
            foreach (var r in GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            if (has) { _height = Mathf.Max(0.5f, b.max.y - transform.position.y); _span = Mathf.Max(b.size.x, b.size.z); }
        }

        bool HasTool(in ASHUseContext c) => requiredTool == ASHToolType.None || (c.Inventory != null && c.Inventory.HasTool(requiredTool));

        public float GetUsePriority(in ASHUseContext c) => _done || _busy ? -1f : 45f;
        /// <summary>Global swing speed (set by ASHChopAnimator): hold per hit = holdPerHit / SwingSpeed.</summary>
        public static float SwingSpeed = 1f;
        public float GetHoldDuration(in ASHUseContext c) => holdPerHit / Mathf.Max(0.1f, SwingSpeed);

        public string GetPrompt(in ASHUseContext c)
        {
            if (!HasTool(c)) return $"Needs {ASHCrafting.ToolName(requiredTool)} to chop {displayName.ToLowerInvariant()}";
            string verb = kind == ASHChopKind.StandingTree ? "Fell" : kind == ASHChopKind.DownedTree ? "Cut up" : "Chop";
            return hits > 1 ? $"{verb} {displayName} ({hits - _left + 1}/{hits})" : $"{verb} {displayName}";
        }

        public bool TryUse(in ASHUseContext c)
        {
            if (_done || _busy || c.Inventory == null || !HasTool(c)) return false;
            bool last = _left <= 1;
            if (last && (kind == ASHChopKind.Log || kind == ASHChopKind.Stump) && !CanTakeYield(c.Inventory)) return false; // pack full: keep the last chop
            _left--;
            PlayChop(last);
            if (chipItem && Random.value < chipChance) c.Inventory.AddItem(chipItem, 1);
            StopAllCoroutines();
            if (!last) { StartCoroutine(Shake()); return true; }
            _done = true;
            switch (kind)
            {
                case ASHChopKind.StandingTree:
                    GiveYield(c.Inventory);
                    var away = transform.position - c.PlayerPosition; away.y = 0f;
                    if (away.sqrMagnitude < 0.01f) away = transform.forward;
                    StartCoroutine(Fall(away.normalized));
                    break;
                case ASHChopKind.DownedTree:
                    GiveYield(c.Inventory); SpawnLogs(transform.position, LongAxis()); StartCoroutine(Deplete()); break;
                default:
                    GiveYield(c.Inventory); StartCoroutine(Deplete()); break;
            }
            return true;
        }

        bool CanTakeYield(ASHInventory inv) { foreach (var y in finalYield) if (y.item && !inv.HasSpaceFor(y.item, 1)) return false; return true; }
        void GiveYield(ASHInventory inv) { foreach (var y in finalYield) if (y.item) inv.AddItem(y.item, y.amount); }

        void PlayChop(bool last)
        {
            if (chopClips == null || chopClips.Length == 0) return;
            var clip = chopClips[Random.Range(0, chopClips.Length)]; if (!clip) return;
            Play(clip, transform.position + Vector3.up * Mathf.Min(1.2f, _height * 0.5f), volume, last ? 0.8f : Random.Range(0.92f, 1.08f));
        }

        static void Play(AudioClip clip, Vector3 pos, float vol, float pitch)
        {
            var go = new GameObject("ASH_ChopAudio"); go.transform.position = pos;
            var a = go.AddComponent<AudioSource>(); a.clip = clip; a.volume = vol; a.pitch = pitch; a.spatialBlend = 1f; a.minDistance = 3f; a.maxDistance = 60f; a.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        Vector3 LongAxis()
        {
            // longest horizontal renderer axis (downed trees / logs)
            var r = visualRoot.GetComponentInChildren<MeshFilter>();
            if (!r || !r.sharedMesh) return transform.right;
            var e = r.sharedMesh.bounds.extents; var ax = e.x >= e.z ? r.transform.right : r.transform.forward; ax.y = 0f;
            return ax.sqrMagnitude > 0.001f ? ax.normalized : transform.right;
        }

        IEnumerator Shake()
        {
            var t = visualRoot.transform; var axis = Random.insideUnitSphere; axis.y = 0f; if (axis.sqrMagnitude < 0.01f) axis = Vector3.right;
            float amp = kind == ASHChopKind.StandingTree ? 1.2f : 3f;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.3f) { t.localRotation = _baseRot * Quaternion.AngleAxis(Mathf.Sin(k * Mathf.PI * 3f) * amp * (1f - k), axis.normalized); yield return null; }
            t.localRotation = _baseRot;
        }

        IEnumerator Fall(Vector3 dir)
        {
            _busy = true;
            foreach (var col in _cols) if (col) col.enabled = false; // don't shove the player while it falls
            if (fallClip) Play(fallClip, transform.position + Vector3.up * 2f, volume, Random.Range(0.95f, 1.05f));
            var t = visualRoot.transform; var pivot = transform.position; var axis = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 p0 = t.position; Quaternion r0 = t.rotation; float angle = 0f;
            for (float k = 0f; k < 1f; k += Time.deltaTime / fallSeconds)
            {
                angle = 88f * k * k; // accelerating topple
                var q = Quaternion.AngleAxis(angle, axis);
                t.SetPositionAndRotation(pivot + q * (p0 - pivot), q * r0);
                yield return null;
            }
            var qf = Quaternion.AngleAxis(88f, axis); t.SetPositionAndRotation(pivot + qf * (p0 - pivot), qf * r0);
            if (chopClips != null && chopClips.Length > 0 && chopClips[0]) Play(chopClips[0], pivot + dir * _height * 0.5f, volume, 0.55f); // thud
            yield return new WaitForSeconds(0.35f);
            SpawnLogs(pivot + dir * 0.8f, dir);
            if (stumpPrefab) Spawned.Add(Instantiate(stumpPrefab, pivot, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), transform.parent));
            visualRoot.SetActive(false);
            _busy = false;
            Felled?.Invoke(this);
            if (respawnSeconds > 0f) StartCoroutine(Respawn());
        }

        void SpawnLogs(Vector3 start, Vector3 dir)
        {
            if (!logPrefab || maxLogs <= 0) return;
            float reach = kind == ASHChopKind.DownedTree ? _span : _height;
            int n = Mathf.Clamp(Mathf.FloorToInt(reach * usableHeight / logLength), 1, maxLogs);
            float total = n * logLength + (n - 1) * 0.2f;
            if (kind == ASHChopKind.DownedTree) start -= dir * (total * 0.5f);
            for (int i = 0; i < n; i++)
            {
                var p = start + dir * (logLength * (i + 0.5f) + 0.2f * i);
                var n0 = Vector3.up;
                if (Physics.Raycast(p + Vector3.up * 6f, Vector3.down, out var h, 20f, ~(1 << 13 | 1 << 2), QueryTriggerInteraction.Ignore)) { p = h.point; n0 = h.normal; }
                var fwd = Vector3.ProjectOnPlane(dir, n0).normalized; if (fwd.sqrMagnitude < 0.01f) fwd = dir;
                var rot = Quaternion.LookRotation(fwd, n0) * Quaternion.Euler(0f, -90f + Random.Range(-6f, 6f), 0f); // local X along the trunk
                Spawned.Add(Instantiate(logPrefab, p, rot, transform.parent));
            }
        }

        IEnumerator Deplete()
        {
            _busy = true; var t = visualRoot.transform;
            for (float k = 0f; k < 1f; k += Time.deltaTime / 0.35f) { t.localScale = Vector3.Lerp(_baseScale, _baseScale * 0.05f, k * k); yield return null; }
            foreach (var col in _cols) if (col) col.enabled = false;
            visualRoot.SetActive(false); _busy = false;
            if (respawnSeconds > 0f) StartCoroutine(Respawn());
        }

        IEnumerator Respawn()
        {
            yield return new WaitForSeconds(respawnSeconds);
            var t = visualRoot.transform; t.localScale = _baseScale; t.localRotation = _baseRot; t.localPosition = _basePos;
            visualRoot.SetActive(true); foreach (var col in _cols) if (col) col.enabled = true;
            _left = hits; _done = false;
        }
    }
}

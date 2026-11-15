using ASH.Gameplay;
using UnityEngine;

namespace ASH.Interaction
{
    /// <summary>
    /// Reusable hinged door, opened/closed with Interact (E) through ASHInteractor. The door leaf is a scene object that is
    /// never re-placed: at runtime it is rotated about a hinge line computed from its mesh bounds (no reparenting, no helper
    /// objects saved), and its closed pose is the pose it was placed in. This component lives on its own object and carries
    /// a box collider that follows the leaf (interaction target + blocking collider). Can be locked (prompt + rattle).
    /// No existing door script was reusable: Invector vSimpleDoor is trigger-driven (auto-open), the Survival Pack and
    /// Genesis have none.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHDoor : MonoBehaviour, IASHUsable
    {
        public enum HingeSide { Auto, MinX, MaxX, MinZ, MaxZ }
        public enum SwingMode { AwayFromPlayer, Positive, Negative }

        [Tooltip("The door leaf that swings (scene object, left where it was placed). Empty = this object.")] public Transform leaf;
        [Tooltip("Which edge of the leaf mesh is the hinge (local bounds). Auto = the edge nearest the leaf's pivot.")] public HingeSide hingeSide = HingeSide.Auto;
        [Tooltip("Extra hinge offset in the leaf's local space (metres, before scale).")] public Vector3 hingeOffset = Vector3.zero;
        [Tooltip("Open angle in degrees.")] [Range(5f, 180f)] public float openAngle = 100f;
        public SwingMode swing = SwingMode.AwayFromPlayer;
        [Tooltip("Time to swing fully open/closed (s).")] [Range(0.05f, 10f)] public float swingSeconds = 0.9f;
        [Tooltip("Collider thickness added around the leaf (m) so the interact ray hits this door first.")] [Range(0f, 0.3f)] public float colliderPadding = 0.04f;
        public bool startOpen;

        [Header("Lock")]
        public bool locked;
        public string openPrompt = "Open door", closePrompt = "Close door";
        public string lockedMessage = "It's stuck.";
        [Tooltip("Jiggle when trying a locked door (degrees).")] [Range(0f, 10f)] public float lockedJiggle = 1.6f;
        [Tooltip("Rattle while the ground shakes (degrees at full quake intensity).")] [Range(0f, 15f)] public float quakeRattle = 2.5f;

        [Header("Audio")]
        public AudioClip[] openClips, closeClips, lockedClips, unlockClips;
        [Range(0f, 1f)] public float volume = 0.8f;
        [Range(1f, 100f)] public float audioRange = 18f;

        public bool IsOpen => _targetAngle != 0f;
        public float Angle => _angle;
        public bool Locked => locked;
        public Transform UseTransform => transform;

        Vector3 _p0, _hingeW, _axisW, _centre0; Quaternion _r0; Bounds _mb; bool _init; float _angle, _targetAngle, _vel, _jiggleT, _applied = float.NaN;
        BoxCollider _col; ASH.Opening.ASHEarthquakeEffects _quake; float _nextQuakeFind;

        void Awake() => Init();

        void Init()
        {
            if (_init) return; _init = true;
            if (!leaf) leaf = transform;
            _p0 = leaf.position; _r0 = leaf.rotation;
            var mf = leaf.GetComponent<MeshFilter>();
            _mb = mf && mf.sharedMesh ? mf.sharedMesh.bounds : new Bounds(new Vector3(0.5f, 1f, 0f), new Vector3(1f, 2f, 0.1f));
            var side = hingeSide;
            if (side == HingeSide.Auto)
            {
                // the long horizontal axis of the leaf is the width; the hinge is the edge nearest the pivot
                bool alongX = _mb.size.x >= _mb.size.z;
                side = alongX ? (Mathf.Abs(_mb.min.x) <= Mathf.Abs(_mb.max.x) ? HingeSide.MinX : HingeSide.MaxX)
                              : (Mathf.Abs(_mb.min.z) <= Mathf.Abs(_mb.max.z) ? HingeSide.MinZ : HingeSide.MaxZ);
            }
            var c = _mb.center; var hl = side switch
            {
                HingeSide.MinX => new Vector3(_mb.min.x, 0f, c.z), HingeSide.MaxX => new Vector3(_mb.max.x, 0f, c.z),
                HingeSide.MinZ => new Vector3(c.x, 0f, _mb.min.z), _ => new Vector3(c.x, 0f, _mb.max.z)
            };
            _hingeW = leaf.TransformPoint(hl + hingeOffset);
            _axisW = leaf.up; _centre0 = leaf.TransformPoint(_mb.center);
            if (leaf != transform)
            {
                _col = GetComponent<BoxCollider>(); if (!_col) _col = gameObject.AddComponent<BoxCollider>();
                var s = leaf.lossyScale;
                _col.size = Vector3.Scale(_mb.size, new Vector3(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z))) + Vector3.one * (colliderPadding * 2f);
                _col.center = Vector3.Scale(_mb.center, s);
                _col.isTrigger = false;
            }
            else if (!leaf.GetComponent<Collider>()) { _col = gameObject.AddComponent<BoxCollider>(); _col.center = _mb.center; _col.size = _mb.size + Vector3.one * colliderPadding * 2f; }
            if (leaf.gameObject.isStatic) Debug.LogWarning($"[ASH] Door '{leaf.name}' is marked Static - static batching may stop it from moving visibly. Untick Static on the leaf.");
            if (startOpen) { _targetAngle = _angle = openAngle; }
            Apply(true);
        }

        void Update()
        {
            if (!_init) Init();
            float dt = Time.deltaTime;
            _angle = Mathf.SmoothDamp(_angle, _targetAngle, ref _vel, Mathf.Max(0.02f, swingSeconds * 0.3f), Mathf.Infinity, dt);
            if (Mathf.Abs(_angle - _targetAngle) < 0.01f) { _angle = _targetAngle; _vel = 0f; }
            Apply(false);
        }

        float Wobble()
        {
            float w = 0f;
            if (_jiggleT > 0f) { _jiggleT -= Time.deltaTime; w += Mathf.Sin(_jiggleT * 60f) * lockedJiggle * Mathf.Clamp01(_jiggleT / 0.35f); }
            if (quakeRattle > 0f)
            {
                if (!_quake && Time.time >= _nextQuakeFind) { _nextQuakeFind = Time.time + 2f; _quake = FindAnyObjectByType<ASH.Opening.ASHEarthquakeEffects>(); }
                if (_quake && _quake.Intensity > 0.001f) w += (Mathf.PerlinNoise(Time.time * 9f, 3.1f) - 0.3f) * quakeRattle * _quake.Intensity;
            }
            return w;
        }

        void Apply(bool force)
        {
            float a = _angle + Wobble();
            if (!force && Mathf.Abs(a - _applied) < 0.0005f) return;
            _applied = a;
            var q = Quaternion.AngleAxis(a, _axisW);
            leaf.SetPositionAndRotation(_hingeW + q * (_p0 - _hingeW), q * _r0);
            if (leaf != transform) transform.SetPositionAndRotation(leaf.position, leaf.rotation);
        }

        /// <summary>Signed angle that swings the free edge away from a world point.</summary>
        public float AngleAwayFrom(Vector3 worldPoint, float deg)
        {
            if (!_init) Init();
            // +angle about the leaf's up moves its local +X edge toward local -Z; pick the side opposite the point
            var local = Quaternion.Inverse(_r0) * (worldPoint - _hingeW);
            var free = Quaternion.Inverse(_r0) * (_centre0 - _hingeW); free.y = 0f;
            var probe = Quaternion.AngleAxis(10f, Vector3.up) * free; // where +10 deg moves the centre
            float sideNow = Vector3.Dot(probe - free, local);
            return sideNow > 0f ? -deg : deg;
        }

        public void SetOpen(bool open, Vector3? awayFrom = null)
        {
            if (!_init) Init();
            if (!open) { _targetAngle = 0f; return; }
            float sign = swing == SwingMode.Positive ? 1f : swing == SwingMode.Negative ? -1f : 0f;
            _targetAngle = sign != 0f ? openAngle * sign : (awayFrom.HasValue ? AngleAwayFrom(awayFrom.Value, openAngle) : openAngle);
        }

        /// <summary>Swing to a partial angle (e.g. knocked ajar by the quake), away from a point.</summary>
        public void Ajar(float deg, Vector3 awayFrom) { if (!_init) Init(); _targetAngle = AngleAwayFrom(awayFrom, deg); }
        public void SnapClosed() { if (!_init) Init(); _targetAngle = _angle = 0f; _vel = 0f; Apply(true); }
        public void Lock() { locked = true; }
        public void Unlock(bool sound = true) { if (!locked) return; locked = false; if (sound) Sfx("doorUnlock", unlockClips, 0.8f); }

        void Sfx(string key, AudioClip[] clips, float v) { if (clips != null && clips.Length > 0) ASHSfx.Play(key, clips, transform.position, volume * v, 1f, 0.06f, 1f, audioRange); }

        // ------------------------------------------------------------------ IASHUsable
        public float GetUsePriority(in ASHUseContext context) => isActiveAndEnabled ? 1f : -1f;
        public string GetPrompt(in ASHUseContext context) => IsOpen && !locked ? closePrompt : openPrompt;
        public float GetHoldDuration(in ASHUseContext context) => 0f;
        public bool TryUse(in ASHUseContext context)
        {
            if (locked)
            {
                _jiggleT = 0.35f; Sfx("doorLocked", lockedClips, 1f);
                var hud = FindAnyObjectByType<UI.ASHHud>(); if (hud && !string.IsNullOrEmpty(lockedMessage)) hud.Toast(lockedMessage);
                return true;
            }
            if (IsOpen) { SetOpen(false); Sfx("doorClose", closeClips, 1f); }
            else { SetOpen(true, context.PlayerPosition); Sfx("doorOpen", openClips, 1f); }
            return true;
        }
    }
}

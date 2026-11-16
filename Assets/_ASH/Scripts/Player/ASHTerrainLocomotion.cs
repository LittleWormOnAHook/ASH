using ASH.Survival;
using ASH.World.Accumulation;
using Invector.vCharacterController;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.Player
{
    /// <summary>
    /// Slows the Invector motor by slope (uphill curve, optional downhill bonus, slide/limit on steep ground)
    /// and by ash/snow depth from <see cref="ASHAccumulationController"/>. Climbing (and optionally deep snow)
    /// costs extra stamina, multiplied by <see cref="ASHSurvivalTuning.DrainRate"/>. Auto-attaches to the player.
    /// Does not modify Invector source or the player prefab unless you run the installer to pin a copy.
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(-40)]
    public sealed class ASHTerrainLocomotion : MonoBehaviour
    {
        public static ASHTerrainLocomotion Instance { get; private set; }

        /// <summary>Current blended speed multiplier applied to the motor (1 = normal).</summary>
        public float SpeedMultiplier { get; private set; } = 1f;
        /// <summary>Signed move-direction slope in degrees (positive = uphill).</summary>
        public float SlopeDegrees { get; private set; }
        /// <summary>Ash/snow depth in metres used for the speed curve.</summary>
        public float DepthMetres { get; private set; }
        /// <summary>1 on a packed main path, 0 off it.</summary>
        public float PathFactor { get; private set; }
        /// <summary>Extra stamina drain being applied this frame (before DrainRate), per real second.</summary>
        public float ClimbDrainPerSecond { get; private set; }

        vThirdPersonController _cc;
        ASHTerrainLocomotionSettings S => ASHTerrainLocomotionSettings.I;
        float _savedSlopeLimit = 75f;
        bool _savedUseSlide = true, _savedUseSlopeLimit = true, _appliedLimits;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Hook()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded; SceneManager.sceneLoaded += OnSceneLoaded;
        }
        /// <summary>Also attach after the first scene loads: with Enter Play Mode Options (no scene reload) sceneLoaded never fires on Play.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachAfterLoad() => OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);

        static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            var cc = FindAnyObjectByType<vThirdPersonController>();
            if (cc && !cc.GetComponent<ASHTerrainLocomotion>()) cc.gameObject.AddComponent<ASHTerrainLocomotion>();
        }

        void Awake()
        {
            Instance = this;
            _cc = GetComponent<vThirdPersonController>();
            SpeedMultiplier = 1f;
        }

        void OnEnable()
        {
            if (!_cc) _cc = GetComponent<vThirdPersonController>();
            ApplyLimits();
        }

        void OnDisable()
        {
            RestoreLimits();
            if (_cc) _cc.speedMultiplier = _cc.defaultSpeedMultiplier > 0f ? _cc.defaultSpeedMultiplier : 1f;
            if (Instance == this) Instance = null;
        }

        void ApplyLimits()
        {
            if (!_cc || _appliedLimits) return;
            _savedSlopeLimit = _cc.slopeLimit;
            _savedUseSlide = _cc.useSlide;
            _savedUseSlopeLimit = _cc.useSlopeLimit;
            _cc.slopeLimit = S.slopeLimitDegrees;
            _cc.useSlide = S.allowSlide;
            _cc.useSlopeLimit = true;
            _appliedLimits = true;
        }

        void RestoreLimits()
        {
            if (!_cc || !_appliedLimits) return;
            _cc.slopeLimit = _savedSlopeLimit;
            _cc.useSlide = _savedUseSlide;
            _cc.useSlopeLimit = _savedUseSlopeLimit;
            _appliedLimits = false;
        }

        void FixedUpdate()
        {
            if (!_cc) return;
            // Re-apply limits if settings were tweaked in Play.
            if (_appliedLimits)
            {
                _cc.slopeLimit = S.slopeLimitDegrees;
                _cc.useSlide = S.allowSlide;
            }

            bool moving = _cc.input.sqrMagnitude > 0.01f;
            bool grounded = _cc.isGrounded;
            float target = 1f;
            SlopeDegrees = 0f; DepthMetres = 0f; PathFactor = 0f; ClimbDrainPerSecond = 0f;

            if (!S.groundedOnly || grounded)
            {
                float signed = _cc.GroundAngleFromDirection();
                SlopeDegrees = signed;
                float up = Mathf.Max(0f, signed);
                float down = Mathf.Max(0f, -signed);
                float slopeMul = 1f;
                if (up > S.uphillStartDegrees) slopeMul = Mathf.Max(0f, S.uphillSpeed.Evaluate(up));
                else if (down > 0.5f) slopeMul = Mathf.Max(0f, S.downhillSpeed.Evaluate(down));

                var acc = ASHAccumulationController.Instance;
                if (acc) { DepthMetres = Mathf.Max(0f, acc.GetDepthMetres(transform.position)); PathFactor = Mathf.Clamp01(acc.GetPathFactor(transform.position)); }
                else DepthMetres = Mathf.Max(0f, S.fallbackDepthMetres);
                float depthMul = Mathf.Max(0f, S.depthSpeed.Evaluate(DepthMetres));
                float pathMul = Mathf.Lerp(1f, S.pathSpeedBonus, PathFactor);
                target = slopeMul * depthMul * pathMul;
            }

            target = Mathf.Clamp(target, S.minSpeedMultiplier, S.maxSpeedMultiplier);
            float k = 1f - Mathf.Exp(-S.blendPerSecond * Time.fixedDeltaTime);
            SpeedMultiplier = Mathf.Lerp(SpeedMultiplier, target, k);
            float basis = _cc.defaultSpeedMultiplier > 0f ? _cc.defaultSpeedMultiplier : 1f;
            _cc.speedMultiplier = basis * SpeedMultiplier;

            if (!moving || (S.groundedOnly && !grounded) || _cc.isSliding) return;

            float uphill = Mathf.Max(0f, SlopeDegrees);
            if (uphill >= S.climbStaminaStartDegrees && S.climbStaminaPerSecond > 0f)
                ClimbDrainPerSecond += S.climbStaminaPerSecond * Mathf.Clamp01(uphill / 45f) * Mathf.Clamp01(_cc.input.magnitude);
            if (DepthMetres > 0f && S.depthStaminaPerMetre > 0f)
                ClimbDrainPerSecond += S.depthStaminaPerMetre * DepthMetres * Mathf.Clamp01(_cc.input.magnitude);

            if (ClimbDrainPerSecond > 0f && _cc.currentStamina > 0f)
                _cc.currentStamina = Mathf.Max(0f, _cc.currentStamina - ClimbDrainPerSecond * ASHSurvivalTuning.DrainRate * Time.fixedDeltaTime);
        }
    }
}

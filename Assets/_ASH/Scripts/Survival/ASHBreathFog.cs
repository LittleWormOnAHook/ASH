using ASH.Core;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Cold breath once warmth is at or below the threshold (6): a small particle puff at the Head bone + mouth offset on each
    /// breath (faster when exhausted or sprinting), denser as warmth falls, reduced/hidden under the rag or mask, with a quiet
    /// exhale per puff (muffled under the mask), occasional (every ~8 s at rest). At any temperature: an occasional heavy breath
    /// (every 6-12 s) while stamina is at or below 10, and one recovery breath when a sprint ends. Settings: ASH_TemperatureSettings.
    /// Added to the player with ASHBodyTemperature.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ASHBreathFog : MonoBehaviour
    {
        ASHTemperatureSettings S => ASHTemperatureSettings.I;
        ASHSurvivalStats _stats; ASHBodyTemperature _temp; ASHFaceProtection _face; Transform _head; Animator _anim;
        ParticleSystem _ps; AudioSource _src; AudioLowPassFilter _lp; float _next; static Texture2D _tex;

        void Awake() { _stats = GetComponent<ASHSurvivalStats>(); _temp = GetComponent<ASHBodyTemperature>(); _face = GetComponent<ASHFaceProtection>(); }

        Transform Head()
        {
            if (_head) return _head;
            if (!_anim) _anim = GetComponentInChildren<Animator>();
            if (_anim && _anim.isHuman) _head = _anim.GetBoneTransform(HumanBodyBones.Head);
            return _head;
        }

        Texture PuffTex()
        {
            if (S.breathPuffTexture) return S.breathPuffTexture;
            if (_tex) return _tex;
            const int n = 256;
            _tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "ASH_BreathCloud" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float env = Mathf.Clamp01(1f - r); env = env * env * (3f - 2f * env); env = Mathf.Pow(env, 1.2f);
                float n1 = Mathf.PerlinNoise(x * 0.03f + 5f, y * 0.03f + 3f);
                float n2 = Mathf.PerlinNoise(x * 0.07f + 20f, y * 0.07f + 11f);
                float n3 = Mathf.PerlinNoise(x * 0.16f + 7f, y * 0.16f + 29f);
                float cloud = Mathf.SmoothStep(0.22f, 0.92f, n1 * 0.55f + n2 * 0.3f + n3 * 0.15f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(env * cloud) * 255f));
            }
            _tex.SetPixels32(px); _tex.Apply(false, false); return _tex;
        }

        void ApplyMotion()
        {
            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            // duration can only be set while the system is stopped (Unity errors otherwise)
            if (!_ps.isPlaying) main.duration = 5f;
            main.startLifetime = S.breathPuffLifetime; main.maxParticles = 400; main.startSpeed = 0f; main.gravityModifier = 0f;
            var sz = _ps.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.35f, 0.9f), new Keyframe(1f, Mathf.Max(0.5f, S.breathPuffGrow))));
            var col = _ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.9f, 0.92f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.1f), new GradientAlphaKey(0.35f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            // all three axes must use the same MinMaxCurve mode
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(S.breathPuffRise * 0.55f, S.breathPuffRise * 1.15f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var lim = _ps.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = S.breathPuffDampen; lim.limit = 0.08f;
        }

        void Build()
        {
            var go = new GameObject("ASH_BreathFog"); go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>(); _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ApplyMotion();
            var em = _ps.emission; em.enabled = false;
            var sh = _ps.shape; sh.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh0 = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            var m = new Material(sh0) { name = "ASH_BreathFog (runtime)" };
            var tex = PuffTex(); m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetOverrideTag("RenderType", "Transparent"); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
            if (m.HasProperty("_SrcBlend")) { m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); }
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SoftParticlesEnabled")) { m.SetFloat("_SoftParticlesEnabled", 1f); m.EnableKeyword("_SOFTPARTICLES_ON"); }
            m.renderQueue = 3000;
            r.sharedMaterial = m; r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sortingFudge = -1f;
            _ps.Play();

            var ag = new GameObject("ASH_Exhale"); ag.transform.SetParent(transform, false); ag.transform.localPosition = Vector3.up * 1.6f;
            _src = ag.AddComponent<AudioSource>(); _src.playOnAwake = false; _src.spatialBlend = 0f; _src.loop = false;
            _lp = ag.AddComponent<AudioLowPassFilter>(); _lp.cutoffFrequency = 22000f;
        }

        float _nextHeavy; bool _wasSprinting; float _sprintSince = -1f;

        /// <summary>Breath interval multiplier from movement: idleBreathIntervalMultiplier standing still, walk multiplier in
        /// between, 1 (normal pace) when running / sprinting or while the rag / filter is in its warning window.</summary>
        public static float PaceMultiplier(GameObject player)
        {
            var t = ASHTemperatureSettings.I; if (!player) return 1f;
            var st = player.GetComponent<ASHSurvivalStats>(); if (st && st.IsSprinting) return 1f;
            var face = player.GetComponent<ASHFaceProtection>(); if (face && face.Worn != ASHFaceProtection.Kind.None && face.Low) return 1f;
            var rb = player.GetComponent<Rigidbody>();
            float v = 0f;
            if (rb) { var vel = rb.linearVelocity; vel.y = 0f; v = vel.magnitude; }
            else { var cc = player.GetComponent<CharacterController>(); if (cc) { var vel = cc.velocity; vel.y = 0f; v = vel.magnitude; } }
            if (v <= t.idleSpeed) return t.idleBreathIntervalMultiplier;
            float walkSpeed = Mathf.Lerp(t.idleSpeed, t.runSpeed, 0.4f);
            if (v <= walkSpeed) return Mathf.Lerp(t.idleBreathIntervalMultiplier, t.walkBreathIntervalMultiplier, Mathf.InverseLerp(t.idleSpeed, walkSpeed, v));
            return Mathf.Lerp(t.walkBreathIntervalMultiplier, 1f, Mathf.InverseLerp(walkSpeed, t.runSpeed, v));
        }

        void Update()
        {
            if (!_stats || _stats.IsDead || Time.timeScale <= 0f) return;
            if (!_temp) _temp = GetComponent<ASHBodyTemperature>();
            if (!_face) _face = GetComponent<ASHFaceProtection>();
            var worn = _face ? _face.Worn : ASHFaceProtection.Kind.None;
            float exert = Mathf.Clamp01(Mathf.Max(_stats.IsSprinting ? 1f : 0f, 1f - _stats.Stamina / Mathf.Max(1f, _stats.MaxStamina)));
            bool cold = S.breathFog && _temp && _temp.Warmth <= S.breathFogWarmth;
            float density = cold ? Mathf.Clamp01(Mathf.InverseLerp(S.breathFogWarmth + 0.001f, S.breathFogFullWarmth, _temp.Warmth)) : 0f;
            density = cold ? Mathf.Max(S.breathMinDensity, density) : 0f;
            float mult = worn == ASHFaceProtection.Kind.Mask ? S.maskFogMultiplier : worn == ASHFaceProtection.Kind.Rag ? S.ragFogMultiplier : 1f;

            // heavy breathing (any temperature): one recovery breath when a sprint ends, panting while stamina is low
            bool sprint = _stats.IsSprinting;
            if (sprint && !_wasSprinting) _sprintSince = Time.time;
            if (!sprint && _wasSprinting && S.breathOnSprintStop && _sprintSince >= 0f && Time.time - _sprintSince >= S.sprintStopMinSeconds)
            {
                Breath(density * mult, 1f, worn, S.sprintStopVolume, true);
                _next = Time.time + S.coldBreathEveryExerted; _nextHeavy = Time.time + S.heavyBreathEverySeconds.y;
            }
            _wasSprinting = sprint;
            if (_stats.Stamina <= S.heavyBreathStamina && Time.time >= _nextHeavy)
            {
                var iv = S.heavyBreathEverySeconds;
                _nextHeavy = Time.time + Random.Range(Mathf.Min(iv.x, iv.y), Mathf.Max(iv.x, iv.y));
                Breath(density * mult, 1f, worn, S.heavyBreathVolume, true);
                _next = Time.time + S.coldBreathEveryExerted;
            }

            // cold breath: visible puffs + soft exhale, only at or below the warmth threshold
            if (!cold || Time.time < _next) return;
            float interval = Mathf.Lerp(S.coldBreathEveryRest, S.coldBreathEveryExerted, exert);
            interval *= PaceMultiplier(gameObject); // slower standing still, normal pace running / sprinting / rag-filter warning
            _next = Time.time + interval * (1f + Random.Range(-S.coldBreathJitter, S.coldBreathJitter));
            Breath(density * mult, exert, worn, -1f, false);
        }

        void Breath(float k, float exert, ASHFaceProtection.Kind worn, float volume, bool heavy)
        {
            var head = Head(); if (!head) return;
            if (!_ps) Build();
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized; if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var o = S.mouthOffset; var pos = head.position + right * o.x + Vector3.up * o.y + fwd * o.z;
            if (k > 0.01f)
            {
                ApplyMotion();
                int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(S.breathPuffCountMin, S.breathPuffCountMax, k) * Mathf.Lerp(1f, 1.25f, exert)));
                var c = S.breathPuffColor; float b = S.breathPuffBrightness;
                var tint = new Color(c.r * b, c.g * b, c.b * b, S.breathPuffOpacity * Mathf.Lerp(0.7f, 1f, k));
                for (int i = 0; i < n; i++)
                {
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = pos + Random.insideUnitSphere * 0.025f,
                        velocity = fwd * S.breathPuffSpeed * Mathf.Lerp(0.85f, 1.25f, exert) * Random.Range(0.8f, 1.15f) + Vector3.up * S.breathPuffRise * 0.35f + Random.insideUnitSphere * 0.05f,
                        startSize = Random.Range(S.breathPuffSize.x, S.breathPuffSize.y),
                        startLifetime = S.breathPuffLifetime * Random.Range(0.75f, 1.25f),
                        startColor = tint,
                        rotation = Random.Range(0f, 360f)
                    };
                    _ps.Emit(ep, 1);
                }
            }
            // exhale sound with the puff (also under the mask, muffled)
            var clips = heavy && S.heavyBreathClips != null && S.heavyBreathClips.Length > 0 && System.Array.Exists(S.heavyBreathClips, c => c) ? S.heavyBreathClips : S.exhaleClips;
            if (clips == null || clips.Length == 0) return;
            var clip = Gameplay.ASHSfx.Pick(clips); if (!clip) return;
            _lp.cutoffFrequency = worn == ASHFaceProtection.Kind.Mask ? S.exhaleMaskMuffleHz : worn == ASHFaceProtection.Kind.Rag ? S.exhaleRagMuffleHz : 22000f;
            _src.pitch = Mathf.Lerp(1f, S.exhaleExhaustedPitch, exert) * (1f + Random.Range(-S.exhalePitchJitter, S.exhalePitchJitter));
            float vol = (volume >= 0f ? volume : Mathf.Lerp(S.exhaleVolume, S.exhaleExhaustedVolume, exert)) * (1f + Random.Range(-S.exhaleVolumeJitter, S.exhaleVolumeJitter));
            _src.PlayOneShot(clip, Mathf.Clamp01(vol * ASHGameSettings.CachedSfx));
        }
    }
}

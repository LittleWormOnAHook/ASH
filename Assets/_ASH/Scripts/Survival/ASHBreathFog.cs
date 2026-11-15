using ASH.Core;
using UnityEngine;

namespace ASH.Survival
{
    /// <summary>
    /// Visible breath in cold air: a small lit particle puff at the Head bone + mouth offset on each breath (faster when
    /// exhausted or sprinting), denser as the air gets colder, reduced/hidden under the rag or mask, with a quiet exhale sound
    /// per puff (pitch/volume variation, heavier when exhausted, muffled under the mask). Settings: ASH_TemperatureSettings.
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

        static Texture2D SoftTex()
        {
            if (_tex) return _tex;
            const int n = 64; _tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f; float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r); a = a * a * (3f - 2f * a);
                a *= 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.15f, y * 0.15f);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            _tex.SetPixels32(px); _tex.Apply(); return _tex;
        }

        void Build()
        {
            var go = new GameObject("ASH_BreathFog"); go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>(); _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main; main.playOnAwake = false; main.loop = true; main.duration = 5f; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = S.puffLifetime; main.maxParticles = 64; main.startSpeed = 0f; main.gravityModifier = -0.02f;
            var em = _ps.emission; em.enabled = false;
            var sh = _ps.shape; sh.enabled = false;
            var sz = _ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));
            var col = _ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.4f, 0.55f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var vel = _ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World; vel.y = new ParticleSystem.MinMaxCurve(0.05f, 0.12f); vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            var lim = _ps.limitVelocityOverLifetime; lim.enabled = true; lim.dampen = 0.35f; lim.limit = 0.05f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh0 = Shader.Find("Universal Render Pipeline/Particles/Lit") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit");
            var m = new Material(sh0) { name = "ASH_BreathFog (runtime)" };
            m.SetTexture("_BaseMap", SoftTex()); m.mainTexture = SoftTex();
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

        float EffectiveAmbient()
        {
            if (!_temp) return 20f;
            var ex = ASHExposure.Instance; float sealing = ex ? ex.Sealing : 0f;
            return Mathf.Lerp(_temp.Ambient, Mathf.Max(_temp.Ambient, S.shelterAmbient), sealing);
        }

        void Update()
        {
            if (!S.breathFog || !_stats || _stats.IsDead || Time.timeScale <= 0f) return;
            if (!_temp) _temp = GetComponent<ASHBodyTemperature>();
            if (!_face) _face = GetComponent<ASHFaceProtection>();
            if (Time.time < _next) return;

            float exert = Mathf.Clamp01(Mathf.Max(_stats.IsSprinting ? 1f : 0f, 1f - _stats.Stamina / Mathf.Max(1f, _stats.MaxStamina)));
            float interval = Mathf.Lerp(S.breathIntervalRest, S.breathIntervalExerted, exert);
            _next = Time.time + interval * (1f + Random.Range(-S.breathIntervalJitter, S.breathIntervalJitter));

            float amb = EffectiveAmbient();
            if (amb >= S.breathFogAmbient) return;
            float density = Mathf.Clamp01(Mathf.InverseLerp(S.breathFogAmbient, S.breathFogFullAmbient, amb));
            var worn = _face ? _face.Worn : ASHFaceProtection.Kind.None;
            float mult = worn == ASHFaceProtection.Kind.Mask ? S.maskFogMultiplier : worn == ASHFaceProtection.Kind.Rag ? S.ragFogMultiplier : 1f;
            Puff(density * mult, exert, worn);
        }

        void Puff(float k, float exert, ASHFaceProtection.Kind worn)
        {
            var head = Head(); if (!head) return;
            if (!_ps) Build();
            var fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized; if (fwd.sqrMagnitude < 0.01f) fwd = transform.forward;
            var right = Vector3.Cross(Vector3.up, fwd);
            var o = S.mouthOffset; var pos = head.position + right * o.x + Vector3.up * o.y + fwd * o.z;
            if (k > 0.01f)
            {
                var main = _ps.main; main.startLifetime = S.puffLifetime;
                int n = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(S.puffParticlesMin, S.puffParticlesMax, k) * Mathf.Lerp(1f, 1.4f, exert)));
                var c = S.puffColor;
                for (int i = 0; i < n; i++)
                {
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = pos + Random.insideUnitSphere * 0.015f,
                        velocity = (fwd * S.puffSpeed * Mathf.Lerp(1f, 1.6f, exert) + Random.insideUnitSphere * 0.08f + Vector3.down * 0.03f),
                        startSize = Random.Range(S.puffSize.x, S.puffSize.y),
                        startLifetime = S.puffLifetime * Random.Range(0.75f, 1.15f),
                        startColor = new Color(c.r, c.g, c.b, S.puffAlpha * Mathf.Lerp(0.45f, 1f, k)),
                        rotation = Random.Range(0f, 360f)
                    };
                    _ps.Emit(ep, 1);
                }
            }
            // exhale sound with the puff (also under the mask, muffled)
            if (S.exhaleClips == null || S.exhaleClips.Length == 0) return;
            var clip = Gameplay.ASHSfx.Pick(S.exhaleClips); if (!clip) return;
            _lp.cutoffFrequency = worn == ASHFaceProtection.Kind.Mask ? S.exhaleMaskMuffleHz : worn == ASHFaceProtection.Kind.Rag ? S.exhaleRagMuffleHz : 22000f;
            _src.pitch = Mathf.Lerp(1f, S.exhaleExhaustedPitch, exert) * (1f + Random.Range(-S.exhalePitchJitter, S.exhalePitchJitter));
            float vol = Mathf.Lerp(S.exhaleVolume, S.exhaleExhaustedVolume, exert) * (1f + Random.Range(-S.exhaleVolumeJitter, S.exhaleVolumeJitter));
            _src.PlayOneShot(clip, Mathf.Clamp01(vol * ASHGameSettings.CachedSfx));
        }
    }
}

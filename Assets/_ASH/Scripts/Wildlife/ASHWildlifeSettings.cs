using System;
using System.Collections.Generic;
using ASH.Inventory;
using ASH.Movement;
using UnityEngine;

namespace ASH.Wildlife
{
    public enum ASHWildlifeKind { Wolf, Bear, Fox, Rabbit, Raccoon, Deer, Fawn, Boar, Cougar }
    public enum ASHWildlifeSize { Young, Adult, Alpha }
    public enum ASHWildlifeTemperament { Passive, Hostile, Stalk }
    public enum ASHTrapSize { None, Snare, Large }
    public enum ASHDogWildlifePolicy { Engage, WarnDistractRetreat, Ignore }

    [CreateAssetMenu(menuName = "ASH/Wildlife/Wildlife Settings", fileName = "ASH_WildlifeSettings")]
    public sealed class ASHWildlifeSettings : ScriptableObject
    {
        static ASHWildlifeSettings _i;
        public static ASHWildlifeSettings I => _i ? _i : (_i = Resources.Load<ASHWildlifeSettings>("ASH_WildlifeSettings"));

        [Serializable]
        public sealed class Drop
        {
            public ASHItemDefinition item;
            [Range(0, 20)] public int min = 1;
            [Range(0, 20)] public int max = 1;
            [Range(0f, 1f)] public float chance = 1f;
        }

        [Serializable]
        public sealed class Species
        {
            public ASHWildlifeKind kind;
            public string displayName = "Wolf";
            public ASHWildlifeTemperament temperament = ASHWildlifeTemperament.Hostile;
            public ASHTrapSize trapSize = ASHTrapSize.None;
            public ASHDogWildlifePolicy dogPolicy = ASHDogWildlifePolicy.Engage;
            [Range(1f, 500f)] public float maxHealth = 40f;
            [Range(0f, 80f)] public float damage = 8f;
            [Range(1f, 80f)] public float detectRange = 22f;
            [Range(4f, 80f)] public float stalkRange = 32f;
            [Range(0.5f, 8f)] public float attackRange = 2.2f;
            [Range(0.2f, 8f)] public float walkSpeed = 2.2f;
            [Range(0.2f, 16f)] public float runSpeed = 6.5f;
            [Range(0.2f, 12f)] public float attackCooldown = 1.6f;
            [Range(0.4f, 20f)] public float harvestHold = 3.5f;
            [Range(0.5f, 40f)] public float wanderRadius = 18f;
            [Tooltip("Short flee distance when they only dash and look back.")]
            [Range(2f, 40f)] public float shortFleeMeters = 8f;
            [Range(4f, 80f)] public float longFleeMeters = 22f;
            [Range(0f, 1f)] public float shortFleeChance = 0.4f;
            [Range(0.5f, 2f)] public float baseScale = 1f;
            public bool scaredByTorch = true;
            [Range(2f, 40f)] public float torchScareRadius = 8f;
            [Range(1f, 30f)] public float torchScareSeconds = 6f;
            [Range(0, 12)] public int placeCount = 2;
            [Range(1, 6)] public int packSize = 1;
            [Tooltip("Trot is mid-gait. 0 = halfway between walk and run.")]
            [Range(0f, 14f)] public float trotSpeed = 0f;
            [Tooltip("Ground speed the walk/trot/run clips cover at playback 1. Used so feet stay planted.")]
            [Range(0.05f, 8f)] public float walkClipSpeed = 1f;
            [Range(0.05f, 12f)] public float trotClipSpeed = 2f;
            [Range(0.05f, 20f)] public float runClipSpeed = 4f;
            [Range(0f, 1.2f)] public float bodyRadius = 0f;
            [Range(0f, 2.5f)] public float bodyLength = 0f;
            [Range(0f, 1.8f)] public float bodyHeight = 0f;
            public List<Drop> drops = new List<Drop>();
        }

        [Header("Size variants (Young / Adult / Alpha). Play-editable.")]
        [Range(0.3f, 1f)] public float youngScale = 0.7f;
        [Range(1f, 1.6f)] public float alphaScale = 1.18f;
        [Range(0.2f, 1f)] public float youngHealthMul = 0.6f;
        [Range(1f, 2.5f)] public float alphaHealthMul = 1.45f;
        [Range(0.2f, 1f)] public float youngDamageMul = 0.55f;
        [Range(1f, 2.5f)] public float alphaDamageMul = 1.35f;
        [Range(0.5f, 1.5f)] public float youngSpeedMul = 1.08f;
        [Range(0.5f, 1.5f)] public float alphaSpeedMul = 0.96f;

        public Species wolf, bear, fox, rabbit, raccoon, deer, fawn, boar, cougar;

        [Header("Wrapper prefabs (filled by Setup Wildlife; not committed)")]
        public GameObject wolfPrefab, bearPrefab, foxPrefab, rabbitPrefab, raccoonPrefab, deerPrefab, fawnPrefab, boarPrefab, cougarPrefab;

        public GameObject Prefab(ASHWildlifeKind kind)
        {
            switch (kind)
            {
                case ASHWildlifeKind.Bear: return bearPrefab;
                case ASHWildlifeKind.Fox: return foxPrefab;
                case ASHWildlifeKind.Rabbit: return rabbitPrefab;
                case ASHWildlifeKind.Raccoon: return raccoonPrefab;
                case ASHWildlifeKind.Deer: return deerPrefab;
                case ASHWildlifeKind.Fawn: return fawnPrefab;
                case ASHWildlifeKind.Boar: return boarPrefab;
                case ASHWildlifeKind.Cougar: return cougarPrefab;
                default: return wolfPrefab;
            }
        }

        public void SetPrefab(ASHWildlifeKind kind, GameObject go)
        {
            switch (kind)
            {
                case ASHWildlifeKind.Bear: bearPrefab = go; break;
                case ASHWildlifeKind.Fox: foxPrefab = go; break;
                case ASHWildlifeKind.Rabbit: rabbitPrefab = go; break;
                case ASHWildlifeKind.Raccoon: raccoonPrefab = go; break;
                case ASHWildlifeKind.Deer: deerPrefab = go; break;
                case ASHWildlifeKind.Fawn: fawnPrefab = go; break;
                case ASHWildlifeKind.Boar: boarPrefab = go; break;
                case ASHWildlifeKind.Cougar: cougarPrefab = go; break;
                default: wolfPrefab = go; break;
            }
        }

        [Header("Locomotion (same walk/turn/run setup as the dog; Play-editable)")]
        [Range(0.5f, 30f)] public float acceleration = 6f;
        [Range(30f, 1080f)] public float turnSpeed = 300f;
        [Range(0f, 1f)] public float turnSmoothTime = 0.18f;
        [Range(30f, 180f)] public float turnInPlaceAngle = 100f;
        [Range(5f, 90f)] public float turnInPlaceDone = 30f;
        [Range(0.1f, 1f)] public float arcTurnSlow = 0.55f;
        [Range(30f, 720f)] public float turnBlendFullRate = 160f;
        [Range(0.2f, 3f)] public float animSpeedScale = 1f;
        [Range(0.1f, 1f)] public float animRateMin = 0.6f;
        [Range(1f, 5f)] public float animRateMax = 2.6f;
        [Range(10f, 70f)] public float maxSlope = 45f;
        [Range(0.1f, 1.5f)] public float maxStepUp = 0.45f;
        [Range(0.3f, 4f)] public float whiskerLength = 1.3f;
        [Range(10f, 80f)] public float whiskerAngle = 35f;
        public bool depenetrate = true;
        public LayerMask environmentLayers = ~((1 << 2) | (1 << 5));

        [Header("Traps")]
        [Range(0.4f, 6f)] public float trapProximityRadius = 1.15f;
        [Tooltip("Trapped wolf may break free after this many real seconds.")]
        [Range(5f, 600f)] public float wolfBreakFreeSeconds = 60f;
        [Range(0f, 1f)] public float snareRabbitChance = 0.7f;
        [Range(0f, 1f)] public float snareRaccoonChance = 0.3f;
        [Range(0f, 1f)] public float largeTrapFawnChance = 0.6f;
        [Range(0f, 1f)] public float largeTrapWolfChance = 0.4f;

        [Header("Spawning (Place Far From Camp)")]
        [Tooltip("Nothing is placed this close to the cabin, truck, player, POIs, lights, Gaia Water, porch or tire objects.")]
        [Range(10f, 300f)] public float campExclusionMeters = 80f;
        [Range(4f, 80f)] public float packSpacing = 14f;
        public int spawnSeed = 0;
        [Range(0f, 1f)] public float youngChance = 0.25f;
        [Range(0f, 1f)] public float alphaChance = 0.12f;

        [Header("Dog assist")]
        [Range(8f, 80f)] public float dogAssistRange = 28f;
        [Range(1f, 20f)] public float dogDistractDistance = 5f;
        [Range(1f, 20f)] public float dogDistractRetreatSeconds = 3.5f;

        void OnEnable() => EnsureDefaults();

        public void EnsureDefaults()
        {
            if (wolf == null || wolf.kind != ASHWildlifeKind.Wolf || string.IsNullOrEmpty(wolf.displayName))
                wolf = Sp(ASHWildlifeKind.Wolf, "Wolf", ASHWildlifeTemperament.Stalk, ASHTrapSize.Large, ASHDogWildlifePolicy.Engage, 40, 10, 24, 36, 2.1f, 2.4f, 7.4f, 1.5f, 3.2f, 20, true, 3, 2);
            if (bear == null || bear.kind != ASHWildlifeKind.Bear || string.IsNullOrEmpty(bear.displayName))
                bear = Sp(ASHWildlifeKind.Bear, "Bear", ASHWildlifeTemperament.Hostile, ASHTrapSize.None, ASHDogWildlifePolicy.WarnDistractRetreat, 160, 22, 18, 28, 2.6f, 2.0f, 5.5f, 2.2f, 5.5f, 16, false, 1, 1);
            if (fox == null || fox.kind != ASHWildlifeKind.Fox || fox.displayName == "Wolf")
                fox = Sp(ASHWildlifeKind.Fox, "Fox", ASHWildlifeTemperament.Passive, ASHTrapSize.None, ASHDogWildlifePolicy.Ignore, 18, 0, 16, 16, 1.6f, 2.0f, 8.0f, 2f, 2.8f, 22, true, 2, 1);
            if (rabbit == null || rabbit.kind != ASHWildlifeKind.Rabbit || rabbit.displayName == "Wolf")
                rabbit = Sp(ASHWildlifeKind.Rabbit, "Rabbit", ASHWildlifeTemperament.Passive, ASHTrapSize.Snare, ASHDogWildlifePolicy.Ignore, 8, 0, 12, 12, 1.2f, 1.8f, 9.0f, 2f, 2.0f, 14, true, 6, 1);
            if (raccoon == null || raccoon.kind != ASHWildlifeKind.Raccoon || raccoon.displayName == "Wolf")
                raccoon = Sp(ASHWildlifeKind.Raccoon, "Raccoon", ASHWildlifeTemperament.Passive, ASHTrapSize.Snare, ASHDogWildlifePolicy.Ignore, 14, 0, 11, 11, 1.4f, 1.6f, 6.5f, 2f, 2.6f, 16, true, 3, 1);
            if (deer == null || deer.kind != ASHWildlifeKind.Deer || deer.displayName == "Wolf")
                deer = Sp(ASHWildlifeKind.Deer, "Deer", ASHWildlifeTemperament.Passive, ASHTrapSize.None, ASHDogWildlifePolicy.Ignore, 45, 0, 20, 20, 2.0f, 2.4f, 11f, 2f, 4.0f, 24, true, 3, 1);
            if (fawn == null || fawn.kind != ASHWildlifeKind.Fawn || fawn.displayName == "Wolf")
                fawn = Sp(ASHWildlifeKind.Fawn, "Fawn", ASHWildlifeTemperament.Passive, ASHTrapSize.Large, ASHDogWildlifePolicy.Ignore, 22, 0, 14, 14, 1.6f, 2.0f, 8.5f, 2f, 3.2f, 16, true, 2, 1);
            if (boar == null || boar.kind != ASHWildlifeKind.Boar || boar.displayName == "Wolf")
                boar = Sp(ASHWildlifeKind.Boar, "Boar", ASHWildlifeTemperament.Hostile, ASHTrapSize.None, ASHDogWildlifePolicy.Engage, 80, 14, 16, 22, 2.2f, 2.1f, 8.0f, 1.8f, 4.2f, 18, true, 2, 1);
            if (cougar == null || cougar.kind != ASHWildlifeKind.Cougar || cougar.displayName == "Wolf")
                cougar = Sp(ASHWildlifeKind.Cougar, "Cougar", ASHWildlifeTemperament.Stalk, ASHTrapSize.None, ASHDogWildlifePolicy.Engage, 70, 18, 26, 42, 2.2f, 2.6f, 9.2f, 1.4f, 4.0f, 22, true, 1, 1);
            if (fawn.baseScale < 0.2f) fawn.baseScale = 0.72f;
            if (rabbit.baseScale < 0.2f) rabbit.baseScale = 0.85f;
            if (wolf.placeCount <= 0) wolf.placeCount = 3;
            if (wolf.packSize <= 0) wolf.packSize = 2;
            if (bear.placeCount <= 0) bear.placeCount = 1;
            if (fox.placeCount <= 0) fox.placeCount = 2;
            if (rabbit.placeCount <= 0) rabbit.placeCount = 6;
            if (raccoon.placeCount <= 0) raccoon.placeCount = 3;
            if (deer.placeCount <= 0) deer.placeCount = 3;
            if (fawn.placeCount <= 0) fawn.placeCount = 2;
            if (boar.placeCount <= 0) boar.placeCount = 2;
            if (cougar.placeCount <= 0) cougar.placeCount = 1;
            if (wolf.trapSize == ASHTrapSize.None) wolf.trapSize = ASHTrapSize.Large;
            if (wolf.temperament == ASHWildlifeTemperament.Hostile) wolf.temperament = ASHWildlifeTemperament.Stalk;
            if (fawn.trapSize == ASHTrapSize.None) fawn.trapSize = ASHTrapSize.Large;
            if (rabbit.trapSize == ASHTrapSize.None) rabbit.trapSize = ASHTrapSize.Snare;
            if (raccoon.trapSize == ASHTrapSize.None) raccoon.trapSize = ASHTrapSize.Snare;
        }

        static Species Sp(ASHWildlifeKind k, string name, ASHWildlifeTemperament t, ASHTrapSize trap, ASHDogWildlifePolicy dog,
            float hp, float dmg, float detect, float stalk, float atkR, float walk, float run, float cd, float hold, float wander, bool torch, int count, int pack)
        {
            return new Species
            {
                kind = k, displayName = name, temperament = t, trapSize = trap, dogPolicy = dog,
                maxHealth = hp, damage = dmg, detectRange = detect, stalkRange = stalk, attackRange = atkR,
                walkSpeed = walk, runSpeed = run, attackCooldown = cd, harvestHold = hold, wanderRadius = wander,
                scaredByTorch = torch, placeCount = count, packSize = pack, drops = new List<Drop>()
            };
        }

        public Species Of(ASHWildlifeKind kind)
        {
            EnsureDefaults();
            switch (kind)
            {
                case ASHWildlifeKind.Bear: return bear;
                case ASHWildlifeKind.Fox: return fox;
                case ASHWildlifeKind.Rabbit: return rabbit;
                case ASHWildlifeKind.Raccoon: return raccoon;
                case ASHWildlifeKind.Deer: return deer;
                case ASHWildlifeKind.Fawn: return fawn;
                case ASHWildlifeKind.Boar: return boar;
                case ASHWildlifeKind.Cougar: return cougar;
                default: return wolf;
            }
        }

        public IEnumerable<Species> All()
        {
            EnsureDefaults();
            yield return wolf; yield return bear; yield return fox; yield return rabbit;
            yield return raccoon; yield return deer; yield return fawn; yield return boar; yield return cougar;
        }

        public float Scale(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return 1f;
            float s = sp.baseScale > 0.1f ? sp.baseScale : 1f;
            if (size == ASHWildlifeSize.Young) s *= youngScale;
            else if (size == ASHWildlifeSize.Alpha) s *= alphaScale;
            return s;
        }

        public float Health(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return 1f;
            float h = sp.maxHealth;
            if (size == ASHWildlifeSize.Young) h *= youngHealthMul;
            else if (size == ASHWildlifeSize.Alpha) h *= alphaHealthMul;
            return Mathf.Max(1f, h);
        }

        public float Damage(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return 0f;
            float d = sp.damage;
            if (size == ASHWildlifeSize.Young) d *= youngDamageMul;
            else if (size == ASHWildlifeSize.Alpha) d *= alphaDamageMul;
            return Mathf.Max(0f, d);
        }

        public float Walk(Species sp, ASHWildlifeSize size) => Speed(sp != null ? sp.walkSpeed : 2f, size);
        public float Trot(Species sp, ASHWildlifeSize size)
        {
            if (sp == null) return Speed(3f, size);
            float t = sp.trotSpeed > 0.2f ? sp.trotSpeed : (sp.walkSpeed + sp.runSpeed) * 0.5f;
            return Speed(t, size);
        }
        public float Run(Species sp, ASHWildlifeSize size) => Speed(sp != null ? sp.runSpeed : 6f, size);

        public ASHGroundLocomotion.Tune Loco(Species sp, ASHWildlifeSize size)
        {
            var t = new ASHGroundLocomotion.Tune();
            t.walkSpeed = Walk(sp, size);
            t.trotSpeed = Trot(sp, size);
            t.runSpeed = Run(sp, size);
            if (sp != null)
            {
                t.walkClipSpeed = sp.walkClipSpeed > 0.05f ? sp.walkClipSpeed : 1f;
                t.trotClipSpeed = sp.trotClipSpeed > 0.05f ? sp.trotClipSpeed : 2f;
                t.runClipSpeed = sp.runClipSpeed > 0.05f ? sp.runClipSpeed : 4f;
            }
            t.turnSpeed = turnSpeed;
            t.turnSmoothTime = turnSmoothTime;
            t.acceleration = acceleration;
            t.turnInPlaceAngle = turnInPlaceAngle;
            t.turnInPlaceDone = turnInPlaceDone;
            t.arcTurnSlow = arcTurnSlow;
            t.turnBlendFullRate = turnBlendFullRate;
            t.animSpeedScale = animSpeedScale;
            t.animRateMin = animRateMin;
            t.animRateMax = animRateMax;
            t.maxSlope = maxSlope;
            t.maxStepUp = maxStepUp;
            t.whiskerLength = whiskerLength;
            t.whiskerAngle = whiskerAngle;
            t.depenetrate = depenetrate;
            t.environmentLayers = environmentLayers;
            float sc = Scale(sp, size);
            var body = Body(sp);
            t.bodyRadius = body.x * sc;
            t.bodyLength = body.y * sc;
            t.bodyHeight = body.z * sc;
            return t;
        }

        public Vector3 Body(Species sp)
        {
            if (sp != null && sp.bodyRadius > 0.05f)
                return new Vector3(sp.bodyRadius, sp.bodyLength, sp.bodyHeight);
            switch (sp != null ? sp.kind : ASHWildlifeKind.Wolf)
            {
                case ASHWildlifeKind.Bear: return new Vector3(0.45f, 1.1f, 0.7f);
                case ASHWildlifeKind.Fox: return new Vector3(0.18f, 0.45f, 0.3f);
                case ASHWildlifeKind.Rabbit: return new Vector3(0.12f, 0.25f, 0.16f);
                case ASHWildlifeKind.Raccoon: return new Vector3(0.16f, 0.35f, 0.25f);
                case ASHWildlifeKind.Deer: return new Vector3(0.28f, 0.9f, 0.7f);
                case ASHWildlifeKind.Fawn: return new Vector3(0.2f, 0.55f, 0.45f);
                case ASHWildlifeKind.Boar: return new Vector3(0.32f, 0.7f, 0.45f);
                case ASHWildlifeKind.Cougar: return new Vector3(0.28f, 0.85f, 0.45f);
                default: return new Vector3(0.28f, 0.7f, 0.5f);
            }
        }

        float Speed(float v, ASHWildlifeSize size)
        {
            if (size == ASHWildlifeSize.Young) v *= youngSpeedMul;
            else if (size == ASHWildlifeSize.Alpha) v *= alphaSpeedMul;
            return Mathf.Max(0.2f, v);
        }
    }
}

using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Combat
{
    public enum ASHWeaponKind { Melee, Gun, Bow }

    /// <summary>
    /// One weapon's combat numbers (data-driven: a club is one of these plus a model). Lives in Resources/ASH_Weapons and is
    /// looked up by its ASH item. ASHCombatBridge pushes these onto the Invector vMeleeWeapon / vShooterWeapon on the held
    /// model each time the weapon is drawn, so edits apply on the next equip. Defaults are the Genesis tuning (Wood Axe,
    /// survival rifle, sidearm); the bow has no Genesis row and uses Invector's archery charge.
    /// No swords, no shields, no blocking.
    /// </summary>
    [CreateAssetMenu(menuName = "ASH/Combat/Weapon Data", fileName = "ASH_Weapon_")]
    public sealed class ASHWeaponData : ScriptableObject
    {
        [Tooltip("The ASH item this data belongs to (hatchet, club, rifle, pistol, wooden bow...).")]
        public ASHItemDefinition item;
        public ASHWeaponKind kind = ASHWeaponKind.Melee;

        [Header("Damage")]
        [Tooltip("Base damage per hit (melee) or per shot at close range (guns). Bow: damage of an uncharged arrow.")]
        [Range(0f, 1000f)] public float damage = 26f;
        [Tooltip("+/- random spread on each hit.")]
        [Range(0f, 200f)] public float damageRandom = 4f;
        [Tooltip("Chance of a critical hit (melee).")]
        [Range(0f, 1f)] public float critChance = 0.12f;
        [Range(1f, 10f)] public float critMultiplier = 2f;
        [Tooltip("Poise damage on the target as a share of the damage dealt (Genesis 0.44).")]
        [Range(0f, 5f)] public float poiseFromDamage = 0.44f;

        [Header("Body weight (hit stop, target kick)")]
        [Tooltip("1 = light weapon. At or above the tuning's heavy threshold (Genesis 1.35) hits count as heavy: longer hit stop, bigger kick.")]
        [Range(0.2f, 4f)] public float bodyWeight = 1f;
        [Tooltip("Knockback push on the target (metres-ish; Genesis Wood Axe 2.5).")]
        [Range(0f, 20f)] public float knockback = 2.5f;

        [Header("Melee")]
        [Tooltip("Reach the attack starts from (Invector distanceToAttack; Genesis meleeRange 2.4).")]
        [Range(0.3f, 6f)] public float reach = 2.4f;
        [Range(0f, 200f)] public float staminaCost = 10f;
        [Range(0f, 10f)] public float staminaRecoveryDelay = 1f;
        [Tooltip("Invector attack animation set (AttackID) and locomotion set (MoveSet ID); 0/0 = unarmed-style swings, 1 = one-hand axe/club set.")]
        [Range(0, 20)] public int attackID = 1;
        [Range(0, 20)] public int movesetID = 1;
        [Tooltip("Hit box on the weapon head: size (metres) and how far along the handle it sits (0 = grip, 1 = tip).")]
        public Vector3 hitBoxSize = new Vector3(0.18f, 0.2f, 0.22f);
        [Range(0f, 1f)] public float hitBoxAlong = 0.82f;

        [Header("Ranged")]
        [Tooltip("Off (default): the gun keeps Invector's own handling (spread, recoil, fire rate, projectile speed, clip and reload) copied from its Invector template; only damage comes from this asset. On: the values below replace it.")]
        public bool overrideInvectorHandling;
        [Tooltip("On: keep Invector's damage too (used by the shotgun, which has no Genesis row).")]
        public bool useInvectorDamage;
        [Tooltip("Invector weapon prefab the setup menu copies behaviour from (Assets/Invector-3rdPersonController/...). Change it and re-run the menu to swap.")]
        public string invectorTemplate = "";
        [Tooltip("Range at which damage has fallen to the minimum (Genesis rifle 55 m, pistol 32 m).")]
        [Range(1f, 1000f)] public float range = 55f;
        [Tooltip("Projectile speed (m/s). Bow: speed of a fully drawn arrow.")]
        [Range(1f, 2000f)] public float projectileSpeed = 220f;
        [Tooltip("Hip/aim spread in degrees.")]
        [Range(0f, 30f)] public float dispersion = 0.3f;
        [Tooltip("Seconds between shots.")]
        [Range(0.02f, 10f)] public float shotInterval = 1.1f;
        [Tooltip("Camera kick per shot.")]
        [Range(0f, 10f)] public float cameraKick = 1.2f;

        [Header("Bow (Invector archery)")]
        [Tooltip("Seconds of holding LMB to reach full bow power.")]
        [Range(0.1f, 10f)] public float drawSeconds = 1.1f;
        [Tooltip("Damage multiplier at full power (uncharged = 1).")]
        [Range(1f, 10f)] public float fullDrawDamageMultiplier = 2.5f;
        [Tooltip("Arrow speed multiplier at full power (uncharged speed = projectileSpeed / this).")]
        [Range(1f, 10f)] public float fullDrawSpeedMultiplier = 2f;
        [Tooltip("Loose automatically at full power.")]
        public bool autoLooseAtFullDraw;
        [Tooltip("How deep a stuck arrow sinks in, uncharged and full power (metres).")]
        [Range(0f, 1f)] public float minPenetration = 0.05f;
        [Range(0f, 1f)] public float maxPenetration = 0.2f;

        public float RollDamage(out bool crit)
        {
            crit = critChance > 0f && Random.value < critChance;
            float d = Mathf.Max(0f, damage + Random.Range(-damageRandom, damageRandom));
            return crit ? d * critMultiplier : d;
        }

        static Dictionary<ASHItemDefinition, ASHWeaponData> _byItem;
        public static ASHWeaponData For(ASHItemDefinition item)
        {
            if (item == null) return null;
            if (_byItem == null)
            {
                _byItem = new Dictionary<ASHItemDefinition, ASHWeaponData>();
                foreach (var d in Resources.LoadAll<ASHWeaponData>("ASH_Weapons")) if (d && d.item && !_byItem.ContainsKey(d.item)) _byItem.Add(d.item, d);
            }
            return _byItem.TryGetValue(item, out var r) ? r : null;
        }
        public static void ClearCache() => _byItem = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _byItem = null;
    }
}

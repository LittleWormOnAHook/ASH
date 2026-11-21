using System;
using System.Collections.Generic;
using ASH.Inventory;
using UnityEngine;

namespace ASH.Wildlife
{
    public enum ASHWildlifeKind { Wolf, Bear }

    /// <summary>How the dog treats this animal when helping in Follow or Defend (never Stay).</summary>
    public enum ASHDogWildlifePolicy { Engage, WarnDistractRetreat }

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
            [Range(1f, 500f)] public float maxHealth = 40f;
            [Range(0f, 80f)] public float damage = 8f;
            [Range(1f, 80f)] public float detectRange = 22f;
            [Range(0.5f, 8f)] public float attackRange = 2.2f;
            [Range(0.2f, 8f)] public float walkSpeed = 2.2f;
            [Range(0.2f, 16f)] public float runSpeed = 6.5f;
            [Range(0.2f, 12f)] public float attackCooldown = 1.6f;
            [Range(0.4f, 20f)] public float harvestHold = 3.5f;
            [Range(0.5f, 30f)] public float wanderRadius = 18f;
            public ASHDogWildlifePolicy dogPolicy = ASHDogWildlifePolicy.Engage;
            public bool scaredByTorch = true;
            [Range(2f, 40f)] public float torchScareRadius = 8f;
            [Tooltip("Seconds the animal flees a torch scare pulse.")]
            [Range(1f, 30f)] public float torchScareSeconds = 6f;
            public List<Drop> drops = new List<Drop>();
        }

        public Species wolf = new Species
        {
            kind = ASHWildlifeKind.Wolf, displayName = "Wolf", maxHealth = 40f, damage = 8f,
            detectRange = 24f, attackRange = 2.1f, walkSpeed = 2.4f, runSpeed = 7.2f,
            attackCooldown = 1.5f, harvestHold = 3.2f, wanderRadius = 20f,
            dogPolicy = ASHDogWildlifePolicy.Engage, scaredByTorch = true
        };

        public Species bear = new Species
        {
            kind = ASHWildlifeKind.Bear, displayName = "Bear", maxHealth = 120f, damage = 16f,
            detectRange = 18f, attackRange = 2.6f, walkSpeed = 2.0f, runSpeed = 5.4f,
            attackCooldown = 2.2f, harvestHold = 5.5f, wanderRadius = 16f,
            dogPolicy = ASHDogWildlifePolicy.WarnDistractRetreat, scaredByTorch = false
        };

        [Header("Spawning (Place Far From Camp)")]
        [Range(0, 20)] public int wolfCount = 3;
        [Range(0, 8)] public int bearCount = 1;
        [Tooltip("Nothing is placed this close to the cabin, truck, player, POIs, lights, Gaia Water, porch or tire objects.")]
        [Range(10f, 300f)] public float campExclusionMeters = 80f;
        [Range(4f, 80f)] public float packSpacing = 14f;
        public int spawnSeed = 0;

        [Header("Dog assist")]
        [Tooltip("How far the dog will keep a combat assist target in Follow or Defend.")]
        [Range(8f, 80f)] public float dogAssistRange = 28f;
        [Range(1f, 20f)] public float dogDistractDistance = 5f;
        [Range(1f, 20f)] public float dogDistractRetreatSeconds = 3.5f;

        public Species Of(ASHWildlifeKind kind) => kind == ASHWildlifeKind.Bear ? bear : wolf;
    }
}

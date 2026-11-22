using System.Collections.Generic;
using System.Text;
using ASH.Inventory;
using ASH.Survival;
using ASH.UI;
using ASH.World.Weather;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Totals of everything worn (ASHCharacterEquipment), scaled by each piece's condition, plus set bonuses, capped by
    /// ASH_ClothingSettings. Survival systems read <see cref="Current"/>: body temperature (cold resist), exposure (ash resist,
    /// health / stamina drain), survival stats (max health, hunger / thirst, empty-need drain), Invector stamina (max, sprint drain).
    /// Recomputed when equipment changes and a few times a second (condition, wet, live settings edits).</summary>
    public static class ASHClothing
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHClothing() { UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded; _t = Totals.Neutral; _next = 0f; _dirty = true; _hooked = false; ActiveSets.Clear(); }

        public struct Totals
        {
            public float coldResist01, ashResist01, bonusHealth, bonusStamina, healthDrainMul, staminaDrainMul, hungerMul, thirstMul;
            public int carrySlots; public bool wet;
            public static Totals Neutral => new Totals { healthDrainMul = 1f, staminaDrainMul = 1f, hungerMul = 1f, thirstMul = 1f };
        }

        static Totals _t = Totals.Neutral;
        static float _next;
        static bool _dirty = true, _hooked;
        /// <summary>Set names whose bonus is active right now.</summary>
        public static readonly List<string> ActiveSets = new List<string>();

        public static Totals Current
        {
            get
            {
                if (!_hooked) { _hooked = true; ASHCharacterEquipment.Changed += MarkDirty; }
                if (_dirty || Time.unscaledTime >= _next) Recompute();
                return _t;
            }
        }

        public static void MarkDirty() => _dirty = true;

        /// <summary>Effect share of a piece at this condition.</summary>
        public static float ConditionFactor(float condition)
        {
            var S = ASHClothingSettings.I;
            return Mathf.Lerp(S.effectAtZeroCondition, 1f, Mathf.Clamp01(condition));
        }

        public static bool IsWet()
        {
            var S = ASHClothingSettings.I;
            var dir = ASHWeatherDirector.Instance; if (!dir) return false;
            var k = dir.CurrentKind;
            if (k != ASHWeatherKind.RainAsh && k != ASHWeatherKind.Thunderstorm) return false;
            var ex = ASHExposure.Instance;
            return !ex || ex.Sealing < S.dryWhenSealing;
        }

        static void Recompute()
        {
            _dirty = false; _next = Time.unscaledTime + 0.5f;
            var t = Totals.Neutral; ActiveSets.Clear();
            var S = ASHClothingSettings.I; var eq = ASHCharacterEquipment.Instance;
            if (eq == null) { _t = t; return; }
            var bp = eq[ASHCharacterEquipment.Slot.Backpack];
            t.carrySlots = bp != null && bp.item ? Mathf.Clamp(bp.item.clothing.carrySlots, 0, ASHInventory.MaxBonusPackSlots) : 0;
            if (!S.clothingStatsEnabled) { _t = t; return; }
            bool wet = IsWet(); t.wet = wet;
            float cold = 0f, ash = 0f;
            var setCounts = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var w in eq.WornPieces())
            {
                var it = w.item; if (!it || it.clothing == null) continue;
                var m = it.clothing; float f = ConditionFactor(w.condition);
                cold += m.coldResist * f * (wet && !eq.IsWaterproof(it) ? 1f - S.wetColdPenalty : 1f);
                ash += m.ashResist * f;
                t.bonusHealth += m.bonusHealth * f; t.bonusStamina += m.bonusStamina * f;
                t.healthDrainMul *= Mathf.Lerp(1f, m.healthDrainMul, f);
                t.staminaDrainMul *= Mathf.Lerp(1f, m.staminaDrainMul, f);
                t.hungerMul *= Mathf.Lerp(1f, m.hungerMul, f);
                t.thirstMul *= Mathf.Lerp(1f, m.thirstMul, f);
                if (!string.IsNullOrEmpty(it.clothingSet)) { setCounts.TryGetValue(it.clothingSet, out int c); setCounts[it.clothingSet] = c + 1; }
            }
            if (S.setBonusesEnabled)
                foreach (var kv in setCounts)
                {
                    var sb = S.FindSet(kv.Key); if (sb == null || kv.Value < sb.piecesRequired) continue;
                    ActiveSets.Add(sb.setName);
                    cold += sb.coldResist; ash += sb.ashResist; t.bonusHealth += sb.bonusHealth; t.bonusStamina += sb.bonusStamina;
                    t.staminaDrainMul *= sb.staminaDrainMul; t.healthDrainMul *= sb.healthDrainMul;
                }
            t.coldResist01 = Mathf.Clamp(cold / 100f, 0f, S.maxColdResist);
            t.ashResist01 = Mathf.Clamp(ash / 100f, 0f, S.maxAshResist);
            t.bonusHealth = Mathf.Clamp(t.bonusHealth, -S.maxBonusHealth, S.maxBonusHealth);
            t.bonusStamina = Mathf.Clamp(t.bonusStamina, -S.maxBonusStamina, S.maxBonusStamina);
            float floor = S.minDrainMultiplier;
            t.healthDrainMul = Mathf.Max(floor, t.healthDrainMul); t.staminaDrainMul = Mathf.Max(floor, t.staminaDrainMul);
            t.hungerMul = Mathf.Max(floor, t.hungerMul); t.thirstMul = Mathf.Max(floor, t.thirstMul);
            _t = t;
        }

        /// <summary>One line of totals for the Rook tab.</summary>
        public static string Summary()
        {
            var t = Current; var sb = new StringBuilder();
            sb.Append($"Cold {t.coldResist01 * 100f:0}%   Ash {t.ashResist01 * 100f:0}%");
            if (Mathf.Abs(t.bonusHealth) >= 0.5f) sb.Append($"   HP {t.bonusHealth:+0;-0}");
            if (Mathf.Abs(t.bonusStamina) >= 0.5f) sb.Append($"   ST {t.bonusStamina:+0;-0}");
            if (t.staminaDrainMul < 0.995f || t.staminaDrainMul > 1.005f) sb.Append($"   ST drain {Pct(t.staminaDrainMul)}");
            if (t.healthDrainMul < 0.995f || t.healthDrainMul > 1.005f) sb.Append($"   HP drain {Pct(t.healthDrainMul)}");
            if (t.hungerMul < 0.995f) sb.Append($"   Hunger {Pct(t.hungerMul)}");
            if (t.thirstMul < 0.995f) sb.Append($"   Thirst {Pct(t.thirstMul)}");
            if (t.carrySlots > 0) sb.Append($"   Pack +{t.carrySlots}");
            if (ActiveSets.Count > 0) sb.Append($"   <color=#c9a46a>Set: {string.Join(", ", ActiveSets)}</color>");
            if (t.wet) sb.Append("   <color=#8fb3d9>wet</color>");
            return sb.ToString();
        }

        static string Pct(float mul) => $"{(mul - 1f) * 100f:+0;-0}%";

        /// <summary>Stat line for a tooltip (clothing stats, rarity, value).</summary>
        public static string Describe(ASHItemDefinition it, float condition = 1f)
        {
            if (!it) return "";
            var S = ASHClothingSettings.I; var sb = new StringBuilder();
            string rc = ColorUtility.ToHtmlStringRGB(Color.Lerp(S.RarityColor(it.rarity), Color.white, 0.35f));
            if (it.IsWearable)
            {
                var m = it.clothing;
                sb.Append($"<color=#{rc}>{it.rarity}</color>  ·  {ASHCharacterEquipment.Label(SlotFor(it))}");
                if (m != null)
                {
                    if (m.coldResist > 0f) sb.Append($"  ·  Cold {m.coldResist:0}%");
                    if (m.ashResist > 0f) sb.Append($"  ·  Ash {m.ashResist:0}%");
                    if (m.bonusHealth != 0f) sb.Append($"  ·  HP {m.bonusHealth:+0;-0}");
                    if (m.bonusStamina != 0f) sb.Append($"  ·  ST {m.bonusStamina:+0;-0}");
                    if (m.staminaDrainMul != 1f) sb.Append($"  ·  ST drain {Pct(m.staminaDrainMul)}");
                    if (m.healthDrainMul != 1f) sb.Append($"  ·  HP drain {Pct(m.healthDrainMul)}");
                    if (m.hungerMul != 1f) sb.Append($"  ·  Hunger {Pct(m.hungerMul)}");
                    if (m.thirstMul != 1f) sb.Append($"  ·  Thirst {Pct(m.thirstMul)}");
                    if (m.carrySlots > 0) sb.Append($"  ·  +{m.carrySlots} pack slots");
                    var eqd = ASHCharacterEquipment.Instance; float ph = eqd ? eqd.ProofHours(it) : 0f;
                    if (m.waterproof) sb.Append("  ·  waterproof");
                    else if (ph > 0f) sb.Append($"  ·  waterproofed ({ph:0} wet h)");
                    else if (ASHCharacterEquipment.CanWaterproof(it)) sb.Append("  ·  <color=#a69e91>can be waterproofed</color>");
                }
                if (!string.IsNullOrEmpty(it.clothingSet)) sb.Append($"  ·  {it.clothingSet} set");
                if (condition < 0.999f) sb.Append($"  ·  {condition * 100f:0}%");
            }
            if (it.CanSell && !ASHInventory.IsKeyItem(it)) { if (sb.Length > 0) sb.Append("  ·  "); sb.Append("Value ").Append(ASHEconomy.Format(it.sellValue)); }
            return sb.ToString();
        }

        /// <summary>Character slot a wearable goes to (accessories: first free accessory slot).</summary>
        public static ASHCharacterEquipment.Slot SlotFor(ASHItemDefinition it)
        {
            switch (it ? it.wearSlot : ASHWearSlot.None)
            {
                case ASHWearSlot.Head: return ASHCharacterEquipment.Slot.Head;
                case ASHWearSlot.Face: return ASHCharacterEquipment.Slot.Face;
                case ASHWearSlot.Outerwear: return ASHCharacterEquipment.Slot.Outerwear;
                case ASHWearSlot.Torso: return ASHCharacterEquipment.Slot.Torso;
                case ASHWearSlot.Hands: return ASHCharacterEquipment.Slot.Hands;
                case ASHWearSlot.Legs: return ASHCharacterEquipment.Slot.Legs;
                case ASHWearSlot.Feet: return ASHCharacterEquipment.Slot.Feet;
                case ASHWearSlot.Backpack: return ASHCharacterEquipment.Slot.Backpack;
                default:
                    var eq = ASHCharacterEquipment.Instance;
                    if (eq && !eq[ASHCharacterEquipment.Slot.Accessory0].Empty && eq[ASHCharacterEquipment.Slot.Accessory1].Empty) return ASHCharacterEquipment.Slot.Accessory1;
                    return ASHCharacterEquipment.Slot.Accessory0;
            }
        }

        // ------------------------------------------------------------------ "Wear" from the pack (LMB / context menu)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            ASHHud.UseHandlers.Add(new WearHandler());
            EnsureComponents();
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        }

        static void OnSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m) => EnsureComponents();

        /// <summary>Equipment + wallet live next to the inventory; add them early so a save load restores them.</summary>
        static void EnsureComponents()
        {
            ASHCharacterEquipment.Ensure(); ASHWallet.Ensure(); ASHClothingScatter.Ensure(); ASHClothingVisuals.Ensure();
            var S = ASHClothingSettings.I; if (S.recipes == null || S.recipes.Count == 0) return;
            foreach (var c in Object.FindObjectsByType<ASHCrafting>(FindObjectsInactive.Exclude))
                foreach (var r in S.recipes) if (r) c.AddRecipe(r);
        }

        sealed class WearHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.IsWearable && it.itemId != "wet_rag" && it.itemId != "gas_mask" && it.itemId != "dry_rag";
            public string UseLabel(ASHItemDefinition it) => "Wear";
            public bool Use(ASHItemDefinition it, int slot)
            {
                var eq = ASHCharacterEquipment.Ensure(); if (!eq) return false;
                bool ok = eq.TryEquip(it, slot, SlotFor(it));
                var hud = Object.FindAnyObjectByType<ASHHud>();
                if (hud) hud.Toast(ok ? $"Wearing {it.displayName}." : (eq.Message ?? "Can't wear that."));
                return ok;
            }
        }
    }
}

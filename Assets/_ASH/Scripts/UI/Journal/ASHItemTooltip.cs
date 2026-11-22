using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEngine;

namespace ASH.UI
{
    /// <summary>B0: one shared item hover popup for every item slot or icon (pack, hotbar, cross bar, Rook, Survival Guide recipes and
    /// materials, vendor rows, cook / dog / vehicle rows). Styled like Grandpa's pages (journal paper + PaperInk). Attach with
    /// ASHItemTooltip.Attach(component, () => info); the view (ASHItemTooltipView) shows it after the hover delay, follows the cursor,
    /// clamps to the screen, and also shows beside a slot focused with the gamepad. Settings: Resources/ASH_ItemTooltipSettings.</summary>
    public static class ASHItemTooltip
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics_ASHItemTooltip() { Blockers.Clear(); _inv = null; _craft = null; }

        /// <summary>What a slot shows. Leave fields at their defaults when they don't apply.</summary>
        public struct Info
        {
            public ASHItemDefinition item;
            /// <summary>Stack count in this slot (0 = not a stack, e.g. a recipe output or a vendor row).</summary>
            public int amount;
            /// <summary>Condition / fullness 0..1 of this piece (-1 = unknown, not shown).</summary>
            public float condition;
            /// <summary>Recipe: ingredients have/need, tool, station, water, time, level.</summary>
            public ASHRecipe recipe;
            /// <summary>Recipe not in the guide yet: name hidden, hint shown.</summary>
            public bool recipeLocked;
            /// <summary>Extra price line (vendor: "Buy 1s 20c"). Rich text allowed.</summary>
            public string price;
            /// <summary>Lock line (vendor skill lock...). Shown in the bad ink.</summary>
            public string lockText;
            /// <summary>Free note at the end (use hint...).</summary>
            public string note;
            public bool IsEmpty => !item && !recipe;

            public static Info Item(ASHItemDefinition it, int amount = 0, float condition = -1f) => new Info { item = it, amount = amount, condition = condition };
            public static Info Slot(ASHInventory.Slot s) => s == null || s.IsEmpty ? default : new Info { item = s.item, amount = s.amount, condition = s.condition };
            public static Info Recipe(ASHRecipe r, bool locked = false) => new Info { recipe = r, item = r ? r.outputItem : null, amount = 0, condition = -1f, recipeLocked = locked };
        }

        /// <summary>Anything that should hide the popup (dragging, a context menu, a page flip). True = hidden.</summary>
        public static readonly List<Func<bool>> Blockers = new List<Func<bool>>();
        public static bool Blocked { get { for (int i = 0; i < Blockers.Count; i++) { try { if (Blockers[i] != null && Blockers[i]()) return true; } catch { } } return false; } }

        static ASHInventory _inv; static ASHCrafting _craft;
        /// <summary>Player inventory / crafting used for have/need, tools and break-down yields (the HUD binds them; else found once).</summary>
        public static void Bind(ASHInventory inv, ASHCrafting craft) { if (inv) _inv = inv; if (craft) _craft = craft; }
        static ASHInventory Inv { get { if (!_inv) { var c = Craft; _inv = c ? c.GetComponent<ASHInventory>() : null; if (!_inv) _inv = UnityEngine.Object.FindAnyObjectByType<ASHInventory>(); } return _inv; } }
        static ASHCrafting Craft { get { if (!_craft) _craft = UnityEngine.Object.FindAnyObjectByType<ASHCrafting>(); return _craft; } }

        /// <summary>Adds (or re-points) the hover target on a slot / row. The provider is called when the popup shows and on each refresh.</summary>
        public static ASHTooltipTarget Attach(Component c, Func<Info> provider)
        {
            if (!c) return null;
            var t = c.GetComponent<ASHTooltipTarget>(); if (!t) t = c.gameObject.AddComponent<ASHTooltipTarget>();
            t.provider = provider; return t;
        }
        public static ASHTooltipTarget Attach(Component c, ASHItemDefinition it, int amount = 0, float condition = -1f) => Attach(c, () => Info.Item(it, amount, condition));

        public static void Hide() { var v = ASHItemTooltipView.Current; if (v) v.ForceHide(); }

        // ------------------------------------------------------------------ text
        static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);
        static string Ink(string s, Color c) => $"<color=#{Hex(c)}>{s}</color>";

        public static Color RarityInk(ASHItemRarity r)
        {
            var S = ASHItemTooltipSettings.I;
            return Color.Lerp(ASHClothingSettings.I.RarityColor(r), S.ink, S.rarityInkMix);
        }
        public static Color RarityColor(ASHItemRarity r) => ASHClothingSettings.I.RarityColor(r);

        /// <summary>Coins in the tooltip's inks (readable on parchment).</summary>
        public static string Coins(int copper)
        {
            var S = ASHItemTooltipSettings.I; var E = ASHEconomySettings.I;
            ASHEconomy.Split(copper, out int g, out int s, out int c);
            var sb = new StringBuilder();
            void P(int n, string sh, Color col) { if (sb.Length > 0) sb.Append(' '); sb.Append(Ink($"<b>{n}</b>{sh}", col)); }
            if (g > 0) P(g, E.goldShort, S.goldInk);
            if (s > 0) P(s, E.silverShort, S.silverInk);
            if (c > 0 || sb.Length == 0) P(c, E.copperShort, S.copperInk);
            return sb.ToString();
        }

        static string Pct(float mul) => $"{(mul - 1f) * 100f:+0;-0}%";

        /// <summary>Title (name), sub line (category / rarity / stack) and body for an info. Lines are skipped per settings.</summary>
        public static void Compose(Info info, out string title, out string sub, out string body)
        {
            var S = ASHItemTooltipSettings.I; var it = info.item; var r = info.recipe;
            var sbS = new StringBuilder(); var b = new StringBuilder();
            void Line(string s) { if (string.IsNullOrEmpty(s)) return; if (b.Length > 0) b.Append('\n'); b.Append(s); }
            void Gap() { if (b.Length > 0) b.Append("\n<size=6> </size>"); }
            string Ok(bool ok) => Ink(ok ? "√" : "×", ok ? S.goodInk : S.badInk);

            if (r && info.recipeLocked)
            {
                title = Ink("<b>???</b>", S.dimInk);
                sub = Ink("Not in the guide yet", S.dimInk);
                var man = ASHFieldManual.Instance; var e = man && man.Data ? man.Data.Find(r) : null;
                if (e != null && !string.IsNullOrEmpty(e.hint)) Line(Ink($"<i>{e.hint}</i>", S.ink));
                if (e != null && man)
                {
                    Line(Ink(ASHFieldManual.RequireAll(e) || ASHFieldManual.Conditions(e).Count < 2 ? "Unlocks when you:" : "Unlocks when you do any of:", S.headerInk));
                    foreach (var c in ASHFieldManual.Conditions(e)) Line($"{Ok(man.ConditionMet(c))}  {Ink(man.ConditionText(c), S.ink)}");
                }
                body = b.ToString(); return;
            }

            string name = it ? it.displayName : r ? r.displayName : "";
            if (r && r.outputAmount > 1 && !r.IsUpgrade) name += $"  <size={Mathf.Max(8, S.nameSize - 5)}>x{r.outputAmount}</size>";
            title = Ink($"<b>{name}</b>", it && (S.showRarity) ? RarityInk(it.rarity) : S.ink);

            // sub line: category · rarity · slot · stack
            void SubAdd(string s) { if (string.IsNullOrEmpty(s)) return; if (sbS.Length > 0) sbS.Append("  ·  "); sbS.Append(s); }
            if (r && !it) SubAdd($"Recipe  ·  {r.category}");
            if (it)
            {
                if (S.showCategory) SubAdd(r ? $"Recipe  ·  {r.category}" : it.IsWearable ? ASHCharacterEquipment.Label(ASHClothing.SlotFor(it)) : it.category.ToString());
                if (S.showRarity && (it.IsWearable || it.rarity != ASHItemRarity.Common)) SubAdd(Ink(it.rarity.ToString(), RarityInk(it.rarity)));
                if (S.showStack && !r)
                {
                    int lim = ASHInventory.StackLimit(it);
                    if (info.amount > 0 && lim > 1) SubAdd($"{info.amount}/{lim}");
                    else if (info.amount <= 0 && lim > 1) SubAdd($"stacks to {lim}");
                }
                if (ASHInventory.IsKeyItem(it)) SubAdd(Ink("key item", S.headerInk));
            }
            sub = Ink(sbS.ToString(), S.dimInk);

            // description
            if (S.showDescription)
            {
                string d = r && !string.IsNullOrEmpty(r.description) ? r.description : it ? it.description : "";
                if (!string.IsNullOrEmpty(d)) Line(Ink($"<i>{d}</i>", S.ink));
            }

            if (it && !r)
            {
                // effects
                if (S.showEffects && it.IsConsumable)
                {
                    var e = new List<string>();
                    if (it.restoreHunger > 0) e.Add($"+{it.restoreHunger:0} hunger");
                    if (it.restoreThirst > 0) e.Add($"+{it.restoreThirst:0} thirst");
                    if (it.restoreHealth > 0) e.Add($"+{it.restoreHealth:0} health");
                    if (it.restoreStamina > 0) e.Add($"+{it.restoreStamina:0} stamina");
                    if (e.Count > 0) Line(Ink(string.Join("   ", e), S.goodInk));
                    if (it.sicknessChance > 0f) Line(Ink($"{it.sicknessChance * 100f:0}% chance to make you sick", S.badInk));
                    if (it.requiresOpener) { var inv = Inv; bool has = inv && inv.HasOpener; Line(Ink(has ? "You can open this cleanly." : $"No opener: smashing it spills ~{(1f - it.smashedYield) * 100f:0}%", has ? S.goodInk : S.badInk)); }
                }
                if (S.showEffects && it.toolType != ASHToolType.None) Line(Ink($"Tool  ·  {ASHCrafting.ToolName(it.toolType)}", S.ink));
                if (S.showEffects && it.containerLitres > 0f && info.condition >= 0f) Line(Ink($"Fuel  {it.containerLitres * info.condition:0.#} / {it.containerLitres:0.#} L", S.ink));

                // clothing stats
                if (S.showClothingStats && it.IsWearable && it.clothing != null)
                {
                    var m = it.clothing; var st = new List<string>();
                    if (m.coldResist > 0f) st.Add($"Cold resist {m.coldResist:0}%");
                    if (m.ashResist > 0f) st.Add($"Ash resist {m.ashResist:0}%");
                    if (m.bonusHealth != 0f) st.Add($"Max HP {m.bonusHealth:+0;-0}");
                    if (m.bonusStamina != 0f) st.Add($"Max stamina {m.bonusStamina:+0;-0}");
                    if (m.healthDrainMul != 1f) st.Add($"HP drain {Pct(m.healthDrainMul)}");
                    if (m.staminaDrainMul != 1f) st.Add($"Stamina drain {Pct(m.staminaDrainMul)}");
                    if (m.hungerMul != 1f) st.Add($"Hunger {Pct(m.hungerMul)}");
                    if (m.thirstMul != 1f) st.Add($"Thirst {Pct(m.thirstMul)}");
                    if (m.carrySlots > 0) st.Add($"+{m.carrySlots} pack slots");
                    if (st.Count > 0) { Gap(); Line(Ink(string.Join("   ·   ", st), S.ink)); }
                    var eq = ASHCharacterEquipment.Instance; float ph = eq ? eq.ProofHours(it) : 0f;
                    if (m.waterproof) Line(Ink("Waterproof", S.goodInk));
                    else if (ph > 0f) Line(Ink($"Waterproofed  ·  {ph:0} wet hours left", S.goodInk));
                    else if (ASHCharacterEquipment.CanWaterproof(it)) Line(Ink("Can be waterproofed", S.dimInk));
                    if (!string.IsNullOrEmpty(it.clothingSet)) Line(Ink($"{it.clothingSet} set", S.headerInk));
                }

                // condition
                if (S.showCondition && info.condition >= 0f && it.containerLitres <= 0f && (it.IsWearable || it.maxStack <= 1))
                {
                    float c = Mathf.Clamp01(info.condition);
                    Line(Ink($"Condition {c * 100f:0}%", c >= 0.5f ? S.ink : c >= 0.25f ? S.headerInk : S.badInk));
                }
            }

            // value
            if (S.showValue && it && !r)
            {
                if (ASHInventory.IsKeyItem(it)) Line(Ink("Can't be sold", S.dimInk));
                else if (it.CanSell)
                {
                    string v = $"{Ink("Value", S.dimInk)}  {Coins(it.sellValue)}";
                    if (info.condition >= 0f && info.condition < 0.999f && it.maxStack <= 1) v += Ink($"   (sells for {ASHEconomy.Format(ASHEconomy.SellPrice(it, info.condition), false)})", S.dimInk);
                    Line(v);
                }
            }
            if (!string.IsNullOrEmpty(info.price)) Line(info.price);

            // break down
            if (S.showBreakdown && it && !r && it.CanBreakDown)
            {
                var c = Craft; float f = c ? c.BreakdownYieldFactor(it) : 1f;
                var ys = string.Join(", ", it.breakdownYield.Where(y => y != null && y.item).Select(y => $"{Mathf.Max(1, Mathf.FloorToInt(y.amount * f))} {y.item.displayName}"));
                if (ys.Length > 0) Line($"{Ink("Breaks down", S.headerInk)} {Ink("→ " + ys, S.ink)}");
                if (it.breakdownTool != ASHToolType.None)
                {
                    var inv = Inv; bool has = inv && inv.HasTool(it.breakdownTool);
                    Line(Ink(it.breakdownToolRequired ? $"Needs {ASHCrafting.ToolName(it.breakdownTool)}" : $"Better with {ASHCrafting.ToolName(it.breakdownTool)}", has ? S.goodInk : it.breakdownToolRequired ? S.badInk : S.dimInk));
                }
            }

            // recipe
            if (r && S.showRecipe)
            {
                var inv = Inv; var craft = Craft;
                Gap(); Line(Ink("REQUIRES", S.headerInk));
                foreach (var ing in r.ingredients)
                {
                    if (ing == null || !ing.item) continue;
                    int have = inv ? inv.Count(ing.item) : 0; bool ok = have >= ing.amount;
                    Line($"{Ok(ok)}  {Ink(ing.item.displayName, S.ink)}   {Ink($"{have}/{ing.amount}", ok ? S.dimInk : S.badInk)}");
                }
                if (r.requiredTool != ASHToolType.None) { bool ok = inv && inv.HasTool(r.requiredTool); Line($"{Ok(ok)}  {Ink("Tool: " + ASHCrafting.ToolName(r.requiredTool), S.ink)}"); }
                if (r.station == ASHCraftStation.Campfire) { bool ok = craft && craft.NearStation(ASHCraftStation.Campfire); Line($"{Ok(ok)}  {Ink("At a lit campfire", S.ink)}"); }
                if (r.waterPercent > 0f) { bool ok = ASHCrafting.HasWaterFor(r); Line($"{Ok(ok)}  {Ink($"Water {r.waterPercent:0}%", S.ink)}"); }
                Line(Ink($"Craft time {r.craftSeconds:0.#} s", S.dimInk));
            }
            // level locks
            if (S.showLevelLock && r && r.requiredCraftingLevel > 1)
            {
                int have = ASHSkills.Level(ASHSkills.Crafting); bool ok = have >= r.requiredCraftingLevel;
                Line(Ink($"Needs Crafting lv {r.requiredCraftingLevel}  (you: {have})", ok ? S.dimInk : S.badInk));
            }
            if (S.showLevelLock && !string.IsNullOrEmpty(info.lockText)) Line(Ink(info.lockText, S.badInk));

            // use hint
            if (S.showUseHint && !string.IsNullOrEmpty(info.note)) Line(Ink(info.note, S.dimInk));
            body = b.ToString();
        }
    }
}

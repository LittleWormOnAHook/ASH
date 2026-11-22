using System;
using System.Collections.Generic;
using ASH.Core;
using ASH.Interaction;
using ASH.Inventory;
using ASH.UI;
using UnityEngine;

namespace ASH.Gameplay
{
    /// <summary>Clothing C3: Waterproofing tin and Repair (sinew thread) used from the pack. Registered as ASHHud use handlers.
    /// Waterproofing goes on the worn piece that needs it most (feet first), else a waterproofable piece in the pack.
    /// Repair patches the most worn piece (worn or in the pack): thread + cloth (or hide strips for hide / fur) with a needle or knife.
    /// All numbers in ASH_ClothingSettings.</summary>
    public static class ASHClothingCare
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook() { ASHHud.UseHandlers.Add(new WaterproofHandler()); ASHHud.UseHandlers.Add(new RepairHandler()); }

        static ASHInventory Inv() => UnityEngine.Object.FindAnyObjectByType<ASHInventory>();
        static void Toast(string m) { var h = UnityEngine.Object.FindAnyObjectByType<ASHHud>(); if (h && !string.IsNullOrEmpty(m)) h.Toast(m); }
        static ASHItemDefinition Find(string id) => ASHItemRegistry.I && !string.IsNullOrEmpty(id) ? ASHItemRegistry.I.Find(id) : null;

        // ------------------------------------------------------------------ waterproofing
        public static bool Waterproof()
        {
            var S = ASHClothingSettings.I; var eq = ASHCharacterEquipment.Ensure(); var inv = Inv(); var tin = Find(S.waterproofItemId);
            if (!eq || !inv || !tin || inv.Count(tin) <= 0) return false;
            ASHItemDefinition target = null; float best = float.MaxValue;
            foreach (var w in eq.WornPieces())
            {
                if (!ASHCharacterEquipment.CanWaterproof(w.item)) continue;
                float h = eq.ProofHours(w.item) + (w.item.wearSlot == ASHWearSlot.Feet ? -0.5f : 0f);
                if (h < best && eq.ProofHours(w.item) < S.waterproofMaxHours - 1f) { best = h; target = w.item; }
            }
            if (!target)
                foreach (var sl in inv.Slots)
                    if (!sl.IsEmpty && ASHCharacterEquipment.CanWaterproof(sl.item) && eq.ProofHours(sl.item) < S.waterproofMaxHours - 1f) { target = sl.item; break; }
            if (!target) { Toast("Nothing here needs waterproofing (boots, coats, cloaks and hoods take it)."); return false; }
            if (!inv.Remove(tin, 1)) return false;
            float add = Mathf.Min(S.waterproofHoursPerTin, S.waterproofMaxHours - eq.ProofHours(target));
            eq.AddProof(target, add);
            Toast($"Rubbed waterproofing into your {target.displayName} ({eq.ProofHours(target):0} wet hours).");
            return true;
        }

        sealed class WaterproofHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == ASHClothingSettings.I.waterproofItemId;
            public string UseLabel(ASHItemDefinition it) => "Waterproof gear";
            public bool Use(ASHItemDefinition it, int slot) => Waterproof();
        }

        // ------------------------------------------------------------------ repair
        static bool IsHide(ASHItemDefinition it)
        {
            if (!it) return false;
            string set = it.clothingSet ?? "", n = (it.displayName ?? "").ToLowerInvariant();
            return set.Equals("Fur", StringComparison.OrdinalIgnoreCase) || set.Equals("Hide", StringComparison.OrdinalIgnoreCase)
                || n.Contains("hide") || n.Contains("fur") || n.Contains("skin") || n.Contains("pelt") || n.Contains("leather");
        }

        static ASHItemDefinition FirstCarried(ASHInventory inv, List<string> ids)
        {
            if (ids == null) return null;
            foreach (var id in ids) { var it = Find(id); if (it && inv.Count(it) > 0) return it; }
            return null;
        }

        public static bool Repair()
        {
            var S = ASHClothingSettings.I; var eq = ASHCharacterEquipment.Ensure(); var inv = Inv();
            if (!inv) return false;
            var thread = Find(S.repairThreadId);
            if (!thread || inv.Count(thread) <= 0) { Toast("You need sinew thread to repair clothing."); return false; }
            if (!FirstCarried(inv, S.repairToolIds)) { Toast("You need a bone needle or a knife to repair clothing."); return false; }
            float cap = S.repairMaxCondition - 0.005f;
            // most worn piece: worn first, then the pack
            ASHCharacterEquipment.Worn wTarget = null; int pTarget = -1; float low = cap;
            if (eq) foreach (var w in eq.WornPieces()) if (w.item && w.condition < low) { low = w.condition; wTarget = w; }
            var slots = inv.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i]; if (sl.IsEmpty || !sl.item.IsWearable || sl.item.maxStack > 1) continue;
                if (sl.condition < low) { low = sl.condition; pTarget = i; wTarget = null; }
            }
            var item = wTarget != null ? wTarget.item : (pTarget >= 0 ? slots[pTarget].item : null);
            if (!item) { Toast("Your clothes don't need repairing."); return false; }
            var patch = IsHide(item) ? (FirstCarried(inv, S.repairHideIds) ?? FirstCarried(inv, S.repairClothIds)) : FirstCarried(inv, S.repairClothIds);
            if (!patch) { Toast(IsHide(item) ? "You need hide strips or cloth to patch it." : "You need cloth or rags to patch it."); return false; }
            if (!inv.Remove(thread, 1)) return false;
            if (!inv.Remove(patch, 1)) { inv.AddItem(thread, 1); return false; }
            float before;
            if (wTarget != null) { before = wTarget.condition; wTarget.condition = Mathf.Min(S.repairMaxCondition, wTarget.condition + S.repairAmount); ASHClothing.MarkDirty(); }
            else
            {
                // the patch / thread removal above may have shifted nothing in this slot (wearables never stack), but re-check
                if (pTarget >= inv.Slots.Count || inv.Slots[pTarget].item != item) { Toast("Repair failed."); inv.AddItem(thread, 1); inv.AddItem(patch, 1); return false; }
                before = inv.Slots[pTarget].condition;
                inv.RestoreSlot(pTarget, item, 1, Mathf.Min(S.repairMaxCondition, before + S.repairAmount)); inv.NotifyChanged();
            }
            float after = wTarget != null ? wTarget.condition : inv.Slots[pTarget].condition;
            if (S.repairCraftingXp > 0f) ASHSkills.AddXp(ASHSkills.Crafting, S.repairCraftingXp);
            Toast($"Patched your {item.displayName}: {before * 100f:0}% -> {after * 100f:0}% ({patch.displayName} + {thread.displayName}).");
            if (eq) ASHCharacterEquipment.RaiseChanged();
            return true;
        }

        sealed class RepairHandler : ASHHud.IItemUseHandler
        {
            public bool Handles(ASHItemDefinition it) => it && it.itemId == ASHClothingSettings.I.repairThreadId;
            public string UseLabel(ASHItemDefinition it) => "Repair clothing";
            public bool Use(ASHItemDefinition it, int slot) => Repair();
        }
    }

}

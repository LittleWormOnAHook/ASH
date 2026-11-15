using System.Collections;
using System.Reflection;
using System.Text;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Player;
using ASH.UI;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ASH.DebugTools
{
    /// <summary>Pack size + item menu test (Play Mode, exec ASH.DebugTools.ASHInventoryMenuTest.Run): 22 pack slots in the grid,
    /// old 12-slot layouts migrate with the cross bar kept, RMB menu Use / Drop 1 / Drop all / Split, Use disabled for materials,
    /// dropped pickups on the ground in front of the player, Esc and click-away close. Logs "[ASH] INVMENU ..." + SUMMARY.</summary>
    public sealed class ASHInventoryMenuTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_InvMenuTest").AddComponent<ASHInventoryMenuTest>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"INVMENU {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
        static ASHItemDefinition Item(string file)
        {
#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{file}.asset");
#else
            return null;
#endif
        }

        IEnumerator Start()
        {
            yield return null;
            var inv = FindAnyObjectByType<ASHInventory>(); var hud = FindAnyObjectByType<ASHHud>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>();
            var bar = Item("EnergyBar"); var kindling = Item("Kindling"); var rifle = Item("HuntingRifle");
            Check("setup", inv && hud && eq && input && bar && kindling && rifle, $"inv={(bool)inv} hud={(bool)hud} eq={(bool)eq} input={(bool)input} items={(bool)bar}/{(bool)kindling}/{(bool)rifle}");
            if (!(inv && hud && eq && input && bar && kindling && rifle)) { Finish(); yield break; }
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand); eq.Unequip(ASHEquipSlot.Hip);

            // ---- slot count + grid
            int P = inv.SlotCount;
            Check("22 pack slots in the grid, cross bar after them", P >= ASHInventory.MinPackSlots && P == 22 && hud.PackSlotCount == P && inv.Slots.Count == P + ASHInventory.CrossCount && inv.CrossStart == P,
                $"pack {P} (min {ASHInventory.MinPackSlots}) grid {hud.PackSlotCount} total {inv.Slots.Count} crossStart {inv.CrossStart}");
            inv.ClearAll(); yield return null;
            for (int i = 0; i < P; i++) inv.AddItem(kindling, kindling.maxStack);
            bool lastUsed = !inv.Slots[P - 1].IsEmpty && inv.Slots[P - 1].item == kindling; bool crossFree = inv.Slots[inv.CrossStart].IsEmpty;
            bool full = !inv.HasSpaceFor(kindling, 1);
            Check("all 22 slots fill before the pack is full (cross bar untouched)", lastUsed && crossFree && full && inv.Count(kindling) == P * kindling.maxStack,
                $"last slot used {lastUsed} cross free {crossFree} full {full} count {inv.Count(kindling)}");
            inv.ClearAll(); yield return null;

            // ---- migration of an old 12 + 4 layout (scene override slotCount 12): pack items stay, cross bar stays on the cross bar
            var f = typeof(ASHInventory).GetField("slots", BindingFlags.NonPublic | BindingFlags.Instance);
            var ensure = typeof(ASHInventory).GetMethod("EnsureSlots", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = (System.Collections.Generic.List<ASHInventory.Slot>)f.GetValue(inv);
            list.Clear(); for (int i = 0; i < 16; i++) list.Add(new ASHInventory.Slot());
            list[3].item = bar; list[3].amount = 2; list[12].item = rifle; list[12].amount = 1;   // old weapon cross slot = 12
            ensure.Invoke(inv, null);
            var w = inv.CrossSlot(ASHInventory.CrossQuad.Weapon);
            Check("old 12-slot layout migrates to 22 (cross bar kept)", inv.Slots.Count == P + 4 && inv.Slots[3].item == bar && inv.Slots[3].amount == 2 && w.item == rifle && inv.Slots[12].IsEmpty,
                $"total {inv.Slots.Count} slot3 {(inv.Slots[3].item ? inv.Slots[3].item.displayName : "-")} weapon {(w.item ? w.item.displayName : "-")} slot12 empty {inv.Slots[12].IsEmpty}");
            inv.ClearAll(); yield return null;

            // ---- open the pack
            if (!hud.InventoryOpen) { input.SimInventory = true; yield return null; yield return null; }
            Check("pack opens", hud.InventoryOpen, $"open {hud.InventoryOpen}");

            // ---- menu for a stack of food
            inv.AddItem(bar, 4); inv.AddItem(kindling, 3); yield return null;
            int bi = -1, ki = -1; for (int i = 0; i < inv.Slots.Count; i++) { if (inv.Slots[i].item == bar) bi = i; if (inv.Slots[i].item == kindling) ki = i; }
            bool opened = hud.OpenContext(bi); string txt = hud.ContextText();
            Check("RMB menu on food: Use, Drop 1, Drop all, Split", opened && hud.ContextOpen && txt.Contains(":1|Drop 1:1|Drop all  x4:1|Split stack:1|") && !txt.StartsWith("Use:0"), $"menu [{txt}]");
            int b0 = inv.Count(bar); bool used = hud.ContextPick("use"); yield return null;
            Check("Use eats one (existing use path)", used && inv.Count(bar) == b0 - 1 && !hud.ContextOpen, $"bars {b0}->{inv.Count(bar)} closed {!hud.ContextOpen}");

            hud.OpenContext(ki); string ktxt = hud.ContextText(); bool kuse = hud.ContextPick("use");
            Check("Use disabled for a crafting material", ktxt.StartsWith("Use:0|") && !kuse, $"menu [{ktxt}] use ran {kuse}");
            hud.CloseContext();

            // ---- drop 1 and drop all
            var who = eq.Animator ? eq.Animator.transform : inv.transform;
            int pk0 = FindObjectsByType<ASHItemPickup>(FindObjectsSortMode.None).Length; int bb = inv.Count(bar);
            hud.OpenContext(bi); bool d1 = hud.ContextPick("drop1"); yield return null;
            var drops = new System.Collections.Generic.List<ASHItemPickup>();
            foreach (var p in FindObjectsByType<ASHItemPickup>(FindObjectsSortMode.None)) if (p.name.EndsWith("_Dropped")) drops.Add(p);
            var one = drops.Find(p => p.Item == bar && p.Amount == 1);
            float dist = one ? Vector3.Distance(Vector3.ProjectOnPlane(one.transform.position - who.position, Vector3.up), Vector3.zero) : -1f;
            float front = one ? Vector3.Dot((one.transform.position - who.position).normalized, who.forward) : -1f;
            float gap = one ? GroundGap(one.transform.position, who) : 99f;
            Check("Drop 1: pickup on the ground in front, one leaves the pack", d1 && one && inv.Count(bar) == bb - 1 && dist > 0.6f && dist < 1.8f && front > 0.5f && gap < 0.15f,
                $"dropped {d1} pickup {(one ? one.name : "none")} x{(one ? one.Amount : 0)} bars {bb}->{inv.Count(bar)} dist {dist:F2} m front {front:F2} ground gap {gap:F2} m");
            int rest = inv.Count(bar);
            hud.OpenContext(bi); bool da = hud.ContextPick("dropall"); yield return null;
            ASHItemPickup all = null; foreach (var p in FindObjectsByType<ASHItemPickup>(FindObjectsSortMode.None)) if (p.name.EndsWith("_Dropped") && p.Item == bar && p.Amount == rest) all = p;
            Check("Drop all: whole stack as one pickup, slot empty", da && all && inv.Count(bar) == 0 && inv.Slots[bi].IsEmpty, $"dropped {da} pickup x{(all ? all.Amount : 0)} (stack was {rest}) left {inv.Count(bar)}");
            Check("dropped pickup uses the item's world pickup prefab when set", all && (bar.worldPickupPrefab == null || all.name.StartsWith(bar.worldPickupPrefab.name)),
                $"prefab {(bar.worldPickupPrefab ? bar.worldPickupPrefab.name : "none (runtime fallback)")} spawned {(all ? all.name : "-")}");

            // ---- Esc and click-away close
            hud.OpenContext(ki); input.SimPauseFrame = Time.frameCount + 1; yield return null; yield return null;
            bool escClosed = !hud.ContextOpen; bool packStill = hud.InventoryOpen;
            Check("Esc closes the menu (not the pack, no pause)", escClosed && packStill && Time.timeScale > 0f, $"menu closed {escClosed} pack open {packStill} timeScale {Time.timeScale}");
            hud.OpenContext(ki); var blk = GameObject.Find("ItemMenuBlocker");
            if (blk && EventSystem.current) ExecuteEvents.Execute(blk, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerDownHandler);
            yield return null;
            Check("click-away closes the menu", blk && !hud.ContextOpen, $"blocker {(bool)blk} open {hud.ContextOpen}");

            // ---- split from the menu (old RMB action)
            hud.OpenContext(ki); bool sp = hud.ContextPick("split"); yield return null;
            int stacks = 0; foreach (var s in inv.Slots) if (s.item == kindling) stacks++;
            Check("Split from the menu", sp && stacks == 2 && inv.Count(kindling) == 3, $"split {sp} stacks {stacks} count {inv.Count(kindling)}");

            // cleanup
            foreach (var p in FindObjectsByType<ASHItemPickup>(FindObjectsSortMode.None)) if (p.name.EndsWith("_Dropped")) Destroy(p.gameObject);
            if (hud.InventoryOpen) { input.SimInventory = true; yield return null; yield return null; }
            Finish();
        }

        static float GroundGap(Vector3 p, Transform who)
        {
            var g = ASHItemDrop.GroundPoint(p, who);
            return Mathf.Abs(p.y - g.y);
        }

        void Finish() { Debug.Log($"[ASH] INVMENU SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}

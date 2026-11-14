using System.Collections;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using ASH.UI;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Cross bar smoke test (Play Mode, exec ASH.DebugTools.ASHCrossBarTest.Run): drag between pack, hotbar and cross bar
    /// with category rules, cross keys (weapon/light/food), hatchet-only-from-quick-slots. Logs "[ASH] CROSS ..." + SUMMARY.</summary>
    public sealed class ASHCrossBarTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_CrossTest").AddComponent<ASHCrossBarTest>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"CROSS {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
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
            var rifle = Item("HuntingRifle"); var rounds = Item("RifleRounds"); var torch = Item("Torch"); var bar = Item("EnergyBar"); var hatchet = Item("Hatchet");
            Check("setup", inv && hud && eq && input && rifle && rounds && torch && bar && hatchet, $"inv={(bool)inv} hud={(bool)hud} eq={(bool)eq}");
            if (!(inv && hud && eq && input && rifle && rounds && torch && bar && hatchet)) { Finish(); yield break; }
            eq.Unequip(ASHEquipSlot.MainHand); eq.Unequip(ASHEquipSlot.OffHand); eq.Unequip(ASHEquipSlot.Hip);
            inv.ClearAll(); yield return null;
            int P = inv.SlotCount, C = inv.CrossStart, H = inv.hotbarSize;
            int W = inv.CrossIndex(ASHInventory.CrossQuad.Weapon), A = inv.CrossIndex(ASHInventory.CrossQuad.Ammo), L = inv.CrossIndex(ASHInventory.CrossQuad.Light), F = inv.CrossIndex(ASHInventory.CrossQuad.Food);
            Check("cross slots", inv.Slots.Count == P + 4 && hud.CrossSlotCount == 4 && C == P, $"slots {inv.Slots.Count} pack {P} hud cross {hud.CrossSlotCount}");

            // fill the hotbar with filler so new items land in the pack area (index >= hotbar)
            var filler = Item("Kindling"); for (int i = 0; i < H; i++) { inv.AddItem(filler, filler.maxStack); }
            inv.AddItem(rifle, 1); inv.AddItem(rounds, 10); inv.AddItem(torch, 1); inv.AddItem(bar, 2); inv.AddItem(hatchet, 1);
            int Find(ASHItemDefinition it) { for (int i = 0; i < inv.Slots.Count; i++) if (inv.Slots[i].item == it) return i; return -1; }
            int ri = Find(rifle), ai = Find(rounds), ti = Find(torch), bi = Find(bar), hi = Find(hatchet);
            Check("pickups land in the pack, not the cross bar", ri >= H && ri < P && ai < P && ti < P && bi < P && hi < P, $"rifle {ri} rounds {ai} torch {ti} bar {bi} hatchet {hi}");

            // hatchet rule
            bool axePack = inv.HasTool(ASHToolType.Axe);
            hud.SimDrag(hi, 0); bool axeHot = inv.HasTool(ASHToolType.Axe); int hiHot = Find(hatchet);
            hud.SimDrag(hiHot, hi); eq.Equip(hatchet); bool axeHeld = inv.HasTool(ASHToolType.Axe); eq.Unequip(ASHEquipSlot.MainHand);
            Check("hatchet counts only on hotbar/cross/held", !axePack && axeHot && axeHeld && hiHot == 0, $"pack={axePack} hotbar={axeHot} held={axeHeld}");

            // pack -> cross, with category rules
            ri = Find(rifle); ai = Find(rounds); ti = Find(torch); bi = Find(bar);
            bool wrong = hud.SimDrag(ri, A);
            bool w = hud.SimDrag(ri, W), a = hud.SimDrag(ai, A), l = hud.SimDrag(ti, L), f = hud.SimDrag(bi, F);
            Check("pack -> cross by category", !wrong && w && a && l && f && hud.CrossText(0).StartsWith(rifle.displayName) && hud.CrossText(1).StartsWith(rounds.displayName) && hud.CrossText(2).StartsWith(torch.displayName) && hud.CrossText(3).StartsWith(bar.displayName),
                $"rifle->ammo refused={!wrong} w={w} a={a} l={l} f={f} cross [{hud.CrossText(0)}] [{hud.CrossText(1)}] [{hud.CrossText(2)}] [{hud.CrossText(3)}]");

            // cross <-> hotbar, swap refused when the hotbar item doesn't fit, cross -> pack
            bool toHot = hud.SimDrag(W, 1); bool rifleOnHot = inv.Slots[1].item == rifle && inv.Slots[W].item == filler ? false : inv.Slots[1].item == rifle;
            bool fillerIntoWeapon = inv.Slots[W].item == filler; // must not happen (swap refused)
            bool back = hud.SimDrag(1, W);
            bool refuse = !hud.SimDrag(2, W); // kindling onto the weapon slot
            bool toPack = hud.SimDrag(A, P - 1) && inv.Slots[P - 1].item == rounds; bool ammoBack = hud.SimDrag(P - 1, A);
            Check("cross <-> hotbar <-> pack", !toHot && !fillerIntoWeapon && refuse && toPack && ammoBack && inv.Slots[W].item == rifle,
                $"weapon->hotbar(kindling there) refused={!toHot} rifleOnHot={rifleOnHot} back={back} kindling->weapon refused={refuse} ammo->pack={toPack} back={ammoBack}");
            // an empty hotbar slot takes the weapon and returns it
            inv.Slots[5].Clear(); bool toEmpty = hud.SimDrag(W, 5) && inv.Slots[5].item == rifle; bool ret = hud.SimDrag(5, W) && inv.Slots[W].item == rifle;
            Check("cross -> empty hotbar -> cross", toEmpty && ret, $"to hotbar {toEmpty} back {ret}");

            // cross keys
            input.SimCross = 0; yield return null; yield return null;
            bool held = eq.Main == rifle;
            input.SimCross = 0; yield return null; yield return null;
            Check("cross weapon key holds/holsters", held && eq.Main == null, $"held={held} after={(eq.Main ? eq.Main.displayName : "-")}");
            input.SimCross = 2; yield return null; yield return null;
            bool lit = eq.Off == torch && eq.HandLightOn;
            input.SimCross = 2; yield return null; yield return null;
            Check("cross light key readies + toggles", lit && eq.Off == torch && !eq.HandLightOn, $"lit={lit} then on={eq.HandLightOn}");
            int b0 = inv.Count(bar); input.SimCross = 3; yield return null; yield return null;
            Check("cross food key eats", inv.Count(bar) == b0 - 1, $"bars {b0}->{inv.Count(bar)}");
            eq.Unequip(ASHEquipSlot.OffHand);
            Finish();
        }

        void Finish() { Debug.Log($"[ASH] CROSS SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}

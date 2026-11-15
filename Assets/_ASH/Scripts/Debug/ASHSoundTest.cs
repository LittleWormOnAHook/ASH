using System.Collections;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using ASH.Player;
using UnityEngine;

namespace ASH.DebugTools
{
    /// <summary>Sound bank coverage (Play Mode, exec ASH.DebugTools.ASHSoundTest.Run, best right after ASHWeaponTest): lists the
    /// bank's clips, then dry-fires the pistol and toggles the torch, flashlight and lantern, and checks every expected sound
    /// key has played this session. Logs "[ASH] SOUND ..." + SUMMARY.</summary>
    public sealed class ASHSoundTest : MonoBehaviour
    {
        public static void Run() { if (Application.isPlaying) new GameObject("ASH_SoundTest").AddComponent<ASHSoundTest>(); }
        int _pass, _fail; readonly StringBuilder _sum = new StringBuilder();
        void Check(string n, bool ok, string d) { if (ok) _pass++; else _fail++; var l = $"SOUND {(ok ? "PASS" : "FAIL")} {n}: {d}"; _sum.AppendLine(l); Debug.Log("[ASH] " + l); }
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
            var b = ASHSoundBank.Instance;
            Check("bank loaded", b, "Resources/ASH_SoundBank");
            if (!b) { Finish(); yield break; }
            string L(AudioClip[] a) => a == null ? "-" : string.Join("|", a.Where(c => c).Select(c => c.name));
            Debug.Log($"[ASH] SOUND bank: bowDraw {L(b.bowDraw)}; bowRelease {L(b.bowRelease)}; arrowFlight {L(b.arrowFlight)}; impact wood {L(b.arrowImpactWood)} snow {L(b.arrowImpactSnow)} rock {L(b.arrowImpactRock)}; rifle {L(b.rifleShot)} bolt {L(b.rifleBolt)}; pistol {L(b.pistolShot)}; dry {L(b.dryFire)}; chop {L(b.axeChop)}; crack {L(b.treeCrack)}; fall {L(b.treeFall)}; torch {L(b.torchOn)}/{L(b.torchOff)}; flashlight {L(b.flashlightOn)}/{L(b.flashlightOff)}; lantern {L(b.lanternOn)}/{L(b.lanternOff)}");
            var inv = FindAnyObjectByType<ASHInventory>(); var eq = FindAnyObjectByType<ASHEquipment>(); var input = FindAnyObjectByType<ASHInput>(); var wa = FindAnyObjectByType<ASHWeaponAim>();
            ASHSfx.LastByKey.Clear(); // this run only
            ASHInput.IgnoreDevices = true;
            if (inv && eq && input && wa)
            {
                // shots: rifle (+ bolt), pistol, bow (draw, release, flight, impact)
                foreach (var (gun, ammo) in new[] { ("HuntingRifle", "RifleRounds"), ("Pistol", "PistolRounds") })
                {
                    var g = Item(gun); var am = Item(ammo); if (!g) continue;
                    if (inv.Count(g) == 0) inv.AddItem(g, 1); if (am && inv.Count(am) < 3) inv.AddItem(am, 3);
                    eq.Equip(g); yield return new WaitForSeconds(0.5f);
                    input.SimSecondaryHeld = true; yield return new WaitForSeconds(0.8f); wa.Fire(); yield return new WaitForSeconds(gun == "HuntingRifle" ? 1.6f : 0.4f);
                    input.SimSecondaryHeld = null; yield return new WaitForSeconds(0.3f);
                }
                var bow = Item("WoodenBow"); var arrow = Item("WoodenArrow");
                if (bow && arrow)
                {
                    if (inv.Count(bow) == 0) inv.AddItem(bow, 1); if (inv.Count(arrow) < 3) inv.AddItem(arrow, 3);
                    eq.Equip(bow); yield return new WaitForSeconds(0.5f);
                    input.SimSecondaryHeld = true; yield return new WaitForSeconds(1.5f); input.SimPrimary = true; yield return null; yield return null;
                    float t = 0f; while (t < 5f && !ASHSfx.LastByKey.Keys.Any(k => k.StartsWith("arrowImpact"))) { t += Time.deltaTime; yield return null; }
                    input.SimSecondaryHeld = null; yield return new WaitForSeconds(0.3f);
                }
                var pistol = Item("Pistol"); var rounds = Item("PistolRounds");
                if (pistol) { if (inv.Count(pistol) == 0) inv.AddItem(pistol, 1); eq.Equip(pistol); yield return new WaitForSeconds(0.5f); }
                int keep = rounds ? inv.Count(rounds) : 0; if (rounds && keep > 0) inv.Remove(rounds, keep);
                input.SimSecondaryHeld = true; yield return new WaitForSeconds(0.6f); wa.Fire(); yield return new WaitForSeconds(0.3f); input.SimSecondaryHeld = null;
                if (rounds && keep > 0) inv.AddItem(rounds, keep);
                eq.Unequip(ASHEquipSlot.MainHand); yield return new WaitForSeconds(0.3f);
                foreach (var lt in new[] { "Torch", "Flashlight", "Lantern" })
                {
                    var it = Item(lt); if (!it) continue; if (inv.Count(it) == 0) inv.AddItem(it, 1);
                    var fuel = it.fuelItem; if (fuel && inv.Count(fuel) < 2) inv.AddItem(fuel, 2);
                    eq.Equip(it); yield return new WaitForSeconds(0.5f); eq.SetLight(it, false); yield return new WaitForSeconds(0.4f); eq.SetLight(it, true); yield return new WaitForSeconds(0.4f);
                    eq.Unequip(ASHEquipSlot.OffHand); eq.Unequip(ASHEquipSlot.Hip); yield return new WaitForSeconds(0.2f);
                }
            }
            var played = ASHSfx.LastByKey;
            foreach (var k in new[] { "bowDraw", "bowRelease", "arrowFlight", "rifleShot", "rifleBolt", "pistolShot", "dryFire", "torchOn", "torchOff", "flashlightOn", "flashlightOff", "lanternOn", "lanternOff" })
                Check($"played {k}", played.ContainsKey(k), played.TryGetValue(k, out var c) ? c : "not played this session");
            var imp = played.Keys.Where(k => k.StartsWith("arrowImpact")).ToArray();
            Check("arrow impact played", imp.Length > 0, string.Join(", ", imp.Select(k => $"{k}={played[k]}")));
            Finish();
        }

        void Finish() { ASHInput.IgnoreDevices = false; Debug.Log($"[ASH] SOUND SUMMARY pass={_pass} fail={_fail}\n{_sum}"); Destroy(gameObject); }
    }
}

using System.Collections.Generic;
using System.Linq;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Prologue;
using ASH.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t443-t445 (idempotent): Grandpa's Journal pages as Tip / Clue / Rare recipe, each in the area it belongs to and pointing
    /// to the next area. Uses the existing torn-page pickup (ASHManualPagePickup, now with a collection + page id) and the journal's page
    /// reader. Placed now: cabin (p1), truck (p2), arrival road (p3). Shack / camp / cache / Red Rock road: placeholder spawn points only.
    /// Rare recipes: 2 pages (JournalPage unlock overrides in ASH_PrologueSettings; the Survival Guide is untouched). Also gives items with
    /// no icon a letter placeholder (Assets/_ASH/Art/Icons Placeholder). Leaves the scene dirty; it does not save it.</summary>
    public static class ASHPrologueSetupPages
    {
        const string JournalPath = "Assets/_ASH/Resources/ASH_GrandpasJournal.asset";
        const string SettingsPath = "Assets/_ASH/Resources/ASH_PrologueSettings.asset";
        const string PlaceholderDir = "Assets/_ASH/Art/Icons Placeholder";
        const int Version = 2;
        static T Load<T>(string p) where T : Object => AssetDatabase.LoadAssetAtPath<T>(p);
        static ASHRecipe Recipe(string n) => Load<ASHRecipe>($"Assets/_ASH/Data/Recipes/ASH_Recipe_{n}.asset");

        [MenuItem("ASH/Systems/Prologue/Set Up Grandpa's Pages (idempotent)", false, 301)]
        public static void SetUpPages()
        {
            var log = new List<string>();
            var j = Load<ASHJournalCollection>(JournalPath); if (!j) { Debug.LogWarning("[ASH] Run Set Up M1 Opening first (no Grandpa's Journal asset)."); return; }
            var shack = GameObject.Find("shack_spur"); var camp = GameObject.Find("ASH_Objective_OldCampsite");
            Vector3 shackAt = shack ? (shack.transform.childCount > 0 ? shack.transform.GetChild(shack.transform.childCount - 1).position : shack.transform.position) : Vector3.zero;
            Vector3 campAt = camp ? camp.transform.position : Vector3.zero;
            if (j.contentVersion < Version) { FillPages(j, shackAt, campAt); j.contentVersion = Version; EditorUtility.SetDirty(j); log.Add("Grandpa's Journal: 8 pages written (types, areas, next areas, markers, rare recipes)"); }
            else log.Add("Grandpa's Journal content kept (already v" + j.contentVersion + ")");
            RareOverrides(j, log);
            AssetDatabase.SaveAssets();
            Scene(j, shackAt, campAt, log);
            LetterIcons(log);
            Debug.Log("[ASH] Grandpa's pages setup:\n - " + string.Join("\n - ", log));
        }

        static void FillPages(ASHJournalCollection j, Vector3 shackAt, Vector3 campAt)
        {
            var old = j.pages.ToDictionary(p => p.id, p => p);
            ASHJournalCollectionPage P(int n, string title, ASHJournalPageType t, string area, string loc, string next, bool placed, string text)
            {
                var id = $"gj_{n:00}"; old.TryGetValue(id, out var o);
                var p = new ASHJournalCollectionPage { id = id, number = n, title = title, type = t, area = area, location = loc, pointsTo = next, placed = placed, text = text, sketch = o != null ? o.sketch : null };
                return p;
            }
            j.pages = new List<ASHJournalCollectionPage>
            {
                P(1, "Water you can trust", ASHJournalPageType.Tip, "Grandpa's cabin", "Kitchen table", "Grandpa's truck (out by the shed)", true,
                  "Creek water looks clean and isn't. Boil it - a rolling boil, a full minute - or it'll lay you flat for a week. Keep two bottles full, always. If the sky ever turns grey, wet a rag and tie it over your mouth before you do anything else.\n\nIf the car gives up on you, the old Ford is out by the shed."),
                P(2, "The old Ford", ASHJournalPageType.Clue, "Grandpa's truck", "On the ground by the truck", "Shed and old campsite (tires), Old Quarry (battery), Ranger Station (fan belt), washout cache (fuel)", true,
                  "The Ford still has some life in her, if you treat her right. Tires are shot - I've got one spare by the shed and I left the other at the old camp. Took the battery down to the old quarry to run the pump. The fan belt's at the ranger station - Hal was matching me a new one. Twenty litres in the can I left at the washout will get you to Red Rock."),
                P(3, "The shack spur", ASHJournalPageType.Clue, "Arrival road", "Roadside, near where you drive in", "Hunting shack (shack spur trail)", true,
                  "Past the cattle guard the shack spur turns off this road - follow the cairns uphill. The shack roof leaks but the stove draws. If the valley road floods, that trail and the ridge get you around it."),
                P(4, "The hunting shack", ASHJournalPageType.RareRecipe, "Hunting shack", "Under the loose floorboard (M3)", "Abandoned camp", false,
                  "Loose board by the stove - that's my cache. Arrows: straight shoots, dried a week, fletch with three feathers and a spit-wet thread. Make a dozen; you'll lose half. The old camp is down the ridge, past the burnt pines."),
                P(5, "Shelter that seals", ASHJournalPageType.Clue, "Abandoned camp", "Camp entrance (M3)", "Supply cache (ore box past the ridge)", false,
                  "A tent stops rain, not ash. Pitch it behind a wall, wet sheet over the door, fire outside the flap. Spare tire's under the tarp here. I left supplies in the old ore box past the ridge - follow the rail ties."),
                P(6, "Breathing in the grey", ASHJournalPageType.RareRecipe, "Supply cache", "Inside the ore box (M3, position TBD)", "Red Rock road", false,
                  "Those old masks take a can filter. Pack one yourself: tin shell, charcoal from a cold fire, cloth both ends, wax the seam. Change it when breathing gets hard. From here the Red Rock road is a day's walk east."),
                P(7, "Before I go", ASHJournalPageType.Clue, "Red Rock road", "Turnoff sign (M4, position TBD)", "Red Rock", false,
                  "The bridge at the turnoff holds a truck if you go slow. Red Rock has a radio and a doctor. Don't stop in the cut when it's falling heavy."),
                P(8, "Scooter", ASHJournalPageType.Tip, "Grandpa's truck", "Sun visor, once the truck runs (M4)", "Red Rock", false,
                  "Scooter rides up front and growls before you see trouble. Trust that dog. Feed him first, he earns it."),
            };
            var gj2 = j.pages[1]; gj2.chainFlag = "truckFound";
            var gj3 = j.pages[2]; gj3.compassMarker = shackAt != Vector3.zero; gj3.markerPosition = shackAt; gj3.chainFlag = "page:gj_03";
            var gj5 = j.pages[4]; gj5.markerPosition = campAt + new Vector3(30f, 0f, 30f); gj5.chainFlag = "page:gj_05";
            j.pages[3].rareRecipeName = "ASH_Recipe_WoodenArrows";
            j.pages[5].rareRecipeName = "ASH_Recipe_FilterCartridge";
        }

        static void RareOverrides(ASHJournalCollection j, List<string> log)
        {
            var s = Load<ASHPrologueSettings>(SettingsPath); var fm = ASHFieldManualData.I; if (!s) return;
            foreach (var p in j.pages.Where(p => p.type == ASHJournalPageType.RareRecipe && !string.IsNullOrEmpty(p.rareRecipeName)))
            {
                var r = Recipe(p.rareRecipeName.Replace("ASH_Recipe_", "")); if (!r) { log.Add("WARNING missing recipe " + p.rareRecipeName); continue; }
                var o = s.unlockOverrides.FirstOrDefault(x => x != null && x.recipe == r);
                if (o == null)
                {
                    o = new ASHRecipeUnlockOverride { recipe = r, requireAll = false };
                    var e = fm ? fm.Find(r) : null; // keep its Survival Guide ways to unlock, the page is an extra (rare) one
                    if (e != null) foreach (var c in e.conditions) o.conditions.Add(JsonUtility.FromJson<ASHUnlockCondition>(JsonUtility.ToJson(c)));
                    s.unlockOverrides.Add(o);
                }
                if (!o.conditions.Any(c => c.kind == ASHUnlockKind.JournalPage && c.pageId == p.id))
                {
                    o.conditions.Add(new ASHUnlockCondition { kind = ASHUnlockKind.JournalPage, collection = j.collectionId, pageId = p.id });
                    log.Add($"rare recipe {r.name}: unlocks with {j.collectionId}/{p.id} (or its Survival Guide conditions, any one)");
                }
                EditorUtility.SetDirty(s);
            }
        }

        static void Scene(ASHJournalCollection j, Vector3 shackAt, Vector3 campAt, List<string> log)
        {
            var root = GameObject.Find("ASH_Prologue"); if (!root) { log.Add("WARNING no ASH_Prologue root in the scene"); return; }
            var pagesRoot = root.transform.Find("ASH_JournalPages"); if (!pagesRoot) { log.Add("WARNING no ASH_JournalPages"); return; }
            // existing page objects: same pickup as the Survival Guide torn pages (collection + page id)
            var template = pagesRoot.Find("ASH_JournalPage_01");
            foreach (Transform t in pagesRoot) SwapPickup(t, log);

            var truck = Object.FindObjectsByType<ASHVehicle>(FindObjectsInactive.Include).FirstOrDefault(v => v.vehicleId == "pickup");
            var arrival = root.transform.Find("ASH_ArrivalSpawn");
            var p2 = pagesRoot.Find("ASH_JournalPage_02");
            if (p2 && truck && Vector3.Distance(p2.position, truck.transform.position) > 8f)
            {
                if (Spot(truck.transform, new[] { -2.6f * Vector3.right, 2.6f * Vector3.right, -3.4f * Vector3.forward, 3.4f * Vector3.forward, new Vector3(-2.6f, 0, -2.5f) }, out var at, truck.transform))
                { Undo.RecordObject(p2, "Grandpa's pages"); p2.position = at + Vector3.up * 0.004f; log.Add($"page 2 (The old Ford) moved from the cabin to the truck at {at:F1}"); }
                else log.Add("WARNING no clear ground by the truck for page 2 (left in the cabin)");
            }
            if (!pagesRoot.Find("ASH_JournalPage_03") && template && arrival)
            {
                if (Spot(arrival, new[] { 4f * Vector3.right, -4f * Vector3.right, 6f * Vector3.right + 3f * Vector3.forward }, out var at, null))
                {
                    var go = Object.Instantiate(template.gameObject, pagesRoot); go.name = "ASH_JournalPage_03"; Undo.RegisterCreatedObjectUndo(go, "Grandpa's pages");
                    go.SetActive(true); go.transform.SetPositionAndRotation(at + Vector3.up * 0.004f, Quaternion.Euler(0, 70f, 0));
                    var pk = go.GetComponent<ASHManualPagePickup>(); if (pk) { pk.collectionId = j.collectionId; pk.pageId = "gj_03"; }
                    log.Add($"page 3 (The shack spur) on the arrival road at {at:F1}");
                }
                else log.Add("WARNING no clear roadside spot for page 3");
            }
            // placeholder spawn points for areas not built yet (data only)
            var ph = root.transform.Find("ASH_JournalPagePlaceholders (M3-M4, data only)");
            if (!ph) { ph = new GameObject("ASH_JournalPagePlaceholders (M3-M4, data only)").transform; ph.SetParent(root.transform, false); Undo.RegisterCreatedObjectUndo(ph.gameObject, "Grandpa's pages"); }
            Vector3 road = arrival ? arrival.position + arrival.forward * -60f : Vector3.zero;
            void Slot(string id, Vector3 at, string note)
            {
                var pg = j.Page(id); string nm = "Page_" + id; if (ph.Find(nm)) return;
                var go = new GameObject(nm); go.transform.SetParent(ph, false); go.transform.position = at; Undo.RegisterCreatedObjectUndo(go, "Grandpa's pages");
                var s = go.AddComponent<ASHPrologueItemSlot>(); s.itemId = "page:" + id; s.area = pg != null ? pg.area : ""; s.active = false; s.note = note;
                log.Add($"placeholder {nm} ({(pg != null ? pg.area : "?")}) at {at:F1}: {note}");
            }
            Slot("gj_04", shackAt + new Vector3(-2f, 0f, 2f), "Hunting shack floorboard cache (M3). Position = the shack spur end until the shack exists.");
            Slot("gj_05", campAt + new Vector3(3f, 0f, -2f), "Abandoned camp entrance (M3).");
            Slot("gj_06", campAt + new Vector3(30f, 0f, 30f), "Supply cache ore box past the ridge (M3, position TBD).");
            Slot("gj_07", road, "Red Rock road turnoff (M4, position TBD: 60 m down the arrival road for now).");
            Slot("gj_08", truck ? truck.transform.position + Vector3.up * 1.4f : Vector3.zero, "Truck sun visor; spawns once the truck runs (M4).");
            EditorSceneManager.MarkSceneDirty(root.scene);
        }

        static void SwapPickup(Transform t, List<string> log)
        {
            var old = t.GetComponents<MonoBehaviour>().FirstOrDefault(m => m && m.GetType().Name == "ASHJournalPagePickup");
            var pk = t.GetComponent<ASHManualPagePickup>();
            if (old)
            {
                var so = new SerializedObject(old); string col = so.FindProperty("collectionId")?.stringValue, id = so.FindProperty("pageId")?.stringValue;
                Undo.DestroyObjectImmediate(old);
                if (!pk) pk = Undo.AddComponent<ASHManualPagePickup>(t.gameObject);
                pk.collectionId = string.IsNullOrEmpty(col) ? "grandpa" : col; pk.pageId = id;
                log.Add($"{t.name}: now the standard torn-page pickup (collection {pk.collectionId}, page {pk.pageId})");
            }
        }

        static bool Spot(Transform around, Vector3[] localOffsets, out Vector3 at, Transform ignore)
        {
            foreach (var o in localOffsets)
            {
                var p = around.TransformPoint(o); p.y = around.position.y;
                var hits = Physics.RaycastAll(p + Vector3.up * 30f, Vector3.down, 80f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                var hit = hits.FirstOrDefault(h => !(ignore && h.collider.transform.IsChildOf(ignore)));
                if (!hit.collider || !(hit.collider is TerrainCollider)) continue; // only bare ground, never on his objects
                bool clear = !Physics.OverlapBox(hit.point + Vector3.up * 0.35f, new Vector3(0.35f, 0.3f, 0.35f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Any(c => !(c is TerrainCollider));
                if (!clear) continue;
                at = hit.point; return true;
            }
            at = Vector3.zero; return false;
        }

        static void LetterIcons(List<string> log)
        {
            var reg = ASHItemRegistry.I; if (!reg) return;
            foreach (var it in reg.items.Where(i => i && !i.icon))
            {
                string letters = Letters(it.displayName);
                string path = $"{PlaceholderDir}/ASH_Placeholder_{it.itemId}.png";
                var src = $"{PlaceholderDir}/ASH_Letter_{letters}.png";
                if (!System.IO.File.Exists(src)) { log.Add($"WARNING no letter tile {src} for {it.itemId}"); continue; }
                if (!System.IO.File.Exists(path)) { AssetDatabase.CopyAsset(src, path); }
                var imp = AssetImporter.GetAtPath(path) as TextureImporter;
                if (imp && imp.textureType != TextureImporterType.Sprite) { imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single; imp.alphaIsTransparency = true; imp.SaveAndReimport(); }
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(path); if (!sp) continue;
                Undo.RecordObject(it, "letter icon"); it.icon = sp; EditorUtility.SetDirty(it);
                log.Add($"letter icon '{letters}' -> {it.itemId} ({path})");
            }
            AssetDatabase.SaveAssets();
        }
        static string Letters(string name)
        {
            var w = System.Array.FindAll((name ?? "?").Split(new[] { ' ', '-', '_' }, System.StringSplitOptions.RemoveEmptyEntries), s => !s.EndsWith("'s")); // "Grandpa's Packed Tent" -> PT
            return w.Length >= 2 ? (w[0].Substring(0, 1) + w[1].Substring(0, 1)).ToUpperInvariant() : w.Length == 1 ? w[0].Substring(0, 1).ToUpperInvariant() : "?";
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using ASH.Inventory;
using ASH.Prologue;
using ASH.Survival;
using ASH.Vehicles;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// M1 prologue setup (idempotent): settings + Grandpa's Journal assets, the Packed Tent placeholder item, and an ASH_Prologue root
    /// in the open scene (director, arrival spawn, parking zone, falling tree, cinematic camera, Grandpa's supplies, journal pages 1-2,
    /// camp-tire / shack-belt placeholders). Existing pieces are kept (re-running never moves what you placed). Marks the cabin
    /// shelter structural and adds ASH_MainMenu to the build scene list. Leaves the scene dirty; it does not save it.
    /// </summary>
    public static class ASHPrologueSetup
    {
        const string ResDir = "Assets/_ASH/Resources";
        const string SettingsPath = ResDir + "/ASH_PrologueSettings.asset";
        const string JournalPath = ResDir + "/ASH_GrandpasJournal.asset";
        const string TentPath = "Assets/_ASH/Data/Items/ASH_Item_PackedTent.asset";
        const string MenuScene = "Assets/_ASH/Scenes/ASH_MainMenu.unity";

        static T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
        static ASHItemDefinition Item(string name) => Load<ASHItemDefinition>($"Assets/_ASH/Data/Items/ASH_Item_{name}.asset");
        static ASHRecipe Recipe(string name) => Load<ASHRecipe>($"Assets/_ASH/Data/Recipes/ASH_Recipe_{name}.asset");

        [MenuItem("ASH/Systems/Prologue/Set Up M1 Opening (idempotent)", false, 300)]
        public static void SetUp()
        {
            var log = new List<string>();
            var tent = EnsureTent(log);
            var s = EnsureSettings(tent, log);
            EnsureJournal(log);
            AssetDatabase.SaveAssets();
            EnsureScene(log);
            EnsureBuildScenes(log);
            Debug.Log("[ASH] Prologue setup:\n - " + string.Join("\n - ", log));
        }

        // ------------------------------------------------------------------ assets
        static ASHItemDefinition EnsureTent(List<string> log)
        {
            var t = Load<ASHItemDefinition>(TentPath);
            if (!t)
            {
                t = ScriptableObject.CreateInstance<ASHItemDefinition>();
                t.itemId = "packedtent"; t.displayName = "Grandpa's Packed Tent";
                t.description = "Grandpa's old canvas tent, rolled tight with its poles. (Pitching it comes later.)";
                t.category = ASHItemCategory.Salvage; t.maxStack = 1; t.sellValue = 15; t.keyItem = false;
                AssetDatabase.CreateAsset(t, TentPath); log.Add("created item " + TentPath + " (placeholder, no icon, not placeable until M2)");
            }
            var reg = Resources.Load<ASHItemRegistry>("ASH_ItemRegistry");
            if (reg && !reg.items.Contains(t)) { reg.items.Add(t); EditorUtility.SetDirty(reg); log.Add("registered packedtent in ASH_ItemRegistry"); }
            return t;
        }

        static ASHPrologueSettings EnsureSettings(ASHItemDefinition tent, List<string> log)
        {
            var s = Load<ASHPrologueSettings>(SettingsPath);
            if (!s) { s = ScriptableObject.CreateInstance<ASHPrologueSettings>(); AssetDatabase.CreateAsset(s, SettingsPath); log.Add("created " + SettingsPath); }
            bool dirty = false;
            if (s.supplies == null || s.supplies.Count == 0)
            {
                s.supplies = new List<ASHRecipeIngredient>();
                void Add(ASHItemDefinition it, int n) { if (it) s.supplies.Add(new ASHRecipeIngredient { item = it, amount = n }); else log.Add("WARNING missing supply item"); }
                Add(Item("CannedBeans"), 1); Add(Item("CannedPeaches"), 1); Add(Item("BottledWater"), 2); Add(Item("Bandage"), 2);
                Add(Item("HuntingRifle"), 1); Add(Item("RifleRounds"), 5); Add(Item("Hatchet"), 1); Add(Item("Torch"), 1); Add(tent, 1);
                dirty = true; log.Add($"supplies: {string.Join(", ", s.supplies.Select(x => x.item.itemId + " x" + x.amount))}");
            }
            if (s.basicRecipes == null || s.basicRecipes.Count == 0)
            {
                s.basicRecipes = new List<ASHRecipe> { Recipe("Campfire"), Recipe("SplitKindling"), Recipe("Bandage"), Recipe("BoilWater"), Recipe("WetRag") };
                s.basicRecipes.RemoveAll(r => !r); dirty = true; log.Add($"basics ({s.basicRecipes.Count}): {string.Join(", ", s.basicRecipes.Select(r => r.name))}");
            }
            if (s.unlockOverrides == null || s.unlockOverrides.Count == 0)
            {
                s.unlockOverrides = new List<ASHRecipeUnlockOverride>();
                void O(string recipe, params ASHUnlockCondition[] cs) { var r = Recipe(recipe); if (!r) { log.Add("WARNING missing recipe " + recipe); return; } s.unlockOverrides.Add(new ASHRecipeUnlockOverride { recipe = r, requireAll = false, conditions = cs.ToList() }); }
                ASHUnlockCondition Pick(string item) => new ASHUnlockCondition { kind = ASHUnlockKind.FirstPickup, item = Item(item) };
                O("Torch", Pick("Torch"));
                O("Rope", Pick("PlantFiber"));
                O("ClothFromShirt", Pick("OldShirt"));
                O("FlattenTins", Pick("EmptyTin"));
                O("CampfireFirewood", Pick("Firewood"));
                dirty = true; log.Add("unlock overrides: Torch (pick up a torch), Rope (plant fiber), Cloth from Shirt (old shirt), Flatten Tins (empty tin), Campfire (firewood) (firewood)");
            }
            else
            {
                int removed = 0;
                foreach (var o in s.unlockOverrides)
                {
                    if (o == null || o.conditions == null) continue;
                    removed += o.conditions.RemoveAll(c => c != null && c.kind == ASHUnlockKind.JournalPage && c.pageId == "gj_01");
                }
                if (removed > 0) { dirty = true; log.Add("unlock overrides: Torch no longer unlocks from Grandpa's Journal p.1 (pickup only)"); }
            }
            if (dirty) EditorUtility.SetDirty(s);
            return s;
        }

        static void EnsureJournal(List<string> log)
        {
            var j = Load<ASHJournalCollection>(JournalPath);
            if (!j) { j = ScriptableObject.CreateInstance<ASHJournalCollection>(); AssetDatabase.CreateAsset(j, JournalPath); log.Add("created " + JournalPath); }
            if (j.pages.Count > 0) return;
            void P(int n, string title, string where, bool placed, string text) => j.pages.Add(new ASHJournalCollectionPage { id = $"gj_{n:00}", number = n, title = title, location = where, placed = placed, text = text });
            P(1, "Water you can trust", "Cabin kitchen (P01)", true,
              "Creek water looks clean and isn't. Boil it - a rolling boil, a full minute - or it'll lay you flat for a week. Keep two bottles full, always. If the sky ever turns grey, wet a rag and tie it over your mouth before you do anything else.");
            P(2, "The old Ford", "Cabin workbench (P01, starts the truck quest)", true,
              "The Ford still has some life in her, if you treat her right. Tires are shot - I've got one spare by the shed and I left the other at the old camp. Battery's on the porch. The fan belt I took up to the hunting shack to match a new one. Twenty litres in the can will get you to Red Rock.");
            P(3, "Duck gear on the western trail", "Cabin bedroom (after P03, starts P07)", false, "[M3 draft] Points to the creek and the fallen pine on the western trail.");
            P(4, "The hunting shack", "Hunting shack cache (P08)", false, "[M3 draft] The shack, the cache under the floor, the snare.");
            P(5, "Shelter that seals", "Abandoned camp entrance (P09, before the Built Tent objective)", false, "[M3 draft] How to build a tent shelter that keeps the ash out.");
            P(6, "Breathing in the grey", "Optional remote cache (unlocks filters)", false, "[M3 draft] Filters and masks.");
            P(7, "Before I go", "Truck glovebox once repaired (P10)", false, "[M4 draft]");
            P(8, "Scooter", "Truck sun visor (final note, starts P11)", false, "[M4 draft]");
            EditorUtility.SetDirty(j); log.Add("Grandpa's Journal: 8 pages (1-2 written and placed; 3-8 drafts for M3/M4). The Survival Guide (ASH_FieldManual) is untouched.");
        }

        // ------------------------------------------------------------------ scene
        static Transform Child(Transform root, string name, out bool created)
        {
            var t = root.Find(name); created = !t;
            if (!t) { t = new GameObject(name).transform; t.SetParent(root, false); Undo.RegisterCreatedObjectUndo(t.gameObject, "Prologue setup"); }
            return t;
        }

        static bool Ground(Vector3 at, out Vector3 hit, params Collider[] ignore)
        {
            var hits = Physics.RaycastAll(at + Vector3.up * 150f, Vector3.down, 400f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
            foreach (var h in hits) { if (ignore != null && ignore.Contains(h.collider)) continue; hit = h.point; return true; }
            hit = at; return false;
        }
        static bool Clear(Vector3 center, Vector3 half, Quaternion rot, params Transform[] ignoreRoots)
        {
            foreach (var c in Physics.OverlapBox(center, half, rot, ~0, QueryTriggerInteraction.Ignore))
            {
                if (c is TerrainCollider) continue;
                if (ignoreRoots != null && ignoreRoots.Any(r => r && c.transform.IsChildOf(r))) continue;
                return false;
            }
            return true;
        }
        static Bounds RendBounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        static void EnsureScene(List<string> log)
        {
            Physics.SyncTransforms();
            var scene = EditorSceneManager.GetActiveScene();
            var rootGo = GameObject.Find("ASH_Prologue");
            if (!rootGo) { rootGo = new GameObject("ASH_Prologue"); Undo.RegisterCreatedObjectUndo(rootGo, "Prologue setup"); log.Add("created ASH_Prologue root"); }
            var root = rootGo.transform;
            var dir = rootGo.GetComponent<ASHPrologueDirector>(); if (!dir) { dir = Undo.AddComponent<ASHPrologueDirector>(rootGo); log.Add("added ASHPrologueDirector"); }
            Undo.RecordObject(dir, "Prologue setup");

            var vehicles = Object.FindObjectsByType<ASHVehicle>(FindObjectsInactive.Include);
            var car = vehicles.FirstOrDefault(v => v.vehicleId == "car");
            var truck = vehicles.FirstOrDefault(v => v.vehicleId == "pickup");
            var cabin = Object.FindObjectsByType<ASHShelterZone>(FindObjectsInactive.Include).FirstOrDefault(z => z.name == "ASH_CabinInterior") ??
                        Object.FindObjectsByType<ASHShelterZone>(FindObjectsInactive.Include).FirstOrDefault();
            if (!car) log.Add("WARNING no ASHVehicle with id 'car' in the scene");
            if (cabin)
            {
                if (!cabin.structural) { Undo.RecordObject(cabin, "Prologue setup"); cabin.structural = true; EditorUtility.SetDirty(cabin); log.Add($"{cabin.name}: structural = true (only change on your cabin object)"); }
                if (!dir.cabinShelter) dir.cabinShelter = cabin;
            }
            var carT = car ? car.transform : null;
            var cabinPos = cabin ? cabin.transform.position : (carT ? carT.position : Vector3.zero);

            // parking zone = the car's current (hand-placed) spot
            var zone = Child(root, "ASH_CabinParkingZone", out bool zNew);
            if (zNew && carT) { zone.SetPositionAndRotation(carT.position, carT.rotation); log.Add($"parking zone at the car's spot {carT.position:F1}"); }
            if (!dir.parkingZone) dir.parkingZone = zone;

            // arrival spawn: behind the car, clear ground, facing the cabin
            var spawn = Child(root, "ASH_ArrivalSpawn", out bool sNew);
            if (sNew && carT)
            {
                var carCols = car.GetComponentsInChildren<Collider>();
                var cb = RendBounds(car.gameObject); var half = new Vector3(1.3f, 1.0f, 2.8f);
                var back = -Vector3.ProjectOnPlane(carT.forward, Vector3.up).normalized;
                bool found = false;
                foreach (float dist in new[] { 40f, 32f, 48f, 26f, 56f })
                {
                    foreach (float ang in new[] { 0f, 15f, -15f, 30f, -30f, 50f, -50f })
                    {
                        var d = Quaternion.Euler(0, ang, 0) * back;
                        if (!Ground(carT.position + d * dist, out var g, carCols)) continue;
                        var face = Quaternion.LookRotation(Vector3.ProjectOnPlane(carT.position - g, Vector3.up).normalized, Vector3.up);
                        if (!Clear(g + Vector3.up * 1.3f, half, face, carT)) continue;
                        spawn.SetPositionAndRotation(g + Vector3.up * 0.35f, face); found = true;
                        log.Add($"arrival spawn {dist:F0} m behind the car ({ang:+0;-0;0} deg) at {spawn.position:F1}, facing the cabin. Move it onto the arrival road when it exists.");
                        break;
                    }
                    if (found) break;
                }
                if (!found) { spawn.SetPositionAndRotation(carT.position + back * 40f + Vector3.up * 2f, carT.rotation); log.Add("WARNING arrival spawn: no clear spot found; placed 40 m behind the car, please move it"); }
            }
            if (!dir.arrivalSpawn) dir.arrivalSpawn = spawn;

            // falling tree (dedicated harvest-tree prefab instance; chopping is disabled on it at runtime)
            var treeT = root.Find("ASH_FallingTree");
            if (!treeT && carT)
            {
                var prefab = Load<GameObject>("Assets/_ASH/Prefabs/Harvest/ASH_Harvest_DeadFir_A.prefab");
                if (prefab)
                {
                    var pivot = new GameObject("ASH_FallingTree"); Undo.RegisterCreatedObjectUndo(pivot, "Prologue setup"); pivot.transform.SetParent(root, false);
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene); inst.transform.SetParent(pivot.transform, false);
                    inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                    var b = RendBounds(inst); float h = Mathf.Max(4f, b.size.y);
                    float D = Mathf.Clamp(h * 0.62f, 4f, 16f);
                    var away = Vector3.ProjectOnPlane(carT.position - cabinPos, Vector3.up).normalized; if (away.sqrMagnitude < 0.01f) away = -carT.forward;
                    var carCols = car.GetComponentsInChildren<Collider>(); var treeCols = inst.GetComponentsInChildren<Collider>();
                    bool ok = false;
                    foreach (float ang in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 120f, -120f })
                    {
                        var d = Quaternion.Euler(0, ang, 0) * away;
                        if (!Ground(carT.position + d * D, out var g, carCols.Concat(treeCols).ToArray())) continue;
                        pivot.transform.position = new Vector3(g.x, g.y - 0.15f, g.z);
                        Physics.SyncTransforms();
                        if (!Clear(g + Vector3.up * 1.6f, new Vector3(0.7f, 1.2f, 0.7f), Quaternion.identity, pivot.transform, carT)) continue;
                        pivot.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(carT.position - g, Vector3.up).normalized, Vector3.up);
                        ok = true; log.Add($"falling tree (DeadFir_A, {h:F1} m) {D:F1} m from the car at {g:F1} ({ang:+0;-0;0} deg from 'away from the cabin')"); break;
                    }
                    if (!ok) { pivot.transform.position = carT.position + away * D; log.Add("WARNING falling tree: no clear spot; placed anyway, please check"); }
                    var ft = pivot.AddComponent<ASHPrologueFallingTree>(); ft.fallTarget = zone; ft.height = h;
                    treeT = pivot.transform;
                }
                else log.Add("WARNING DeadFir_A prefab not found");
            }
            if (treeT && !dir.fallingTree) dir.fallingTree = treeT.GetComponent<ASHPrologueFallingTree>();

            // cinematic camera
            var cam = Child(root, "ASH_TreeCinematicCam", out bool cNew);
            if (cNew && carT)
            {
                var toCabin = Vector3.ProjectOnPlane(cabinPos - carT.position, Vector3.up).normalized;
                var side = Vector3.Cross(Vector3.up, toCabin);
                var p = carT.position + toCabin * 9f + side * 7f; Ground(p, out var g); p = g + Vector3.up * 3.2f;
                cam.SetPositionAndRotation(p, Quaternion.LookRotation(carT.position + Vector3.up - p));
                log.Add($"cinematic camera at {p:F1} (aims at the car and tree when the cut starts)");
            }
            if (!dir.cinematicCamera) dir.cinematicCamera = cam;

            // Grandpa's supplies + journal pages 1-2, inside the cabin
            var journalPickup = GameObject.Find("ASH_Pickup_GrandpasJournal");
            int useLayer = journalPickup ? journalPickup.layer : 0;
            var spots = cabin ? CabinSpots(cabin, 3) : new List<Vector3>();
            var sup = root.Find("ASH_GrandpasSupplies");
            if (!sup)
            {
                var go = new GameObject("ASH_GrandpasSupplies"); Undo.RegisterCreatedObjectUndo(go, "Prologue setup"); go.transform.SetParent(root, false);
                go.transform.position = spots.Count > 0 ? spots[0] : cabinPos;
                var crate = Object.FindObjectsByType<ASH.Interaction.ASHSupplyCrate>().FirstOrDefault();
                if (crate)
                {
                    foreach (var mf in crate.GetComponentsInChildren<MeshFilter>())
                    {
                        var v = new GameObject("Visual_" + mf.name); v.transform.SetParent(go.transform, false);
                        v.transform.localPosition = crate.transform.InverseTransformPoint(mf.transform.position); v.transform.localRotation = Quaternion.Inverse(crate.transform.rotation) * mf.transform.rotation; v.transform.localScale = mf.transform.lossyScale;
                        v.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh; var mr = mf.GetComponent<MeshRenderer>(); if (mr) v.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                        v.layer = useLayer;
                    }
                }
                else { var v = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(v.GetComponent<Collider>()); v.transform.SetParent(go.transform, false); v.transform.localScale = new Vector3(0.8f, 0.45f, 0.5f); v.transform.localPosition = Vector3.up * 0.225f; }
                var bc = go.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(1.0f, 0.8f, 0.8f); bc.center = Vector3.up * 0.35f;
                go.layer = useLayer; go.AddComponent<ASHGrandpaSupplies>();
                sup = go.transform; log.Add($"Grandpa's supplies box in the cabin at {go.transform.position:F1} (hold E; exact kit from the settings)");
            }
            if (!dir.supplies) dir.supplies = sup.GetComponent<ASHGrandpaSupplies>();

            var pagesRoot = Child(root, "ASH_JournalPages", out _);
            for (int n = 1; n <= 2; n++)
            {
                string nm = $"ASH_JournalPage_{n:00}"; if (pagesRoot.Find(nm)) continue;
                var go = new GameObject(nm); Undo.RegisterCreatedObjectUndo(go, "Prologue setup"); go.transform.SetParent(pagesRoot, false);
                var p = spots.Count > n ? spots[n] : cabinPos + Vector3.right * n; go.transform.SetPositionAndRotation(p + Vector3.up * 0.004f, Quaternion.Euler(0, 25f * n, 0));
                var vis = GameObject.CreatePrimitive(PrimitiveType.Cube); vis.name = "Visual"; Object.DestroyImmediate(vis.GetComponent<Collider>());
                vis.transform.SetParent(go.transform, false); vis.transform.localScale = new Vector3(0.2f, 0.004f, 0.27f);
                var mat = Load<Material>("Assets/_ASH/Data/FieldManual/M_Placeholder_manual_page.mat"); if (mat) vis.GetComponent<Renderer>().sharedMaterial = mat;
                var col = go.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(0.4f, 0.25f, 0.45f); col.center = Vector3.up * 0.1f;
                go.layer = useLayer; vis.layer = useLayer;
                var pk = go.AddComponent<ASH.Interaction.ASHManualPagePickup>(); pk.collectionId = "grandpa"; pk.pageId = $"gj_{n:00}";
                log.Add($"Grandpa's Journal page {n} at {p:F1}");
            }

            // data-only placeholders for parts whose areas don't exist yet (M3)
            var ph = Child(root, "ASH_PartPlaceholders (M3, data only)", out _);
            void Slot(string nm, string item, string area, Vector3 at, string note)
            {
                if (ph.Find(nm)) return;
                var go = new GameObject(nm); Undo.RegisterCreatedObjectUndo(go, "Prologue setup"); go.transform.SetParent(ph, false); go.transform.position = at;
                var s = go.AddComponent<ASHPrologueItemSlot>(); s.itemId = item; s.area = area; s.active = false; s.note = note;
                log.Add($"placeholder {nm} ({item}) at {at:F1}: data only, nothing spawns");
            }
            var camp = GameObject.Find("ASH_Objective_OldCampsite"); var shack = GameObject.Find("shack_spur");
            Vector3 shackAt = shack ? (shack.transform.childCount > 0 ? shack.transform.GetChild(shack.transform.childCount - 1).position : shack.transform.position) : Vector3.zero;
            Slot("CampTire", "tire", "Abandoned camp", camp ? camp.transform.position : Vector3.zero, "Second truck tire goes here once the abandoned camp is built (M3). The hand-placed tires near the cabin are unchanged.");
            Slot("ShackFanBelt", "fanbelt", "Hunting shack", shackAt, "The fan belt moves here once the hunting shack exists (M3). For now it stays where it is.");

            EditorUtility.SetDirty(dir);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>Clear floor spots inside the cabin shelter volume (centre first, then a grid).</summary>
        static List<Vector3> CabinSpots(ASHShelterZone cabin, int count)
        {
            var list = new List<Vector3>(); var c = cabin.volume ? cabin.volume : cabin.GetComponent<Collider>(); if (!c) return list;
            var b = c.bounds; var top = b.max.y - 0.1f;
            var offs = new List<Vector2>();
            for (float x = -0.35f; x <= 0.351f; x += 0.175f) for (float z = -0.35f; z <= 0.351f; z += 0.175f) offs.Add(new Vector2(x, z));
            offs = offs.OrderByDescending(o => Mathf.Abs(o.x) + Mathf.Abs(o.y)).ToList(); // near the walls first (out of the walkway)
            foreach (var o in offs)
            {
                var from = new Vector3(b.center.x + o.x * b.size.x, top, b.center.z + o.y * b.size.z);
                if (!Physics.Raycast(from, Vector3.down, out var h, b.size.y + 1f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (h.point.y > b.min.y + 1.2f) continue; // furniture top, not the floor
                if (!Clear(h.point + Vector3.up * 0.3f, new Vector3(0.45f, 0.25f, 0.4f), Quaternion.identity)) continue;
                if (list.Any(p => (p - h.point).sqrMagnitude < 1.0f)) continue;
                list.Add(h.point); if (list.Count >= count) break;
            }
            return list;
        }

        static void EnsureBuildScenes(List<string> log)
        {
            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.Any(x => x.path == MenuScene)) return;
            if (!System.IO.File.Exists(MenuScene)) { log.Add("WARNING " + MenuScene + " not found"); return; }
            scenes.Insert(0, new EditorBuildSettingsScene(MenuScene, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            log.Add("Build Settings: added ASH_MainMenu as scene 0 (ProjectSettings/EditorBuildSettings.asset changed)");
        }
    }
}

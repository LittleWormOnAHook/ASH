using System.Collections.Generic;
using System.Linq;
using ASH.Inventory;
using ASH.Interaction;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools.Systems
{
    /// <summary>
    /// Grandpa's Field Manual setup (idempotent): items (journal, rags, water bottle, gas mask, filter cartridge), the manual's recipes,
    /// the unlock tree for every existing recipe (Resources/ASH_FieldManual - existing entries keep your edits; only missing ones are
    /// added), and placement of the journal / bottle / torn pages. Placement edits the open scene but never saves it.
    /// </summary>
    public static class ASHFieldManualInstaller
    {
        const string ItemDir = "Assets/_ASH/Data/Items", RecipeDir = "Assets/_ASH/Data/Recipes", MatDir = "Assets/_ASH/Data/FieldManual";
        const string DataPath = ASHMenuInstaller.ResDir + "/ASH_FieldManual.asset";

        static ASHItemDefinition Item(string name) => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>($"{ItemDir}/ASH_Item_{name}.asset")
            ?? AssetDatabase.FindAssets($"ASH_Item_{name} t:ASHItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(i => i && i.name == $"ASH_Item_{name}");
        static ASHRecipe Recipe(string name) => AssetDatabase.LoadAssetAtPath<ASHRecipe>($"{RecipeDir}/ASH_Recipe_{name}.asset")
            ?? AssetDatabase.FindAssets($"ASH_Recipe_{name} t:ASHRecipe").Select(g => AssetDatabase.LoadAssetAtPath<ASHRecipe>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(r => r && r.name == $"ASH_Recipe_{name}");

        static ASHItemDefinition NewItem(string name, string id, string display, string desc, ASHItemCategory cat, int stack, Color tint, string verb = "Use")
        {
            var it = Item(name); if (it) return it;
            ASHMenuInstaller.Folder(ItemDir);
            it = ScriptableObject.CreateInstance<ASHItemDefinition>();
            it.itemId = id; it.displayName = display; it.description = desc; it.category = cat; it.maxStack = stack; it.uiTint = tint; it.useVerb = verb;
            AssetDatabase.CreateAsset(it, $"{ItemDir}/ASH_Item_{name}.asset");
            return it;
        }

        static ASHRecipe NewRecipe(string name, string id, string display, string desc, ASHRecipeCategory cat, ASHItemDefinition output, float water, float secs, params (ASHItemDefinition item, int n)[] ings)
        {
            var r = Recipe(name); if (r) return r;
            if (!output) { Debug.LogWarning($"[ASH] Field Manual: recipe {name} skipped (output item missing)"); return null; }
            ASHMenuInstaller.Folder(RecipeDir);
            r = ScriptableObject.CreateInstance<ASHRecipe>();
            r.recipeId = id; r.displayName = display; r.description = desc; r.category = cat; r.outputItem = output; r.outputAmount = 1; r.waterPercent = water; r.craftSeconds = secs;
            foreach (var (item, n) in ings) if (item) r.ingredients.Add(new ASHRecipeIngredient { item = item, amount = n });
            AssetDatabase.CreateAsset(r, $"{RecipeDir}/ASH_Recipe_{name}.asset");
            return r;
        }

        static ASHUnlockCondition Pick(string item) => new ASHUnlockCondition { kind = ASHUnlockKind.FirstPickup, item = Item(item) };
        static ASHUnlockCondition Made(string item, int n = 1) => new ASHUnlockCondition { kind = ASHUnlockKind.Crafted, item = Item(item), quantity = n };
        static ASHUnlockCondition Page(int n) => new ASHUnlockCondition { kind = ASHUnlockKind.ManualPage, page = n };

        [MenuItem("ASH/Systems/Field Manual/Install Field Manual (items, recipes, tree)", false, 120)]
        public static void Install()
        {
            var tan = new Color(0.62f, 0.46f, 0.3f);
            var journal = NewItem("GrandpasJournal", "grandpas_journal", "Grandpa's Journal", "Worn leather journal. \"SURVIVAL GUIDE\" scratched into the cover. Grandpa's handwriting inside.", ASHItemCategory.Key, 1, tan, "Read");
            if (!journal.keyItem) { journal.keyItem = true; EditorUtility.SetDirty(journal); } // stays in the pack: move only
            var wet = NewItem("WetRag", "wet_rag", "Wet Rag", "A damp rag to tie over your mouth and nose. Filters the worst of the ash for several hours before it dries out.", ASHItemCategory.Medical, 1, new Color(0.55f, 0.7f, 0.78f), "Wear");
            var dry = NewItem("DryRag", "dry_rag", "Dry Rag", "Dried out and caked with ash. Wet it again with a little water.", ASHItemCategory.Resource, 5, new Color(0.6f, 0.58f, 0.52f));
            NewItem("WaterBottle", "water_bottle", "Water Bottle", "Grandpa's dented steel canteen. Refill it at water sources. Each drink uses a tenth of it.", ASHItemCategory.Tool, 1, new Color(0.45f, 0.62f, 0.72f), "Drink");
            var mask = NewItem("GasMask", "gas_mask", "Gas Mask", "Old rubber respirator. Far better than a rag, if you have filter cartridges for it.", ASHItemCategory.Tool, 1, new Color(0.3f, 0.33f, 0.3f), "Wear");
            var filter = NewItem("FilterCartridge", "filter_cartridge", "Filter Cartridge", "Screw-in respirator filter. Condition varies; a clogged one is useless.", ASHItemCategory.Tool, 1, new Color(0.5f, 0.52f, 0.36f), "Fit");
            var cloth = Item("Cloth"); var rags = Item("Rags");

            var rWet = NewRecipe("WetRag", "wet_rag", "Wet Rag", "Soak a strip of cloth with water from your bottle. Tie it over mouth and nose.", ASHRecipeCategory.Medical, wet, 10f, 1.5f, (cloth, 1));
            var rWetRags = NewRecipe("WetRagRags", "wet_rag_rags", "Wet Rag (from rags)", "Old rags work too. Soak with water from your bottle.", ASHRecipeCategory.Medical, wet, 10f, 1.5f, (rags, 1));
            var rReWet = NewRecipe("ReWetRag", "rewet_rag", "Re-wet Rag", "Dry rags can be soaked again. Shake the ash out first.", ASHRecipeCategory.Medical, wet, 10f, 1.0f, (dry, 1));
            var rFilter = NewRecipe("FilterCartridge", "filter_cartridge", "Filter Cartridge", "Charcoal from kindling packed between cloth in a tin shell. Not factory grade, but it breathes.", ASHRecipeCategory.Tools, filter, 0f, 6f, (cloth, 2), (Item("ScrapMetal"), 1), (Item("Kindling"), 1));
            EditorUtility.SetDirty(journal);

            var d = ASHMenuInstaller.Ensure<ASHFieldManualData>(DataPath);
            if (!d.journalItem) d.journalItem = journal;
            if (d.pages.Count == 0) d.pages.AddRange(DefaultPages());

            int added = 0;
            void E(ASHRecipe r, int col, int row, bool basic, string[] parents, bool all, params ASHUnlockCondition[] cs)
            {
                if (!r || d.Find(r) != null) return;
                var e = new ASHFieldManualEntry { recipe = r, column = col, row = row, basic = basic, requireAll = all };
                foreach (var c in cs) if (c.kind == ASHUnlockKind.ManualPage || c.item) e.conditions.Add(c);
                if (parents != null) foreach (var p in parents) { var pr = p == "WetRag" ? rWet : Recipe(p); if (pr) e.parents.Add(pr); }
                d.entries.Add(e); added++;
            }
            string[] P(params string[] s) => s;
            // breathing
            E(rWet, 0, 0, true, null, true);
            E(rReWet, 1, 0, false, P("WetRag"), true, Made("WetRag"));
            E(rWetRags, 1, 1, false, P("WetRag"), true, Pick("Rags"));
            E(rFilter, 2, 0, false, P("ReWetRag"), true, Pick("GasMask"), Page(7));
            // fire
            E(Recipe("CampfireFirewood"), 0, 2, true, null, true);
            E(Recipe("CampfirePlanks"), 1, 2, false, P("CampfireFirewood"), true, Pick("ReclaimedWood"));
            E(Recipe("CampfireAlcohol"), 1, 3, false, P("CampfireFirewood"), true, Pick("RubbingAlcohol"));
            E(Recipe("Campfire"), 0, 3, true, null, true);
            E(Recipe("SplitKindling"), 0, 4, true, null, true);
            E(Recipe("TorchKindling"), 1, 4, false, P("SplitKindling"), true, Made("Kindling", 2));
            E(Recipe("Torch"), 0, 5, true, null, true);
            // cloth, rope, hunting
            E(Recipe("ClothFromShirt"), 0, 6, true, null, true);
            E(Recipe("Rope"), 1, 6, false, P("ClothFromShirt"), true, Pick("PlantFiber"));
            E(Recipe("WoodenBow"), 2, 6, false, P("Rope"), true, Made("Rope", 2), Page(5));
            E(Recipe("WoodenArrows"), 3, 6, false, P("WoodenBow"), true, Made("WoodenBow"));
            // medical
            E(Recipe("Bandage"), 0, 7, true, null, true);
            E(Recipe("AntisepticBandage"), 1, 7, false, P("Bandage"), false, Made("Bandage", 3), Page(2));
            E(Recipe("AntisepticBandageWhiskey"), 2, 7, false, P("AntisepticBandage"), true, Pick("Whiskey"));
            E(Recipe("FiberBandage"), 1, 8, false, P("Bandage"), true, Pick("PlantFiber"));
            // metal
            E(Recipe("FlattenTins"), 0, 9, true, null, true);
            E(Recipe("ImprovisedKnife"), 1, 9, false, P("FlattenTins"), true, Pick("ScrapMetal"));
            // water + food
            E(Recipe("BoilWater"), 0, 10, true, null, true);
            E(Recipe("CookMeat"), 1, 10, false, P("BoilWater"), true, Pick("RawMeat"));
            E(Recipe("CookFish"), 1, 11, false, P("BoilWater"), true, Pick("RawFish"));
            E(Recipe("DryChokecherries"), 1, 12, false, P("BoilWater"), true, Pick("Chokecherries"));
            // any other recipe in the project lands in column 4 as a basic (edit it in the asset)
            int extraRow = 0;
            foreach (var g in AssetDatabase.FindAssets("t:ASHRecipe"))
            {
                var r = AssetDatabase.LoadAssetAtPath<ASHRecipe>(AssetDatabase.GUIDToAssetPath(g));
                if (!r || d.Find(r) != null) continue;
                E(r, 4, extraRow++, true, null, true);
            }
            if (string.IsNullOrEmpty(d.tabName) || d.tabName == "GRANDPA'S FIELD MANUAL") d.tabName = "SURVIVAL GUIDE";
            EditorUtility.SetDirty(d);
            ASHMenuInstaller.RefreshRegistry();
            AssetDatabase.SaveAssets();
            Selection.activeObject = d;
            Debug.Log($"[ASH] Field Manual installed: {DataPath} ({d.entries.Count} recipes, {added} added). Next: ASH/Systems/Field Manual/Place Journal + Bottle in Grandpa's Cabin, and Place Torn Page.");
        }

        static IEnumerable<ASHManualPage> DefaultPages()
        {
            string[] t = {
                "Air first", "Clean wounds", "Fire in the wet", "Keep moving", "A bow from nothing", "Water you can trust",
                "Breathing in the grey", "Reading the sky", "Meat and smoke", "Shelter that seals", "Night walking", "If I don't make it" };
            string[] x = {
                "Ash cuts your lungs like glass. Wet cloth over mouth and nose. Stay inside when the sky goes brown.",
                "Boil it, wrap it, keep it covered. A cheap bandage changed often beats a good one left on.",
                "Split the wood, shave the kindling. Dry stuff under the porch. Never trust the top of a pile.",
                "Don't sit down in the open when it's falling heavy. Pick the next roof, go to it, rest there.",
                "Ash or willow, the length of you. Two good ropes for the string. Notch, bend slow, oil it.",
                "Snow melts dirty. Creek water too. If it isn't boiled it isn't water.",
                "Those old masks take a can filter. Pack one yourself: tin shell, cloth, charcoal from kindling. Change it before it stinks.",
                "Brown haze from the north-west means ash by the afternoon. Thunder in ash means get under cover now.",
                "Cook it through. Dry the berries by the fire, they'll keep for weeks.",
                "Tape the window frames, wet sheet over the door. A lean-to helps, but the ash still gets in.",
                "Lanterns carry far. Keep to the trees, count your steps between markers.",
                "Head down the valley road. Don't wait for me. Love you, kid." };
            for (int i = 0; i < 12; i++) yield return new ASHManualPage { number = i + 1, title = t[i], text = x[i] };
        }

        // ------------------------------------------------------------------ placement (scene edits, never saved here)
        static Material Mat(string id, Color c)
        {
            ASHMenuInstaller.Folder(MatDir);
            var path = $"{MatDir}/M_Placeholder_{id}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            m = new Material(sh); m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            AssetDatabase.CreateAsset(m, path); return m;
        }

        /// <summary>Placeholder pickup (primitive + trigger) named ASH_Pickup_{id}; reuses the item's world prefab when it has one.</summary>
        public static GameObject PlacePickup(ASHItemDefinition item, string objName, Vector3 pos, float yaw, PrimitiveType shape, Vector3 size, bool roll = false)
        {
            var go = GameObject.Find(objName);
            if (!go)
            {
                if (item.worldPickupPrefab) { go = (GameObject)PrefabUtility.InstantiatePrefab(item.worldPickupPrefab); go.name = objName; }
                else
                {
                    go = new GameObject(objName);
                    var vis = GameObject.CreatePrimitive(shape); vis.name = "Visual"; Object.DestroyImmediate(vis.GetComponent<Collider>());
                    vis.transform.SetParent(go.transform, false); vis.transform.localScale = size; vis.transform.localPosition = Vector3.up * size.y * (shape == PrimitiveType.Cylinder ? 1f : 0.5f);
                    vis.GetComponent<Renderer>().sharedMaterial = Mat(item.itemId, item.uiTint);
                    var col = go.AddComponent<BoxCollider>(); col.isTrigger = true;
                    float h = shape == PrimitiveType.Cylinder ? size.y * 2f : size.y;
                    col.center = Vector3.up * Mathf.Max(h, 0.25f) * 0.5f; col.size = new Vector3(Mathf.Max(size.x, 0.25f) + 0.12f, Mathf.Max(h, 0.25f), Mathf.Max(size.z, 0.25f) + 0.12f);
                    int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
                }
                Undo.RegisterCreatedObjectUndo(go, "Place " + item.displayName);
            }
            var pk = go.GetComponent<ASHItemPickup>(); if (!pk) pk = go.AddComponent<ASHItemPickup>();
            pk.Configure(item, 1); if (roll) pk.SetRollCondition(true);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            EditorUtility.SetDirty(go); EditorSceneManager.MarkSceneDirty(go.scene);
            return go;
        }

        static bool SceneViewPoint(out Vector3 p, out float yaw)
        {
            p = default; yaw = 0;
            var sv = SceneView.lastActiveSceneView; if (!sv) { Debug.LogWarning("[ASH] Open a Scene view and look at the spot first."); return false; }
            var cam = sv.camera.transform; yaw = cam.eulerAngles.y;
            if (Physics.Raycast(cam.position, cam.forward, out var hit, 500f, ~0, QueryTriggerInteraction.Ignore)) { p = hit.point; return true; }
            p = cam.position + cam.forward * 4f; return true;
        }

        [MenuItem("ASH/Systems/Field Manual/Place Journal + Bottle in Grandpa's Cabin", false, 121)]
        public static void PlaceInCabin()
        {
            var journal = Item("GrandpasJournal"); var bottle = Item("WaterBottle");
            if (!journal || !bottle) { Debug.LogWarning("[ASH] Run Install Field Manual first."); return; }
            var wo = Object.FindAnyObjectByType<ASH.World.Weather.ASHWeatherOutputs>();
            var interior = wo ? wo.interior : null;
            if (!interior) { Debug.LogWarning("[ASH] No cabin interior (ASHWeatherOutputs.interior) found. Use the 'at Scene view' menus instead."); return; }
            var b = interior.bounds;
            Vector3 Drop(Vector3 o)
            {
                var top = new Vector3(b.center.x + o.x, b.max.y - 0.1f, b.center.z + o.z);
                var hits = Physics.RaycastAll(top, Vector3.down, b.size.y + 1f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                foreach (var h in hits) if (h.point.y > b.min.y - 0.3f) return h.point + Vector3.up * 0.005f;
                return new Vector3(top.x, b.min.y, top.z);
            }
            var j = PlacePickup(journal, "ASH_Pickup_GrandpasJournal", Drop(new Vector3(b.extents.x * 0.35f, 0, b.extents.z * 0.2f)), 20f, PrimitiveType.Cube, new Vector3(0.17f, 0.035f, 0.24f));
            var w = PlacePickup(bottle, "ASH_Pickup_WaterBottle", Drop(new Vector3(-b.extents.x * 0.35f, 0, b.extents.z * 0.2f)), 0f, PrimitiveType.Cylinder, new Vector3(0.08f, 0.12f, 0.08f));
            Selection.objects = new Object[] { j, w };
            Debug.Log($"[ASH] Placed Grandpa's Journal at {j.transform.position:F2} and Water Bottle at {w.transform.position:F2} (scene not saved - move them onto a table/shelf if you like, then save).");
        }

        [MenuItem("ASH/Systems/Field Manual/Place Journal at Scene View", false, 122)]
        public static void PlaceJournalHere() { var it = Item("GrandpasJournal"); if (it && SceneViewPoint(out var p, out var y)) Selection.activeObject = PlacePickup(it, "ASH_Pickup_GrandpasJournal", p, y, PrimitiveType.Cube, new Vector3(0.17f, 0.035f, 0.24f)); }

        [MenuItem("ASH/Systems/Field Manual/Place Water Bottle at Scene View", false, 123)]
        public static void PlaceBottleHere() { var it = Item("WaterBottle"); if (it && SceneViewPoint(out var p, out var y)) Selection.activeObject = PlacePickup(it, "ASH_Pickup_WaterBottle", p, y, PrimitiveType.Cylinder, new Vector3(0.08f, 0.12f, 0.08f)); }

        [MenuItem("ASH/Systems/Field Manual/Place Torn Page at Scene View (next missing number)", false, 140)]
        public static void PlacePage()
        {
            if (!SceneViewPoint(out var p, out var yaw)) return;
            var d = AssetDatabase.LoadAssetAtPath<ASHFieldManualData>(DataPath); int total = d ? d.totalPages : 12;
            var used = new HashSet<int>(Object.FindObjectsByType<ASHManualPagePickup>(FindObjectsInactive.Include).Select(x => x.pageNumber));
            int n = 1; while (used.Contains(n) && n <= total) n++;
            if (n > total) { Debug.LogWarning($"[ASH] All {total} manual pages are already placed."); return; }
            var root = GameObject.Find("ASH_ManualPages") ?? new GameObject("ASH_ManualPages");
            var go = new GameObject($"ASH_ManualPage_{n:00}"); go.transform.SetParent(root.transform, true);
            go.transform.SetPositionAndRotation(p + Vector3.up * 0.004f, Quaternion.Euler(0, yaw + Random.Range(-40f, 40f), 0));
            var vis = GameObject.CreatePrimitive(PrimitiveType.Cube); vis.name = "Visual"; Object.DestroyImmediate(vis.GetComponent<Collider>());
            vis.transform.SetParent(go.transform, false); vis.transform.localScale = new Vector3(0.2f, 0.004f, 0.27f); vis.transform.localRotation = Quaternion.Euler(0, 0, 2f);
            vis.GetComponent<Renderer>().sharedMaterial = Mat("manual_page", new Color(0.86f, 0.81f, 0.7f));
            var col = go.AddComponent<BoxCollider>(); col.isTrigger = true; col.size = new Vector3(0.4f, 0.25f, 0.45f); col.center = Vector3.up * 0.1f;
            int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) { go.layer = layer; vis.layer = layer; }
            go.AddComponent<ASHManualPagePickup>().pageNumber = n;
            Undo.RegisterCreatedObjectUndo(go, "Place manual page");
            EditorSceneManager.MarkSceneDirty(go.scene); Selection.activeObject = go;
            var pg = d ? d.Page(n) : null;
            Debug.Log($"[ASH] Placed torn page {n} of {total}{(pg != null ? $" ('{pg.title}')" : "")} at {p:F1}. Change pageNumber on it to use another page.");
        }
    }
}

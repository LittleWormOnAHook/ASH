using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASH.EditorTools.Build;
using ASH.Gameplay;
using ASH.Interaction;
using ASH.Inventory;
using ASH.World.Accumulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools.World
{
    /// <summary>
    /// HQ Survival Pack (Assets/HQ Survival Pack/Prefabs; vendor files untouched, nested in ASH wrappers):
    /// - Pickup visuals: ASH_Pickup_* prefabs keep their root (ASHItemPickup, item, amount, scene positions) and get the vendor
    ///   model as "Visual" (accumulation stripped, plain materials, vendor colliders/rigidbodies off, one trigger).
    /// - World props: salvage crate, stone pile and the placeable campfire get vendor visuals.
    /// - Held items (Assets/_ASH/Prefabs/Held): weapons, hatchet, lantern (off/on), torch (flame + light), flashlight (spot).
    /// - New items/recipes (ASHItemCatalog.PackDefs/PackRecipes), item equip wiring, ASHEquipment on the player, icons,
    ///   and a few placed pickups for the new items next to existing ones (parent ASH_SurvivalPackPickups).
    /// Menus under ASH/World/Survival Pack.
    /// </summary>
    public static class ASHSurvivalPackTools
    {
        const string HQ = "Assets/HQ Survival Pack/Prefabs/";
        public const string HeldDir = ASHContentBuilder.Root + "/Prefabs/Held";
        public const string PickupDir = ASHContentBuilder.Root + "/Prefabs/Pickups";
        public const string PlaceRoot = "ASH_SurvivalPackPickups";

        enum Lay { Keep, Flat }
        static readonly (string file, string vendor, Lay lay, string hide)[] Visuals =
        {
            ("Cloth", "Resources/Cloth", Lay.Keep, null),
            ("ScrapMetal", "Resources/MetalSheet", Lay.Flat, null),
            ("EmptyTin", "Food/SmallFoodCan01", Lay.Keep, null),
            ("CannedBeans", "Food/LargeFoodCan01", Lay.Keep, null),
            ("CannedPeaches", "Food/LargeFoodCan02", Lay.Keep, null),
            ("TinOfWater", "Food/SmallFoodCan02", Lay.Keep, null),
            ("BoiledWater", "Food/SmallFoodCan02", Lay.Keep, null),
            ("RawMeat", "Food/RawMeat", Lay.Keep, null),
            ("CookedMeat", "Food/CookedMeat", Lay.Keep, null),
            ("Painkillers", "Food/Pills", Lay.Keep, null),
            ("Hatchet", "Tools/MetalHatchet", Lay.Flat, null),
            ("HuntingKnife", "Weapons/Melee/HuntingKnife", Lay.Flat, null),
            ("Firewood", "Resources/Log", Lay.Flat, null),
            ("Kindling", "Resources/Stick", Lay.Flat, null),
            ("HuntingRifle", "Weapons/Guns&RangedWeapons/HuntingRifle", Lay.Flat, null),
            ("Pistol", "Weapons/Guns&RangedWeapons/Pistol", Lay.Flat, null),
            ("WoodenBow", "Weapons/Guns&RangedWeapons/WoodenBow", Lay.Flat, "WoodenArrow"),
            ("WoodenArrow", "Weapons/Guns&RangedWeapons/WoodenBow", Lay.Flat, "WoodenBow"),
            ("Lantern", "Objects/Lantern", Lay.Keep, null),
            ("Torch", "Tools/Torch", Lay.Flat, null),
            ("Flashlight", "Tools/Flashlight", Lay.Flat, null),
            ("Battery", "Tools/GenericTools/Battery", Lay.Keep, null),
            ("Rope", "Resources/Rope", Lay.Keep, null),
            ("FirstAidKit", "Objects/MedKit", Lay.Keep, null),
            ("EnergyBar", "Food/EnergyBar", Lay.Flat, null),
            ("SodaCan", "Food/SodaCan", Lay.Keep, null),
            ("BottledWater", "Food/WaterBottle", Lay.Keep, null),
        };

        // held: item, vendor, slot, grip, grip fraction from the tail along +Z (Handle), head on the vertex-heavy end?
        static readonly (string file, string vendor, ASHEquipSlot slot, ASHGripStyle grip, float gripAt, bool headHeavy, string ammo)[] Held =
        {
            ("Hatchet", "Tools/MetalHatchet", ASHEquipSlot.MainHand, ASHGripStyle.Handle, 0.7f, true, null),
            ("HuntingRifle", "Weapons/Guns&RangedWeapons/HuntingRifle", ASHEquipSlot.MainHand, ASHGripStyle.Handle, 0.3f, false, "RifleRounds"),
            ("Pistol", "Weapons/Guns&RangedWeapons/Pistol", ASHEquipSlot.MainHand, ASHGripStyle.PistolGrip, 0.25f, false, "PistolRounds"),
            ("WoodenBow", "Weapons/Guns&RangedWeapons/WoodenBow", ASHEquipSlot.MainHand, ASHGripStyle.Handle, 0.5f, true, "WoodenArrow"),
            ("Torch", "Tools/Torch", ASHEquipSlot.OffHand, ASHGripStyle.Handle, 0.25f, true, null),
            ("Flashlight", "Tools/Flashlight", ASHEquipSlot.OffHand, ASHGripStyle.Beam, 0.35f, true, null),
            ("Lantern", "Objects/Lantern", ASHEquipSlot.Hip, ASHGripStyle.Hang, 0f, false, null),
        };

        static string PickupPath(string file) => $"{PickupDir}/ASH_Pickup_{file}.prefab";
        static string HeldPath(string file) => $"{HeldDir}/ASH_Held_{file}.prefab";
        static ASHItemDefinition Item(string file) => ASHContentBuilder.LoadItem("ASH_Item_" + file);
        static GameObject Vendor(string rel) => AssetDatabase.LoadAssetAtPath<GameObject>(HQ + rel + ".prefab");

        // ------------------------------------------------------------------ menus
        [MenuItem("ASH/World/Survival Pack/1. Build Items, Pickup Visuals, Held Items, Icons", priority = 60)]
        public static void BuildMenu() => Debug.Log(Build(true));
        [MenuItem("ASH/World/Survival Pack/2. Place New Item Pickups In Scene (replaces previous)", priority = 61)]
        public static void PlaceMenu() => Debug.Log(PlacePickups());

        /// <summary>exec: build everything, place the new pickups, save the scene.</summary>
        public static void RunAll()
        {
            Debug.Log(Build(true));
            Debug.Log(PlacePickups());
            var s = EditorSceneManager.GetActiveScene(); EditorSceneManager.SaveScene(s);
            Debug.Log("[ASH] Survival pack RunAll done (scene saved)");
        }

        /// <summary>Called at the end of ASHItemCatalog.Build() so a full catalog rebuild keeps the vendor visuals.</summary>
        public static void ApplyAfterCatalog() => Debug.Log(Build(false));

        public static string Build(bool full)
        {
            var log = new StringBuilder("[ASH] Survival pack: ");
            if (full) { var r = ASHItemCatalog.BuildPackContent(); log.Append($"{ASHItemCatalog.PackItemFiles.Length} pack items, {r.Count} recipes; "); }
            int v = 0; foreach (var e in Visuals) if (PickupVisual(e.file, e.vendor, e.lay, e.hide, log)) v++;
            log.Append($"pickup visuals {v}/{Visuals.Length}; ");
            WorldProps(log);
            ASHBuildUtil.Folder(HeldDir);
            int h = 0; foreach (var e in Held) if (HeldPrefab(e.file, e.vendor, e.grip, e.gripAt, e.headHeavy, log)) h++;
            BuildArrowPrefab(log);
            log.Append($"held {h}/{Held.Length}; ");
            WireItems();
            AddEquipmentToPlayer(log);
            log.Append("held items: ").Append(ASH.EditorTools.Build.ASHHeldItemsSetup.Build()).Append("; ");
            ASHBuildUtil.SaveOwn();
            if (full) BakeIcons(log);
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        // ------------------------------------------------------------------ pickups
        static bool PickupVisual(string file, string vendorRel, Lay lay, string hide, StringBuilder log)
        {
            string path = PickupPath(file); var vendor = Vendor(vendorRel);
            if (!File.Exists(path) || !vendor) { log.Append($"skip {file} (prefab {File.Exists(path)}, vendor {(bool)vendor}); "); return false; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                for (int i = root.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
                foreach (var c in root.GetComponents<Collider>()) Object.DestroyImmediate(c);
                var vis = new GameObject("Visual"); vis.transform.SetParent(root.transform, false);
                var inst = NestVendor(vendor, vis.transform, hide);
                if (lay == Lay.Flat) LayFlat(inst);
                var b = Bounds(inst);
                vis.transform.localPosition = new Vector3(-(b.center.x - root.transform.position.x), -(b.min.y - root.transform.position.y), -(b.center.z - root.transform.position.z));
                b = Bounds(inst);
                var bc = root.AddComponent<BoxCollider>(); bc.isTrigger = true;
                bc.size = new Vector3(Mathf.Max(0.3f, b.size.x + 0.08f), Mathf.Max(0.3f, b.size.y + 0.08f), Mathf.Max(0.3f, b.size.z + 0.08f));
                bc.center = root.transform.InverseTransformPoint(b.center); bc.center = new Vector3(bc.center.x, Mathf.Max(bc.center.y, bc.size.y * 0.5f), bc.center.z);
                int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root, layer);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return true;
        }

        /// <summary>Nested vendor instance, cleaned for an interactable: no accumulation, plain materials, no colliders/rigidbodies.</summary>
        static GameObject NestVendor(GameObject vendor, Transform parent, string hideChild = null, bool keepColliders = false)
        {
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(vendor, parent);
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
            foreach (var a in inst.GetComponentsInChildren<ASHAccumulation>(true)) { a.Revert(); Object.DestroyImmediate(a); }
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < ms.Length; i++) if (ms[i] && ms[i].shader.name == ASHAccumulation.ShaderName) { ms[i] = PlainCopy(ms[i]); ch = true; }
                if (ch) r.sharedMaterials = ms;
            }
            foreach (var rb in inst.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
            if (!keepColliders) foreach (var c in inst.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            if (!string.IsNullOrEmpty(hideChild))
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (t != inst.transform && t.name == hideChild) t.gameObject.SetActive(false);
            return inst;
        }

        static Material PlainCopy(Material m)
        {
            string dir = ASHContentBuilder.Root + "/Materials/Plain"; ASHBuildUtil.Folder(dir);
            var path = $"{dir}/{m.name.Replace("_Accum", "")}_Plain.mat";
            var ex = AssetDatabase.LoadAssetAtPath<Material>(path); if (ex) return ex;
            var c = new Material(m) { shader = Shader.Find(ASHAccumulation.SourceShaderName) }; c.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(c, path); return c;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.2f);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// <summary>Rotates so the thinnest bounds axis is vertical (long items lie on the ground).</summary>
        static void LayFlat(GameObject inst)
        {
            var s = Bounds(inst).size;
            if (s.y <= s.x && s.y <= s.z) return;
            inst.transform.localRotation = s.x <= s.z ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
        }

        // ------------------------------------------------------------------ world props
        static void WorldProps(StringBuilder log)
        {
            ReplaceVisual(ASHContentBuilder.WorldPrefabs + "/ASH_SalvageCrate.prefab", "Objects/WoodenCrate01", true, log);
            ReplaceVisual(ASHContentBuilder.WorldPrefabs + "/ASH_StonePile.prefab", "Decoration/Ores/Stone", true, log);
            // placeable campfire: vendor stones + wood, keep ASH flames/coals/sparks/smoke/light
            string cf = ASHContentBuilder.WorldPrefabs + "/ASH_Campfire.prefab"; var v = Vendor("Objects/Campfire");
            if (File.Exists(cf) && v)
            {
                var root = PrefabUtility.LoadPrefabContents(cf);
                try
                {
                    for (int i = root.transform.childCount - 1; i >= 0; i--)
                    {
                        var c = root.transform.GetChild(i);
                        if (c.name.StartsWith("Stone") || c.name.StartsWith("Log") || c.name == "Visual") Object.DestroyImmediate(c.gameObject);
                    }
                    var vis = new GameObject("Visual"); vis.transform.SetParent(root.transform, false); vis.transform.SetSiblingIndex(0);
                    bool rootCol = root.GetComponent<Collider>();
                    var inst = NestVendor(v, vis.transform, null, !rootCol);
                    var b = Bounds(inst); vis.transform.localPosition = new Vector3(0, -(b.min.y - root.transform.position.y), 0);
                    PrefabUtility.SaveAsPrefabAsset(root, cf);
                    log.Append($"ASH_Campfire <- Campfire (root collider {rootCol}); ");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
        }

        /// <summary>Swaps the children of a prop's "Visual" for a vendor model; refits the root BoxCollider. Root components (nodes) stay.</summary>
        static void ReplaceVisual(string path, string vendorRel, bool fitCollider, StringBuilder log)
        {
            var v = Vendor(vendorRel); if (!File.Exists(path) || !v) { log.Append($"skip {Path.GetFileName(path)}; "); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var vis = root.transform.Find("Visual"); if (!vis) { vis = new GameObject("Visual").transform; vis.SetParent(root.transform, false); }
                for (int i = vis.childCount - 1; i >= 0; i--) Object.DestroyImmediate(vis.GetChild(i).gameObject);
                foreach (var r in vis.GetComponents<Renderer>()) Object.DestroyImmediate(r);
                foreach (var f in vis.GetComponents<MeshFilter>()) Object.DestroyImmediate(f);
                vis.localPosition = Vector3.zero; vis.localRotation = Quaternion.identity; vis.localScale = Vector3.one;
                var inst = NestVendor(v, vis);
                var b = Bounds(inst); inst.transform.localPosition = new Vector3(0, -(b.min.y - root.transform.position.y), 0);
                b = Bounds(inst);
                var bc = root.GetComponent<BoxCollider>();
                if (fitCollider && bc) { bc.center = root.transform.InverseTransformPoint(b.center); bc.size = Vector3.Max(b.size, Vector3.one * 0.3f); }
                int layer = LayerMask.NameToLayer("Resource"); if (layer >= 0) ASHBuildUtil.SetLayerRecursive(root, layer);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                log.Append($"{Path.GetFileNameWithoutExtension(path)} <- {Path.GetFileName(vendorRel)}; ");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // ------------------------------------------------------------------ held items
        static bool HeldPrefab(string file, string vendorRel, ASHGripStyle grip, float gripAt, bool headHeavy, StringBuilder log)
        {
            var v = Vendor(vendorRel); if (!v) { log.Append($"held skip {file}; "); return false; }
            var root = new GameObject("ASH_Held_" + file);
            try
            {
                if (file == "Lantern")
                {
                    var on = Vendor("Objects/Lantern_ON");
                    var offGo = new GameObject("Off"); offGo.transform.SetParent(root.transform, false);
                    var onGo = new GameObject("On"); onGo.transform.SetParent(root.transform, false);
                    var a = NestVendor(v, offGo.transform); var bOn = on ? NestVendor(on, onGo.transform) : null;
                    var b = Bounds(a); var top = new Vector3(b.center.x, b.max.y, b.center.z);
                    offGo.transform.position -= top; onGo.transform.position -= top;
                    var l = onGo.GetComponentInChildren<Light>(true);
                    if (!l) { var lg = new GameObject("Point light"); lg.transform.SetParent(onGo.transform, false); lg.transform.localPosition = new Vector3(0, -b.size.y * 0.5f, 0); l = lg.AddComponent<Light>(); }
                    l.type = LightType.Point; l.color = new Color(1f, 0.78f, 0.5f); l.range = Mathf.Max(l.range, 9f); l.shadows = LightShadows.None;
                    float inten = Mathf.Clamp(l.intensity, 1.2f, 3f); l.intensity = inten;
                    root.AddComponent<ASHLightSource>().Configure(l, new[] { onGo }, new[] { offGo }, null, inten, 0.08f);
                }
                else
                {
                    var holder = new GameObject("Model"); holder.transform.SetParent(root.transform, false);
                    var inst = NestVendor(v, holder.transform, file == "WoodenBow" ? "WoodenArrow" : null); // the arrow rides the right hand
                    AlignLong(inst, holder.transform, grip, gripAt, headHeavy);
                    var b = Bounds(holder);
                    if (file == "Torch")
                    {
                        var fire = ASHContentBuilder.LoadPrefab("ASH_Campfire"); var flames = fire ? fire.transform.Find("Flames") : null;
                        var flameGo = flames ? Object.Instantiate(flames.gameObject, root.transform) : new GameObject("Flames");
                        flameGo.name = "Flame"; flameGo.transform.localPosition = new Vector3(0, 0, b.max.z - 0.02f); flameGo.transform.localScale = Vector3.one * 0.4f;
                        var upright = flames ? flames.localRotation : Quaternion.identity;
                        foreach (var ps in flameGo.GetComponentsInChildren<ParticleSystem>(true)) { var main = ps.main; main.simulationSpace = ParticleSystemSimulationSpace.World; main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.playOnAwake = false; }
                        var lg = new GameObject("TorchLight"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0, b.max.z + 0.1f);
                        var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1f, 0.62f, 0.3f); l.range = 10f; l.intensity = 2.4f; l.shadows = LightShadows.None;
                        var ls = root.AddComponent<ASHLightSource>(); ls.Configure(l, new[] { flameGo }, null, flameGo.GetComponentsInChildren<ParticleSystem>(true), 2.4f, 0.35f);
                        ls.KeepUpright(flameGo.transform, upright);
                    }
                    else if (file == "Flashlight")
                    {
                        var lg = new GameObject("Beam"); lg.transform.SetParent(root.transform, false); lg.transform.localPosition = new Vector3(0, 0, b.max.z + 0.01f);
                        var l = lg.AddComponent<Light>(); l.type = LightType.Spot; l.color = new Color(1f, 0.95f, 0.85f); l.range = 28f; l.spotAngle = 48f; l.innerSpotAngle = 22f; l.intensity = 7f; l.shadows = LightShadows.None;
                        root.AddComponent<ASHLightSource>().Configure(l, null, null, null, 7f, 0f);
                    }
                }
                ASHBuildUtil.SavePrefab(root, HeldPath(file));
                return true;
            }
            catch (System.Exception ex) { log.Append($"held {file} failed: {ex.Message}; "); if (root) Object.DestroyImmediate(root); return false; }
        }

        /// <summary>Long axis to +Z (head end forward), grip point at the origin: gripAt = fraction from the tail along +Z.</summary>
        static void AlignLong(GameObject inst, Transform holder, ASHGripStyle grip, float gripAt, bool headHeavy)
        {
            // vertices in holder space
            var pts = new List<Vector3>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(false))
            {
                var m = mf.sharedMesh; if (!m || !m.isReadable) { if (m) { var bb = m.bounds; for (int i = 0; i < 8; i++) pts.Add(holder.InverseTransformPoint(mf.transform.TransformPoint(bb.center + Vector3.Scale(bb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))))); } continue; }
                foreach (var p in m.vertices) pts.Add(holder.InverseTransformPoint(mf.transform.TransformPoint(p)));
            }
            if (pts.Count == 0) return;
            Vector3 min = pts[0], max = pts[0], sum = Vector3.zero;
            foreach (var p in pts) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); sum += p; }
            var size = max - min; var center = (min + max) * 0.5f; var centroid = sum / pts.Count;
            int ax = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
            Vector3 axis = ax == 0 ? Vector3.right : ax == 1 ? Vector3.up : Vector3.forward;
            float heavy = Vector3.Dot(centroid - center, axis);
            float sign = (heavy >= 0f) == headHeavy ? 1f : -1f;
            var rot = Quaternion.FromToRotation(axis * sign, Vector3.forward);
            inst.transform.localRotation = rot * inst.transform.localRotation;
            inst.transform.localPosition = rot * inst.transform.localPosition;
            // grip point after rotation
            Vector3 rc = rot * center; float half = size[ax] * 0.5f;
            Vector3 gripPoint = rc + Vector3.forward * (-half + size[ax] * gripAt);
            inst.transform.localPosition -= gripPoint;
        }

        // ------------------------------------------------------------------ wiring
        static void WireItems()
        {
            foreach (var e in Held)
            {
                var it = Item(e.file); if (!it) continue;
                it.equipSlot = e.slot; it.gripStyle = e.grip; it.heldScale = e.file == "Hatchet" ? 0.8f : 1f; // hatchet matches the scene axe prefab scale (0.8) it.heldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeldPath(e.file));
                it.ammoItem = e.ammo != null ? Item(e.ammo) : null;
                EditorUtility.SetDirty(it);
            }
            var lantern = Item("Lantern"); if (lantern) { lantern.fuelItem = Item("RubbingAlcohol"); lantern.fuelSecondsPerUnit = 480f; lantern.fuelCapacityUnits = 2; EditorUtility.SetDirty(lantern); }
            var torch = Item("Torch"); if (torch) { torch.fuelItem = null; torch.fuelSecondsPerUnit = 300f; torch.fuelCapacityUnits = 1; EditorUtility.SetDirty(torch); }
            var fl = Item("Flashlight"); if (fl) { fl.fuelItem = Item("Battery"); fl.fuelSecondsPerUnit = 1200f; fl.fuelCapacityUnits = 1; EditorUtility.SetDirty(fl); }
        }

        public const string ArrowPrefabFile = "ArrowProjectile";
        /// <summary>Arrow visual for the hand and the projectile: nock at the origin, tip along +Z, no colliders.</summary>
        static void BuildArrowPrefab(StringBuilder log)
        {
            var v = Vendor("Weapons/Guns&RangedWeapons/WoodenBow"); if (!v) { log.Append("arrow prefab skip; "); return; }
            var root = new GameObject("ASH_Held_" + ArrowPrefabFile);
            try
            {
                var holder = new GameObject("Model"); holder.transform.SetParent(root.transform, false);
                var inst = NestVendor(v, holder.transform, "WoodenBow");
                AlignLong(inst, holder.transform, ASHGripStyle.Handle, 0f, true);
                foreach (var t in inst.GetComponentsInChildren<Transform>(true)) if (!t.gameObject.activeSelf) Object.DestroyImmediate(t.gameObject);
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                ASHBuildUtil.SavePrefab(root, HeldPath(ArrowPrefabFile));
                log.Append("arrow prefab; ");
            }
            catch (System.Exception ex) { log.Append($"arrow prefab failed: {ex.Message}; "); if (root) Object.DestroyImmediate(root); }
        }

        static void AddEquipmentToPlayer(StringBuilder log)
        {
            var root = PrefabUtility.LoadPrefabContents(ASHPlayerBuilder.PrefabPath);
            try
            {
                bool ch = false;
                if (!root.GetComponent<ASHEquipment>()) { root.AddComponent<ASHEquipment>(); ch = true; log.Append("ASHEquipment added to player prefab; "); }
                var anim = root.GetComponentInChildren<Animator>();
                if (anim && !anim.GetComponent<ASHLightAim>()) { anim.gameObject.AddComponent<ASHLightAim>(); ch = true; log.Append($"ASHLightAim added on {anim.name}; "); }
                if (anim && !anim.GetComponent<ASHActionLayer>()) { anim.gameObject.AddComponent<ASHActionLayer>(); ch = true; }
                if (anim && !anim.GetComponent<ASHChopAnimator>()) { anim.gameObject.AddComponent<ASHChopAnimator>(); ch = true; log.Append("ASHChopAnimator added; "); }
                var wa = anim ? anim.GetComponent<ASHWeaponAim>() : null;
                if (anim && !wa) { wa = anim.gameObject.AddComponent<ASHWeaponAim>(); ch = true; log.Append("ASHWeaponAim added; "); }
                var arrow = AssetDatabase.LoadAssetAtPath<GameObject>(HeldPath(ArrowPrefabFile));
                if (wa && arrow && wa.arrowPrefab != arrow) { wa.arrowPrefab = arrow; ch = true; log.Append("arrow prefab wired; "); }
                if (ch) PrefabUtility.SaveAsPrefabAsset(root, ASHPlayerBuilder.PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            var pl = GameObject.Find("ASH_Player");
            if (pl && !pl.GetComponent<ASHEquipment>()) { Undo.AddComponent<ASHEquipment>(pl); EditorSceneManager.MarkSceneDirty(pl.scene); log.Append("ASHEquipment added to scene player; "); }
            var pa = pl ? pl.GetComponentInChildren<Animator>() : null;
            if (pa && !pa.GetComponent<ASHLightAim>()) { Undo.AddComponent<ASHLightAim>(pa.gameObject); EditorSceneManager.MarkSceneDirty(pl.scene); log.Append("ASHLightAim added to scene player; "); }
        }

        static void BakeIcons(StringBuilder log)
        {
            var only = new HashSet<string>(Visuals.Select(e => "ASH_Item_" + e.file));
            foreach (var f in ASHItemCatalog.PackItemFiles) only.Add("ASH_Item_" + f);
            only.Add("ASH_Item_PlantFiber"); only.Add("ASH_Item_Stone"); only.Add("ASH_Item_Campfire");
            ASHIconBaker.BakeAll(only);
            int missing = only.Count(f => { var i = ASHContentBuilder.LoadItem(f); return !i || !i.icon; });
            log.Append($"icons baked for {only.Count} items (missing {missing}); ");
        }

        // ------------------------------------------------------------------ placement of the new items
        static readonly (string anchor, int nth, string file, int amount, Vector3 offset)[] Placements =
        {
            ("Hatchet", 0, "Lantern", 1, new Vector3(1.1f, 0, 0.4f)),
            ("Hatchet", 0, "WoodenBow", 1, new Vector3(-1.2f, 0, 0.5f)),
            ("Hatchet", 0, "WoodenArrow", 6, new Vector3(-1.0f, 0, -0.6f)),
            ("HuntingKnife", 0, "Flashlight", 1, new Vector3(0.8f, 0, 0.3f)),
            ("HuntingKnife", 0, "Battery", 2, new Vector3(0.8f, 0, -0.4f)),
            ("CanOpener", 0, "Pistol", 1, new Vector3(0.7f, 0, 0.2f)),
            ("CanOpener", 0, "PistolRounds", 8, new Vector3(0.7f, 0, -0.4f)),
            ("Whiskey", 0, "HuntingRifle", 1, new Vector3(0.9f, 0, 0.3f)),
            ("Whiskey", 0, "RifleRounds", 5, new Vector3(0.9f, 0, -0.4f)),
            ("CannedPeaches", 0, "EnergyBar", 2, new Vector3(0.6f, 0, 0.3f)),
            ("CannedPeaches", 1, "SodaCan", 1, new Vector3(0.6f, 0, 0.3f)),
            ("CannedPeaches", 2, "BottledWater", 1, new Vector3(0.6f, 0, 0.3f)),
            ("CannedPeaches", 3, "Rope", 1, new Vector3(0.6f, 0, 0.3f)),
            ("CannedPeaches", 4, "FirstAidKit", 1, new Vector3(0.6f, 0, 0.3f)),
            ("CannedPeaches", 5, "Battery", 1, new Vector3(0.6f, 0, 0.3f)),
        };

        static List<GameObject> Instances(string prefabPath) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include).Select(t => t.gameObject)
                .Where(g => g.scene.IsValid() && PrefabUtility.IsOutermostPrefabInstanceRoot(g) && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(g) == prefabPath && (PlaceRootT() == null || !g.transform.IsChildOf(PlaceRootT())))
                .OrderBy(g => g.transform.position.x).ThenBy(g => g.transform.position.z).ToList();
        static Transform _placeRoot;
        static Transform PlaceRootT() { if (!_placeRoot) { var g = GameObject.Find(PlaceRoot); _placeRoot = g ? g.transform : null; } return _placeRoot; }

        public static string PlacePickups()
        {
            var old = GameObject.Find(PlaceRoot); if (old) Undo.DestroyObjectImmediate(old);
            _placeRoot = null;
            var parent = new GameObject(PlaceRoot); Undo.RegisterCreatedObjectUndo(parent, "Place survival pack pickups"); _placeRoot = parent.transform;
            var sb = new StringBuilder("[ASH] Placed survival pack pickups: "); int n = 0;
            var rnd = new System.Random(11);
            int mask = ~(1 << Mathf.Max(0, LayerMask.NameToLayer("Resource")));
            foreach (var p in Placements)
            {
                var anchors = Instances(PickupPath(p.anchor));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPath(p.file)); var item = Item(p.file);
                if (anchors.Count <= p.nth || !prefab || !item) { sb.Append($"skip {p.file} (anchor {p.anchor}#{p.nth} {anchors.Count}); "); continue; }
                var a = anchors[p.nth].transform;
                // same surface as the anchor (table, roof) at the full offset if possible; otherwise the floor/ground at the full
                // offset when the anchor is on furniture (< 2.5 m above it); otherwise closer to the anchor on its surface
                Vector3 pos = a.position; bool found = false; Vector3? floor = null;
                foreach (float k in new[] { 1f, 0.6f, 0.35f })
                {
                    var q = a.position + Quaternion.Euler(0, a.eulerAngles.y, 0) * (p.offset * k);
                    if (!Physics.Raycast(q + Vector3.up * 1.2f, Vector3.down, out var hit, 4f, mask, QueryTriggerInteraction.Ignore)) continue;
                    float dy = a.position.y - hit.point.y;
                    if (Mathf.Abs(dy) < 0.4f) { pos = hit.point; found = true; break; }
                    if (k == 1f && dy > 0f && dy < 2.5f) floor = hit.point;
                }
                if (!found && floor.HasValue) pos = floor.Value;
                else if (!found) pos = a.position + Quaternion.Euler(0, a.eulerAngles.y, 0) * (p.offset * 0.35f);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.scene);
                go.transform.SetParent(parent.transform, true);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0));
                var pk = go.GetComponent<ASHItemPickup>(); if (pk) { pk.Configure(item, p.amount); EditorUtility.SetDirty(pk); PrefabUtility.RecordPrefabInstancePropertyModifications(pk); }
                n++; sb.Append($"{p.file}x{p.amount}@{p.anchor}; ");
            }
            EditorSceneManager.MarkSceneDirty(parent.scene);
            return sb.Append($"total {n}").ToString();
        }
    }
}

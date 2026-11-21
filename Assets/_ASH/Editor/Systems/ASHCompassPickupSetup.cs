using System;
using System.IO;
using System.Text;
using ASH.EditorTools.Build;
using ASH.Interaction;
using ASH.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Items/Cabin Compass Pickup (idempotent, t322):
    /// - ASH_Item_Compass (Tool, stack 1, "Use"): using it unlocks the top-of-screen compass for good and removes the item (ASHCompassUnlock).
    /// - Icon ASH_Icon_Compass.png copied from the HQ Survival Pack compass icon into Assets/_ASH/Art/Icons (Sprite, alpha).
    /// - The HQ Survival Pack compass already placed in/around Grandpa's cabin becomes the pickup (trigger + ASHItemPickup, scene
    ///   overrides only; the vendor prefab is untouched). The scene is left UNSAVED.
    /// </summary>
    public static class ASHCompassPickupSetup
    {
        const string IconDir = "Assets/_ASH/Art/Icons";
        const string IconSrc = "Assets/HQ Survival Pack/Icons/Tools/Compass.png";
        const string IconPath = IconDir + "/ASH_Icon_Compass.png";
        const string CompassPrefab = "Assets/HQ Survival Pack/Prefabs/Tools/Compass.prefab";
        const string GpsPrefab = "Assets/_ASH/Prefabs/Pickups/ASH_Pickup_DamagedGPS.prefab";
        public const string PickupName = "ASH_Pickup_Compass_Cabin";
        const string Desc = "Grandpa's old brass compass. Use it to keep a heading at the top of the screen.";

        [MenuItem("ASH/Systems/Items/Cabin Compass Pickup")]
        public static void Run()
        {
            var sb = new StringBuilder("[ASH] Compass: ");
            ASHItemDefinition it = null;
            try { it = EnsureItem(sb); } catch (Exception e) { Debug.LogException(e); sb.Append("item FAILED; "); }
            try { if (it) Icon(it, sb); } catch (Exception e) { Debug.LogException(e); sb.Append("icon FAILED; "); }
            try { if (it) Pickup(it, sb); } catch (Exception e) { Debug.LogException(e); sb.Append("pickup FAILED; "); }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        static ASHItemDefinition EnsureItem(StringBuilder sb)
        {
            string path = $"{ASHContentBuilder.ItemDir}/ASH_Item_Compass.asset";
            var it = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(path); bool made = false;
            if (!it) { it = ScriptableObject.CreateInstance<ASHItemDefinition>(); AssetDatabase.CreateAsset(it, path); made = true; }
            it.itemId = ASH.UI.ASHCompassUnlock.ItemId; it.displayName = "Compass"; it.description = Desc;
            it.category = ASHItemCategory.Tool; it.maxStack = 1; it.useVerb = "Use"; it.toolType = ASHToolType.None; it.equipSlot = ASHEquipSlot.None;
            it.uiTint = new Color(0.78f, 0.62f, 0.35f);
            EditorUtility.SetDirty(it);
            sb.Append($"item {(made ? "created" : "updated")} ({path}); ");
            return it;
        }

        static void Icon(ASHItemDefinition it, StringBuilder sb)
        {
            string proj = Path.GetDirectoryName(Application.dataPath);
            string src = Path.Combine(proj, IconSrc), dst = Path.Combine(proj, IconPath);
            if (!File.Exists(dst))
            {
                if (!File.Exists(src)) { sb.Append("icon source missing (HQ Survival Pack Icons/Tools/Compass.png); "); return; }
                File.Copy(src, dst, false); AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceUpdate);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            if (imp)
            {
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.maxTextureSize = 256;
                imp.textureCompression = TextureImporterCompression.Uncompressed; imp.SaveAndReimport();
            }
            it.icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath); EditorUtility.SetDirty(it);
            sb.Append($"icon {(it.icon ? "ok" : "MISSING")}; ");
        }

        static void Pickup(ASHItemDefinition it, StringBuilder sb)
        {
            foreach (var p in Object.FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Include))
                if (p && p.gameObject.scene.IsValid() && (p.name == PickupName || p.Item == it)) { sb.Append($"pickup kept ({p.name} at {p.transform.position:F2}); "); return; }
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(CompassPrefab);
            // reference point: the cabin GPS pickup (Grandpa's cabin)
            Vector3 cabin = new Vector3(521.42f, 306.11f, 1557.94f);
            var gpsGo = GameObject.Find(ASHItemIconsGpsSetup.GpsName); if (gpsGo) cabin = gpsGo.transform.position;
            GameObject best = null; float bestD = float.MaxValue; int n = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                var g = t.gameObject; if (!g.scene.IsValid() || EditorUtility.IsPersistent(g)) continue;
                bool isCompass = src && PrefabUtility.IsAnyPrefabInstanceRoot(g) && PrefabUtility.GetCorrespondingObjectFromOriginalSource(g) == src;
                if (!isCompass && !(PrefabUtility.GetOutermostPrefabInstanceRoot(g) == null && g.name.StartsWith("Compass", StringComparison.OrdinalIgnoreCase) && g.GetComponentInChildren<Renderer>())) continue;
                n++; float d = Vector3.Distance(g.transform.position, cabin);
                if (d < bestD) { bestD = d; best = g; }
            }
            if (!best) { sb.Append("no compass model found in the open scene (HQ Survival Pack Compass); "); return; }
            if (bestD > 60f) sb.Append($"WARNING nearest compass is {bestD:F0} m from the cabin; ");
            Undo.RegisterFullObjectHierarchyUndo(best, "Cabin compass pickup");
            best.name = PickupName;
            var gpsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GpsPrefab); if (gpsPrefab) best.layer = gpsPrefab.layer;
            // trigger sized to the model (local space), like the GPS pickup's
            var rends = best.GetComponentsInChildren<Renderer>(); var b = new Bounds(best.transform.position, Vector3.zero); bool has = false;
            foreach (var r in rends) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            var box = Undo.AddComponent<BoxCollider>(best); box.isTrigger = true;
            var ls = best.transform.lossyScale; Vector3 inv = new Vector3(1f / Mathf.Max(1e-4f, Mathf.Abs(ls.x)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ls.y)), 1f / Mathf.Max(1e-4f, Mathf.Abs(ls.z)));
            box.center = best.transform.InverseTransformPoint(b.center);
            var sz = Vector3.Scale(b.size + new Vector3(0.12f, 0.1f, 0.12f), inv); box.size = new Vector3(Mathf.Max(sz.x, 0.05f), Mathf.Max(sz.y, 0.05f), Mathf.Max(sz.z, 0.05f));
            var pk = Undo.AddComponent<ASHItemPickup>(best);
            var so = new SerializedObject(pk); so.FindProperty("item").objectReferenceValue = it; so.FindProperty("amount").intValue = 1; so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(best.scene);
            sb.Append($"pickup made from the placed compass ({n} found, nearest {bestD:F1} m from the cabin GPS) at {best.transform.position:F2}, scene UNSAVED; ");
        }
    }
}
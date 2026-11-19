using System;
using System.IO;
using System.Text;
using ASH.EditorTools.Build;
using ASH.Interaction;
using ASH.Inventory;
using Invector.vShooter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASH.EditorTools
{
    /// <summary>
    /// Idempotent setup for t289-t295:
    /// - Inventory icons: Rifle Rounds / Pistol Rounds / 12 Gauge Shells and the Pump Shotgun from the HQ Survival Pack icons
    ///   (copied into Assets/_ASH/Art/Icons so they're in git). Grandpa's Journal and the Damaged GPS use the painted icons
    ///   committed in Art/Icons (ASH_Icon_GrandpasJournal / ASH_Icon_DamagedGPS), assigned here.
    /// - A Damaged GPS pickup next to Grandpa's journal in the cabin (scene left unsaved: save ASH_Prototype).
    /// - ASH_ArrowProjectile: decal entries with no impact effect removed (vDecalManager NRE on hit).
    /// </summary>
    public static class ASHItemIconsGpsSetup
    {
        const string IconDir = "Assets/_ASH/Art/Icons";
        const string AmmoDir = "Assets/HQ Survival Pack/Icons/Additional Icons (these icons don't have 3D models)/Ammunition/";
        const string GunDir = "Assets/HQ Survival Pack/Icons/Weapons/Guns & Ranged Weapons/";
        const string GpsPrefab = "Assets/_ASH/Prefabs/Pickups/ASH_Pickup_DamagedGPS.prefab";
        const string ArrowPrefab = "Assets/_ASH/Prefabs/Combat/ASH_ArrowProjectile.prefab";
        public const string GpsName = "ASH_Pickup_DamagedGPS_Cabin";

        [MenuItem("ASH/Systems/Items/Journal + GPS Pickup, Weapon + Ammo Icons")]
        public static void Run()
        {
            var sb = new StringBuilder("[ASH] Items: ");
            try { AmmoIcons(sb); } catch (Exception e) { Debug.LogException(e); sb.Append("ammo icons FAILED; "); }
            try { ToolIcons(sb); } catch (Exception e) { Debug.LogException(e); sb.Append("journal/GPS icons FAILED; "); }
            try { PlaceGps(sb); } catch (Exception e) { Debug.LogException(e); sb.Append("GPS FAILED; "); }
            try { ArrowDecals(sb); } catch (Exception e) { Debug.LogException(e); sb.Append("arrow decals FAILED; "); }
            AssetDatabase.SaveAssets();
            Debug.Log(sb.ToString());
        }

        static ASHItemDefinition Item(string file) => ASHContentBuilder.LoadItem(file);

        static void AmmoIcons(StringBuilder sb)
        {
            var map = new[] { (AmmoDir + "Hunting Rifle Bullet.Png", "ASH_Icon_RifleRounds", "ASH_Item_RifleRounds"), (AmmoDir + "Pistol Bullet.png", "ASH_Icon_PistolRounds", "ASH_Item_PistolRounds"),
                              (AmmoDir + "Shotgun Shell.png", "ASH_Icon_ShotgunShells", "ASH_Item_ShotgunShells"), (GunDir + "Pump Action Shotgun.png", "ASH_Icon_Shotgun", "ASH_Item_Shotgun") };
            foreach (var (src, dst, itemFile) in map)
            {
                string srcFull = Path.Combine(Path.GetDirectoryName(Application.dataPath), src);
                string dstPath = $"{IconDir}/{dst}.png";
                var it = Item(itemFile);
                if (!it) { sb.Append($"{itemFile} missing; "); continue; }
                if (!File.Exists(srcFull)) { sb.Append($"{Path.GetFileName(src)} not found (HQ Survival Pack); "); continue; }
                File.Copy(srcFull, Path.Combine(Path.GetDirectoryName(Application.dataPath), dstPath), true);
                AssetDatabase.ImportAsset(dstPath, ImportAssetOptions.ForceUpdate);
                SpriteImport(dstPath);
                it.icon = AssetDatabase.LoadAssetAtPath<Sprite>(dstPath); EditorUtility.SetDirty(it);
                sb.Append($"{it.displayName} icon {(it.icon ? "ok" : "MISSING")}; ");
            }
        }

        static void SpriteImport(string path)
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(path); if (!imp) return;
            imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.maxTextureSize = 256;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.SaveAndReimport();
        }

        static ASHItemPickup FindPickup(ASHItemDefinition it)
        {
            if (!it) return null;
            foreach (var p in Object.FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Include))
                if (p && p.Item == it && p.gameObject.scene.IsValid()) return p;
            return null;
        }

        static void ToolIcons(StringBuilder sb)
        {
            foreach (var (icon, itemFile) in new[] { ("ASH_Icon_GrandpasJournal", "ASH_Item_GrandpasJournal"), ("ASH_Icon_DamagedGPS", "ASH_Item_DamagedGPS") })
            {
                string path = $"{IconDir}/{icon}.png"; var it = Item(itemFile);
                if (!it || !File.Exists(Path.Combine(Path.GetDirectoryName(Application.dataPath), path))) { sb.Append($"{icon} or {itemFile} missing; "); continue; }
                SpriteImport(path);
                it.icon = AssetDatabase.LoadAssetAtPath<Sprite>(path); EditorUtility.SetDirty(it);
                sb.Append($"{it.displayName} icon {(it.icon ? "ok" : "MISSING")}; ");
            }
        }

        static void PlaceGps(StringBuilder sb)
        {
            var gpsItem = Item("ASH_Item_DamagedGPS");
            foreach (var p in Object.FindObjectsByType<ASHItemPickup>(FindObjectsInactive.Include))
                if (p && p.gameObject.scene.IsValid() && (p.name == GpsName || (gpsItem && p.Item == gpsItem)))
                { sb.Append($"GPS pickup kept ({p.name} at {p.transform.position:F2}); "); return; }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GpsPrefab);
            var jp = FindPickup(ASHFieldManualData.I ? ASHFieldManualData.I.journalItem : null);
            if (!prefab || !jp) { sb.Append($"GPS not placed (prefab {(bool)prefab}, journal in scene {(bool)jp}); "); return; }
            var jt = jp.transform;
            var own = jp.GetComponentsInChildren<Collider>(true);
            Vector3 right = Vector3.ProjectOnPlane(jt.right, Vector3.up).normalized, fwd = Vector3.ProjectOnPlane(jt.forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.5f) right = Vector3.right; if (fwd.sqrMagnitude < 0.5f) fwd = Vector3.forward;
            float baseY = Bottom(jp.gameObject, jt.position.y);
            Vector3 spot = jt.position + right * 0.3f; float surfY = baseY; bool found = false;
            foreach (var off in new[] { right * 0.3f, -right * 0.3f, fwd * 0.28f, -fwd * 0.28f, right * 0.45f, -right * 0.45f })
            {
                var from = jt.position + off + Vector3.up * 0.4f;
                var hits = Physics.RaycastAll(from, Vector3.down, 0.9f, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    if (Array.IndexOf(own, h.collider) >= 0) continue;
                    if (h.normal.y > 0.9f && Mathf.Abs(h.point.y - baseY) < 0.08f) { spot = h.point; surfY = h.point.y; found = true; }
                    break;
                }
                if (found) break;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, jt.gameObject.scene);
            go.name = GpsName;
            if (jt.parent) go.transform.SetParent(jt.parent, true);
            go.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, jt.eulerAngles.y + 28f, 0f));
            float lift = surfY - Bottom(go, go.transform.position.y); go.transform.position += Vector3.up * (lift + 0.002f);
            Undo.RegisterCreatedObjectUndo(go, "Place cabin GPS");
            EditorSceneManager.MarkSceneDirty(go.scene);
            sb.Append($"GPS pickup placed next to Grandpa's journal ({(found ? "on its surface" : "beside it, no surface hit")}) at {go.transform.position:F2}, scene UNSAVED; ");
        }

        static float Bottom(GameObject g, float fallback)
        {
            bool has = false; float y = 0f;
            foreach (var r in g.GetComponentsInChildren<Renderer>()) { if (!r.enabled) continue; float m = r.bounds.min.y; if (!has || m < y) { y = m; has = true; } }
            return has ? y : fallback;
        }

        static void ArrowDecals(StringBuilder sb)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(ArrowPrefab)) { sb.Append("arrow prefab missing; "); return; }
            var root = PrefabUtility.LoadPrefabContents(ArrowPrefab); int removed = 0;
            try
            {
                foreach (var dm in root.GetComponentsInChildren<vDecalManager>(true))
                {
                    var so = new SerializedObject(dm); var list = so.FindProperty("decalObjects"); if (list == null) continue;
                    for (int i = list.arraySize - 1; i >= 0; i--)
                    {
                        var e = list.GetArrayElementAtIndex(i).FindPropertyRelative("impactEffect");
                        if (e == null || e.objectReferenceValue == null) { list.DeleteArrayElementAtIndex(i); removed++; }
                    }
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                if (removed > 0) PrefabUtility.SaveAsPrefabAsset(root, ArrowPrefab);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            sb.Append($"arrow decal entries without an effect removed: {removed}; ");
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using ASH.Interaction;
using ASH.Inventory;
using ASH.Prologue;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>t462: Grandpa's key + the locked cabin door. Creates the item (ASH_Item_GrandpasKey, "GK" letter icon until Rook's
    /// ASH_Icon_GrandpasKey), a hand-placeable spawn "ASH_GrandpasKey_Spawn" beside the cabin porch with the key pickup on it,
    /// wires the prologue director (cabin door + key pickup) and gives the door a locked rattle / unlock sound if it has none.
    /// Idempotent: an existing spawn (moved by hand) is left where it is. Leaves the scene dirty; it does not save it.</summary>
    public static class ASHPrologueSetupKey
    {
        const string ItemPath = "Assets/_ASH/Data/Items/ASH_Item_GrandpasKey.asset", IconPath = "Assets/_ASH/Art/Icons Placeholder/ASH_Letter_GK.png";
        const string KeyMesh = "Assets/Malbers Animations/Common/Assets/Chest/Key_Mesh.fbx";
        const string LockedClip = "Assets/Audio/Devices/locked.wav", UnlockClip = "Assets/Audio/Doors/Unlock Door.mp3";

        [MenuItem("ASH/Systems/Prologue/Set Up Grandpa's Key + Cabin Door (idempotent)")]
        public static void SetUp()
        {
            var log = new List<string>();
            var reg = ASHItemRegistry.I;
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            var key = AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(ItemPath);
            if (!key)
            {
                var src = reg.Find("carbattery") ?? reg.items.FirstOrDefault(i => i);
                key = Object.Instantiate(src); key.name = "ASH_Item_GrandpasKey";
                key.itemId = "grandpas_key"; key.displayName = "Grandpa's Key"; key.description = "Grandpa's spare key to the cabin, worn smooth.";
                key.category = ASHItemCategory.Key; key.maxStack = 1; key.keyItem = true; key.sellValue = 0; key.useVerb = "Use";
                key.restoreHunger = key.restoreThirst = key.restoreHealth = key.restoreStamina = 0f; key.requiresOpener = false; key.leftoverItem = null; key.sicknessChance = 0f;
                key.toolType = ASHToolType.None; key.breakdownYield = new List<ASHRecipeIngredient>(); key.worldPickupPrefab = null; key.placeablePrefab = null;
                AssetDatabase.CreateAsset(key, ItemPath); log.Add("item created " + ItemPath);
            }
            if (icon && (!key.icon || AssetDatabase.GetAssetPath(key.icon).Contains("Icons Placeholder"))) { key.icon = icon; EditorUtility.SetDirty(key); }
            if (!reg.items.Contains(key)) { reg.items.Add(key); EditorUtility.SetDirty(reg); log.Add("added to ASH_ItemRegistry"); }

            var dir = Object.FindAnyObjectByType<ASHPrologueDirector>(FindObjectsInactive.Include);
            if (!dir) { Debug.LogWarning("[ASH] Key setup: no ASHPrologueDirector in the open scene"); AssetDatabase.SaveAssets(); return; }
            var door = dir.cabinDoor;
            if (!door)
            {
                var c = dir.cabinShelter ? dir.cabinShelter.transform.position : dir.transform.position; float best = float.MaxValue;
                foreach (var d in Object.FindObjectsByType<ASHDoor>(FindObjectsInactive.Include)) { float m = (d.transform.position - c).sqrMagnitude; if (m < best) { best = m; door = d; } }
            }
            if (!door) log.Add("WARNING no ASHDoor found (the key still works as an item)");
            else
            {
                Undo.RecordObject(door, "door sounds");
                if (door.lockedClips == null || door.lockedClips.Length == 0) { var a = AssetDatabase.LoadAssetAtPath<AudioClip>(LockedClip); if (a) { door.lockedClips = new[] { a }; log.Add("door locked sound " + LockedClip); } }
                if (door.unlockClips == null || door.unlockClips.Length == 0) { var a = AssetDatabase.LoadAssetAtPath<AudioClip>(UnlockClip); if (a) { door.unlockClips = new[] { a }; log.Add("door unlock sound " + UnlockClip); } }
                EditorUtility.SetDirty(door);
            }

            var spawn = GameObject.Find("ASH_GrandpasKey_Spawn");
            if (!spawn)
            {
                spawn = new GameObject("ASH_GrandpasKey_Spawn"); Undo.RegisterCreatedObjectUndo(spawn, "key spawn");
                spawn.transform.SetParent(dir.transform, true);
                spawn.transform.position = SpawnPos(dir, door, log);
                log.Add($"spawn placed at {spawn.transform.position:F1} (move it by hand; re-running keeps your spot)");
            }
            else log.Add($"spawn kept at {spawn.transform.position:F1}");
            var pick = spawn.transform.Find("ASH_Pickup_GrandpasKey")?.gameObject;
            if (!pick)
            {
                pick = new GameObject("ASH_Pickup_GrandpasKey"); pick.transform.SetParent(spawn.transform, false);
                var other = Object.FindAnyObjectByType<ASHItemPickup>(FindObjectsInactive.Include); if (other) pick.layer = other.gameObject.layer;
                var mesh = AssetDatabase.LoadAssetAtPath<GameObject>(KeyMesh);
                GameObject vis;
                if (mesh) { vis = (GameObject)PrefabUtility.InstantiatePrefab(mesh); PrefabUtility.UnpackPrefabInstance(vis, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); }
                else { vis = GameObject.CreatePrimitive(PrimitiveType.Cube); vis.transform.localScale = new Vector3(0.12f, 0.01f, 0.04f); }
                vis.name = "Key_Visual"; vis.transform.SetParent(pick.transform, false);
                foreach (var col in vis.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(col);
                var rs = vis.GetComponentsInChildren<Renderer>(); if (rs.Length > 0)
                {
                    var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); float len = Mathf.Max(b.size.x, b.size.y, b.size.z);
                    if (len > 0.0001f && mesh) vis.transform.localScale *= 0.11f / len; // ~11 cm key
                    vis.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * vis.transform.localRotation; // lying flat
                }
                var bc = pick.AddComponent<BoxCollider>(); bc.isTrigger = true; bc.size = new Vector3(0.3f, 0.15f, 0.3f); bc.center = new Vector3(0f, 0.06f, 0f);
                var p = pick.AddComponent<ASHItemPickup>(); var so = new SerializedObject(p); so.FindProperty("item").objectReferenceValue = key; so.FindProperty("amount").intValue = 1; so.ApplyModifiedPropertiesWithoutUndo();
                log.Add("key pickup created (Malbers key mesh" + (mesh ? "" : " missing: cube") + ")");
            }
            var dso = new SerializedObject(dir);
            if (door) dso.FindProperty("cabinDoor").objectReferenceValue = door;
            dso.FindProperty("keyPickup").objectReferenceValue = pick; dso.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Grandpa's key setup:\n - " + string.Join("\n - ", log) + $"\n - door: {(door ? door.name + " at " + door.transform.position.ToString("F1") : "none")}");
        }

        /// <summary>Beside the porch: 1.6 m out from the door and 1.3 m to one side, on the ground, clear of other colliders.</summary>
        static Vector3 SpawnPos(ASHPrologueDirector dir, ASHDoor door, List<string> log)
        {
            Vector3 c = dir.cabinShelter ? dir.cabinShelter.transform.position : dir.transform.position;
            Vector3 d0 = door ? door.transform.position : c + Vector3.forward * 4f;
            Vector3 outw = d0 - c; outw.y = 0f; outw = outw.sqrMagnitude > 0.01f ? outw.normalized : Vector3.forward; var side = Vector3.Cross(Vector3.up, outw);
            var tries = new[] { outw * 1.6f + side * 1.3f, outw * 1.6f - side * 1.3f, outw * 2.4f + side * 1.8f, outw * 2.4f - side * 1.8f, outw * 3.2f };
            foreach (var off in tries)
            {
                var p = d0 + off;
                if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, ~0, QueryTriggerInteraction.Ignore)) continue;
                var g = hit.point + Vector3.up * 0.02f;
                bool blocked = Physics.OverlapSphere(g + Vector3.up * 0.2f, 0.18f, ~0, QueryTriggerInteraction.Ignore).Any(col => !(col is TerrainCollider) && col != hit.collider);
                if (!blocked) return g;
            }
            log.Add("no clear spot found: placed 1.6 m out from the door");
            return d0 + outw * 1.6f;
        }
    }
}
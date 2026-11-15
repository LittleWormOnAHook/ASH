using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ASH.Gameplay;
using ASH.Inventory;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Pre-places every held weapon/item on ASH_Player in edit mode (like Genesis Drawn_ slots), inactive, for placing by hand:
    /// - Right hand: Held_HuntingRifle, Held_Pistol, Held_Hatchet, Held_HandArrow
    /// - Left hand: Held_WoodenBow, Held_Torch, Held_Flashlight
    /// - Hips/Holder_Hip/Held_Lantern
    /// - Held_HuntingRifle/LeftHandIK, Held_Pistol/LeftHandIK: support-hand (left wrist) targets for ASHWeaponAim
    /// ADDS MISSING ONLY: an existing child (found by name anywhere under the player) is never moved, re-scaled, re-parented or
    /// replaced. New children start at the runtime grip captured by ASHHeldCapture (Library/ASH_HeldCapture.txt) when present,
    /// otherwise at the item grip fields. Children are unpacked copies of the Held/ prefabs, so rebuilding those never moves them.
    /// </summary>
    public static class ASHHeldItemsSetup
    {
        [MenuItem("ASH/Build/Held Items On Player (adds missing, never moves placed ones)", priority = 41)]
        public static void Menu() => Debug.Log("[ASH] HeldItems: " + Build());
        public static void Run() => Menu();

        struct Pose { public string bone; public Vector3 p; public Quaternion q; public Vector3 s; }

        public static string Build()
        {
            var cap = LoadCapture();
            var items = AssetDatabase.FindAssets("t:ASHItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ASHItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(i => i && i.IsEquippable && i.heldPrefab).OrderBy(i => i.name).ToList();
            var sb = new StringBuilder($"capture {(cap.Count > 0 ? "yes" : "no")}; ");
            var root = PrefabUtility.LoadPrefabContents(ASHPlayerBuilder.PrefabPath);
            bool changed = false;
            try
            {
                var anim = root.GetComponentInChildren<Animator>();
                if (!anim || !anim.avatar || !anim.avatar.isHuman) return "no humanoid Animator on the player prefab";
                Transform Existing(string n) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);

                foreach (var item in items)
                {
                    string n = ASHEquipment.HeldName(item);
                    if (Existing(n)) { sb.Append($"{n} kept; "); continue; }
                    Transform parent; Pose pose; bool hasPose;
                    if (item.equipSlot == ASHEquipSlot.Hip)
                    {
                        var hips = Bone(anim, HumanBodyBones.Hips); if (!hips) { sb.Append($"{n} skip (no Hips); "); continue; }
                        var holder = Existing(ASHEquipment.HipHolderName);
                        if (!holder)
                        {
                            holder = new GameObject(ASHEquipment.HipHolderName).transform; holder.SetParent(hips, false);
                            if (cap.TryGetValue("holder_hip", out var hp)) { holder.localPosition = hp.p; holder.localRotation = hp.q; holder.localScale = Div(hp.s, hips.lossyScale); }
                            else
                            {
                                var eq = root.GetComponent<ASHEquipment>(); var off = eq ? eq.hipOffset : new Vector3(-0.19f, -0.06f, 0.03f);
                                holder.localPosition = hips.InverseTransformPoint(hips.position + root.transform.TransformDirection(off));
                                holder.localRotation = Quaternion.Inverse(hips.rotation) * root.transform.rotation; holder.localScale = Div(Vector3.one, hips.lossyScale);
                            }
                            sb.Append($"{ASHEquipment.HipHolderName} added; ");
                        }
                        parent = holder; hasPose = cap.TryGetValue(item.itemId, out pose);
                        if (!hasPose) { var eq = root.GetComponent<ASHEquipment>(); float hs = eq ? eq.hipScale : 0.6f; pose = new Pose { p = item.holdPosition, q = Quaternion.Euler(item.holdEuler), s = Vector3.one * hs }; hasPose = true; }
                    }
                    else
                    {
                        parent = Bone(anim, ASHEquipment.InLeftHand(item) ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        if (!parent) { sb.Append($"{n} skip (no hand bone); "); continue; }
                        hasPose = cap.TryGetValue(item.itemId, out pose);
                        if (!hasPose) pose = new Pose { p = item.holdPosition, q = Quaternion.Euler(item.holdEuler), s = Vector3.one * (item.heldScale > 0f ? item.heldScale : 1f) };
                    }
                    Add(item.heldPrefab, parent, n, pose); changed = true;
                    sb.Append($"{n} added under {parent.name}{(hasPose ? "" : " (item grip)")}; ");
                }

                // the right-hand rest arrow for the bow
                var wa = anim.GetComponent<ASHWeaponAim>();
                if (Existing(ASHEquipment.HandArrowName)) sb.Append($"{ASHEquipment.HandArrowName} kept; ");
                else if (wa && wa.arrowPrefab && Bone(anim, HumanBodyBones.RightHand))
                {
                    var rh = Bone(anim, HumanBodyBones.RightHand);
                    var pose = cap.TryGetValue("handarrow", out var ap) ? ap : new Pose { p = wa.arrowIdleOffset, q = Quaternion.Euler(wa.arrowIdleEuler), s = rh.lossyScale };
                    Add(wa.arrowPrefab, rh, ASHEquipment.HandArrowName, pose); changed = true;
                    sb.Append($"{ASHEquipment.HandArrowName} added under {rh.name}; ");
                }
                // gun support-hand targets (wrist pose for the left hand, like Genesis/Invector handIKTarget): added once, never moved
                changed |= AddLeftHandIK(anim, Existing("Held_HuntingRifle"), new Vector3(0f, 0.01f, 0.38f), new Vector3(1f, 0f, 0.35f), Vector3.up, sb);
                changed |= AddLeftHandIK(anim, Existing("Held_Pistol"), new Vector3(-0.045f, -0.02f, 0.01f), new Vector3(0f, -1f, 0.35f), Vector3.right, sb);
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, ASHPlayerBuilder.PrefabPath);
                sb.Append(changed ? "saved" : "nothing to add");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return sb.ToString();
        }

        /// <summary>Adds gun/LeftHandIK (only if missing): the left wrist pose that puts the left palm centre on gripLocal with the
        /// fingers along fingersLocal and the palm facing palmLocal (gun local). Edit it by hand afterwards; it is never overwritten.</summary>
        static bool AddLeftHandIK(Animator anim, Transform gun, Vector3 gripLocal, Vector3 fingersLocal, Vector3 palmLocal, StringBuilder sb)
        {
            if (!gun) return false;
            if (gun.Find(ASHWeaponAim.LeftHandIKName)) { sb.Append($"{gun.name}/{ASHWeaponAim.LeftHandIKName} kept; "); return false; }
            var hand = Bone(anim, HumanBodyBones.LeftHand); var mid = Bone(anim, HumanBodyBones.LeftMiddleProximal);
            var idx = Bone(anim, HumanBodyBones.LeftIndexProximal); var lit = Bone(anim, HumanBodyBones.LeftLittleProximal);
            if (!hand || !mid || !idx || !lit) { sb.Append($"{gun.name}/{ASHWeaponAim.LeftHandIKName} skip (no left hand bones); "); return false; }
            var A = (mid.position - hand.position).normalized; var T = Vector3.ProjectOnPlane(idx.position - lit.position, A).normalized; var P = -Vector3.Cross(A, T).normalized;
            var pc = Vector3.Lerp(hand.position, mid.position, 0.6f) + P * 0.03f;
            var H = hand.rotation; var aL = Quaternion.Inverse(H) * A; var pL = Quaternion.Inverse(H) * P; var offL = Quaternion.Inverse(H) * (pc - hand.position);
            var aW = gun.TransformDirection(fingersLocal).normalized; var pW = Vector3.ProjectOnPlane(gun.TransformDirection(palmLocal), aW).normalized;
            var Hd = Quaternion.LookRotation(aW, pW) * Quaternion.Inverse(Quaternion.LookRotation(aL, pL));
            var t = new GameObject(ASHWeaponAim.LeftHandIKName).transform; t.SetParent(gun, false);
            t.rotation = Hd; t.position = gun.TransformPoint(gripLocal) - Hd * offL;
            sb.Append($"{gun.name}/{ASHWeaponAim.LeftHandIKName} added at {t.localPosition:F3}; ");
            return true;
        }

        static void Add(GameObject prefab, Transform parent, string name, Pose pose)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(go)) PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            go.name = name;
            go.transform.localPosition = pose.p; go.transform.localRotation = pose.q; go.transform.localScale = Div(pose.s, parent.lossyScale);
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            go.SetActive(false);
        }

        static Vector3 Div(Vector3 a, Vector3 b) => new Vector3(a.x / Mathf.Max(Mathf.Abs(b.x), 1e-5f), a.y / Mathf.Max(Mathf.Abs(b.y), 1e-5f), a.z / Mathf.Max(Mathf.Abs(b.z), 1e-5f));

        /// <summary>Humanoid bone in the prefab: Animator lookup, else the avatar's bone-name mapping.</summary>
        static Transform Bone(Animator anim, HumanBodyBones b)
        {
            Transform t = null;
            try { t = anim.GetBoneTransform(b); } catch { }
            if (t) return t;
            string human = HumanTrait.BoneName[(int)b];
            var map = anim.avatar.humanDescription.human.FirstOrDefault(h => h.humanName == human);
            if (string.IsNullOrEmpty(map.boneName)) return null;
            return anim.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == map.boneName);
        }

        static Dictionary<string, Pose> LoadCapture()
        {
            var d = new Dictionary<string, Pose>();
            var path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/ASH_HeldCapture.txt"));
            if (!File.Exists(path)) return d;
            foreach (var line in File.ReadAllLines(path))
            {
                var f = line.Split('|'); if (f.Length != 5 || line.StartsWith("#")) continue;
                float[] V(string s) => s.Split(',').Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var p = V(f[2]); var q = V(f[3]); var s = V(f[4]);
                if (p.Length != 3 || q.Length != 4 || s.Length != 3) continue;
                d[f[0]] = new Pose { bone = f[1], p = new Vector3(p[0], p[1], p[2]), q = new Quaternion(q[0], q[1], q[2], q[3]).normalized, s = new Vector3(s[0], s[1], s[2]) };
            }
            return d;
        }
    }
}

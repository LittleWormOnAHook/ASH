using System.Collections.Generic;
using System.Linq;
using System.Text;
using ASH.Combat;
using ASH.Gameplay;
using Invector.IK;
using Invector.vShooter;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// ASH/Systems/Combat/3 Fix Ranged Grips. Re-seats the ranged Held_ items on ASH_Player for Invector's shooter animations
    /// (idempotent; backs up ASH_Player_PreGrips first; axe / torch / flashlight untouched):
    ///  - rifle, pistol: the item is moved so the hand bone sits on its authored Grip_R (natural pistol grip, same as Invector's
    ///    weapon handler); Invector's support-hand IK target = the authored Grip_L; muzzle on the bore at the barrel tip.
    ///  - shotgun (Invector model): Invector's right-hand weapon handler mapped from the vBot hand onto the ASH hand (anatomical
    ///    frames: wrist->knuckles, index->little), Invector's leftHandIK converted to the ASH hand bone (Grip_L).
    ///  - wooden bow: left-hand Grip_L placement is kept (not re-seated). The drawing hand is IK'd from the
    ///    string at rest to the authored String_R, pulled in to a cheek anchor by ASHBowDrawRig.anchorIn;
    ///    the held bow is scaled around Grip_L (bowScale) and the nocked arrow is centered on the string plane.
    ///  - ASH_WeaponIKAdjustList: Invector's vBot per-category IK offsets converted to the ASH rig, assigned to the shooter manager
    ///    (made once; later edits in Invector's IK Adjust window are kept).
    ///  - ASH_ArrowProjectile: trail / line renderers removed.
    /// </summary>
    public static class ASHRangedGripFix
    {
        const string Inv = "Assets/Invector-3rdPersonController/";
        const string PlayerPath = "Assets/_ASH/Prefabs/Player/ASH_Player.prefab";
        const string BackupPath = "Assets/_ASH/Prefabs/Player/ASH_Player_PreGrips.prefab";
        const string VBot = Inv + "Shooter/Prefabs/Player/vShooterMelee_NoInventory.prefab";
        const string TplShotgun = Inv + "Shooter/Prefabs/Weapon/_weapons_WITHOUT_Inventory/_weaponsPrefabs/vShotgun_NO_Inventory Variant.prefab";
        const string VBotIKList = Inv + "Shooter/Editor/Resources/Vbot2.0_IKAdjusts/WeaponIKAdjustList@vBot.asset";
        const string IKDir = "Assets/_ASH/Data/Combat/IK";
        const string IKListPath = IKDir + "/ASH_WeaponIKAdjustList.asset";
        const string ArrowOut = "Assets/_ASH/Prefabs/Combat/ASH_ArrowProjectile.prefab";
        const string GripR = ASHHeldRig.GripR, GripL = ASHHeldRig.GripL, StringR = ASHHeldRig.StringR;

        struct Hand { public Transform bone; public Quaternion qA, qF; public Vector3 palmB, hookB; public float lossy; }

        [MenuItem("ASH/Systems/Combat/3 Fix Ranged Grips (Invector baseline)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Grips: stop Play mode first."); return; }
            var sb = new StringBuilder();
            try { Do(sb); }
            catch (System.Exception e) { Debug.LogError("[ASH] Grips failed: " + e); }
            Debug.Log("[ASH] Grips: " + sb);
        }

        [MenuItem("ASH/Systems/Combat/Tune Bow Draw (cheek anchor, scale, centered arrow)")]
        public static void TuneBowDraw()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("[ASH] Bow: stop Play mode first."); return; }
            var sb = new StringBuilder();
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var an = root.GetComponentInChildren<Animator>(true);
                if (!an) { Debug.LogError("[ASH] Bow: no Animator on ASH_Player."); return; }
                var aR = MakeHand(an, true); var aL = MakeHand(an, false);
                var wa = root.GetComponentInChildren<ASHWeaponAim>(true);
                var bow = Find(aL.bone, "Held_WoodenBow");
                if (!bow) { Debug.LogError("[ASH] Bow: Held_WoodenBow missing on ASH_Player."); return; }
                var rig = bow.GetComponent<ASHBowDrawRig>() ?? bow.gameObject.AddComponent<ASHBowDrawRig>();
                rig.bowScale = 0.88f;
                rig.anchorIn = 0.10f;
                rig.anchorOffsetLocal = Vector3.zero;
                rig.nockOffsetLocal = Vector3.zero;
                rig.restOffsetLocal = Vector3.zero;
                Bow(bow, aL, aR, wa, root.transform, sb);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            var item = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/_ASH/Data/Items/ASH_Item_WoodenBow.asset");
            if (item)
            {
                var so = new SerializedObject(item);
                var p = so.FindProperty("heldScale");
                if (p != null && !Mathf.Approximately(p.floatValue, 0.88f))
                {
                    p.floatValue = 0.88f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    sb.Append("ASH_Item_WoodenBow.heldScale=0.88; ");
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[ASH] Bow draw: " + sb);
        }

        static string V(Vector3 v) => $"({v.x:F3},{v.y:F3},{v.z:F3})";

        static void Do(StringBuilder sb)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(BackupPath)) { AssetDatabase.CopyAsset(PlayerPath, BackupPath); sb.Append("backup ASH_Player_PreGrips; "); }

            // ---- Invector vBot reference (read only)
            var vb = PrefabUtility.LoadPrefabContents(VBot);
            Hand iR, iL; Vector3 hPos; Quaternion hRot; bool hOk;
            try
            {
                var an = vb.GetComponentInChildren<Animator>(true);
                iR = MakeHand(an, true); iL = MakeHand(an, false);
                hOk = Handler(vb, an, "rightHandler.defaultHandler", null, true, out hPos, out hRot, sb);
            }
            finally { PrefabUtility.UnloadPrefabContents(vb); }

            // ---- ASH player
            var root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                var an = root.GetComponentInChildren<Animator>(true);
                var aR = MakeHand(an, true); var aL = MakeHand(an, false);
                var mR = aR.qA * Quaternion.Inverse(iR.qA); var mL = aL.qA * Quaternion.Inverse(iL.qA);
                var mRF = aR.qF * Quaternion.Inverse(iR.qF); var mLF = aL.qF * Quaternion.Inverse(iL.qF);
                var wa = root.GetComponentInChildren<ASHWeaponAim>(true);

                foreach (var n in new[] { "Held_HuntingRifle", "Held_Pistol" })
                {
                    var it = Find(aR.bone, n); if (!it) { sb.Append(n + " missing; "); continue; }
                    var g = Child(it, GripR); if (!g) { sb.Append(n + ": no Grip_R, left as is; "); continue; }
                    var before = it.localPosition;
                    PlaceFromGrip(it, g);
                    var w = it.GetComponent<vShooterWeapon>();
                    if (w)
                    {
                        var sup = Child(it, GripL) ?? Child(it, ASHWeaponAim.LeftHandIKName);
                        if (sup) w.handIKTarget = sup;
                        var old = Child(it, "ASH_SupportHandIK"); if (old) Object.DestroyImmediate(old.gameObject);
                        Bore(it, out var bore, out float zTip);
                        var mz = Child(it, "ASH_Muzzle") ?? NewChild(it, "ASH_Muzzle"); mz.localPosition = new Vector3(bore.x, bore.y, zTip + 0.005f); mz.localRotation = Quaternion.identity;
                        var ar = Child(it, "ASH_AimReference") ?? NewChild(it, "ASH_AimReference"); ar.localPosition = mz.localPosition; ar.localRotation = Quaternion.identity;
                        w.muzzle = mz; w.aimReference = ar;
                        sb.Append($"{n}: hand on Grip_R (moved {Vector3.Distance(before, it.localPosition) * aR.lossy:F3} m), support IK {(sup ? sup.name : "none")}, muzzle {V(mz.localPosition)}; ");
                    }
                }

                // shotgun: Invector handler mapped onto the ASH hand
                var sg = Find(aR.bone, "Held_Shotgun");
                if (sg && hOk)
                {
                    foreach (var t in sg.Cast<Transform>().ToList()) if (t.name.StartsWith("collectableStandAlone") || t.name.StartsWith("vActionText")) Object.DestroyImmediate(t.gameObject);
                    var R = mR * hRot;
                    var P = Quaternion.Inverse(hRot) * (iR.palmB - hPos);           // Invector palm in weapon coords (m)
                    var posM = aR.palmB - R * P;                                      // metres along the ASH hand axes
                    sg.localScale = Vector3.one / aR.lossy; sg.localRotation = R; sg.localPosition = posM / aR.lossy;
                    var gr = Child(sg, GripR) ?? NewChild(sg, GripR); gr.localRotation = Quaternion.Inverse(R); gr.localPosition = Quaternion.Inverse(R) * -posM;
                    var w = sg.GetComponent<vShooterWeapon>();
                    var k = Child(sg, "leftHandIK");
                    var tpl = AssetDatabase.LoadAssetAtPath<GameObject>(TplShotgun); var tw = tpl ? tpl.GetComponentInChildren<vShooterWeapon>(true) : null;
                    if (w)
                    {
                        Vector3 kPos; Quaternion kRot; bool kOk = false; kPos = Vector3.zero; kRot = Quaternion.identity;
                        if (tw && tw.handIKTarget) { kPos = tw.transform.InverseTransformPoint(tw.handIKTarget.position); kRot = Quaternion.Inverse(tw.transform.rotation) * tw.handIKTarget.rotation; kOk = true; }
                        else if (k) { kPos = sg.InverseTransformPoint(k.position); kRot = Quaternion.Inverse(sg.rotation) * k.rotation; kOk = true; }
                        if (kOk)
                        {
                            var kRotA = kRot * iL.qA * Quaternion.Inverse(aL.qA);
                            var palm = kPos + kRot * iL.palmB; var kPosA = palm - kRotA * aL.palmB;
                            var gl = Child(sg, GripL) ?? NewChild(sg, GripL); gl.localPosition = kPosA; gl.localRotation = kRotA;
                            w.handIKTarget = gl;
                        }
                        sb.Append($"Held_Shotgun: Invector handler on the ASH hand, support IK {(w.handIKTarget ? w.handIKTarget.name : "none")}; ");
                    }
                }
                else sb.Append(sg ? "Held_Shotgun: vBot handler not found; " : "Held_Shotgun missing; ");

                // bow
                var bow = Find(aL.bone, "Held_WoodenBow");
                if (bow) Bow(bow, aL, aR, wa, root.transform, sb); else sb.Append("Held_WoodenBow missing; ");

                // IK adjust list
                var sm = root.GetComponentInChildren<vShooterManager>(true);
                var list = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(IKListPath);
                if (!list) list = MakeIKList(mR, mL, mRF, mLF, sb); else sb.Append("ASH_WeaponIKAdjustList kept; ");
                if (sm && list) { sm.weaponIKAdjustList = list; EditorUtility.SetDirty(sm); }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            StripArrowTrail(sb);
            AssetDatabase.SaveAssets();
        }

        static void Bow(Transform bow, Hand aL, Hand aR, ASHWeaponAim wa, Transform root, StringBuilder sb)
        {
            var g = Child(bow, GripL); if (!g) { sb.Append("Held_WoodenBow: no Grip_L; "); return; }
            // Do not PlaceFromGrip: the left-hand placement / Grip_L may be hand-tuned. Scale around Grip_L instead.
            var rig = bow.GetComponent<ASHBowDrawRig>() ?? bow.gameObject.AddComponent<ASHBowDrawRig>();
            if (rig.bowScale < 0.5f) rig.bowScale = 0.88f;
            ScaleBowAroundGrip(bow, g, rig.bowScale);

            // geometry in bow space (as ASHWeaponAim reads it): limbs along Z, string on the sign*X side, riser opposite
            var pts = new List<Vector3>();
            foreach (var mf in bow.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.name.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var p in mf.sharedMesh.vertices) pts.Add(bow.InverseTransformPoint(mf.transform.TransformPoint(p)));
            }
            float sign = 1f, stringX = 0.15f, riserX = -0.16f, yMid = 0f;
            if (pts.Count > 20)
            {
                float zMin = pts.Min(p => p.z), zMax = pts.Max(p => p.z), half = (zMax - zMin) * 0.5f, zc = (zMax + zMin) * 0.5f;
                var tips = pts.Where(p => Mathf.Abs(p.z - zc) / half > 0.9f).ToList(); var mid = pts.Where(p => Mathf.Abs(p.z - zc) / half < 0.08f).ToList();
                if (tips.Count > 0 && mid.Count > 0)
                {
                    sign = tips.Average(p => p.x) >= mid.Average(p => p.x) ? 1f : -1f;
                    float smax = pts.Where(p => Mathf.Abs(p.z - zc) / half < 0.6f).Max(p => p.x * sign);
                    stringX = smax * sign;
                    var riser = mid.Where(p => p.x * sign < smax - 0.06f).ToList(); if (riser.Count > 0) { riserX = riser.Average(p => p.x); yMid = riser.Average(p => p.y); }
                }
            }
            var back = new Vector3(sign, 0f, 0f);                 // toward the string / archer
            var up = Vector3.forward;                              // top limb (+Z: the index side of the bow hand on Grip_L)
            var shoot = -back;
            float above = wa ? wa.arrowRestAboveGrip : 0.045f;
            float nockZ = g.localPosition.z + above; // above the grip, stable when the bow is scaled
            // rest and nock share Y (the string plane) so the nocked arrow is centered, not sitting beside the riser
            var rest = new Vector3(riserX, yMid, nockZ);
            var nock = new Vector3(stringX, yMid, nockZ);
            var rot = Quaternion.LookRotation(shoot, up);
            var w = bow.GetComponent<vShooterWeapon>();
            var mz = Child(bow, "ASH_ArrowRest") ?? NewChild(bow, "ASH_ArrowRest");
            var ar = Child(bow, "ASH_AimReference") ?? NewChild(bow, "ASH_AimReference");
            var sR = Child(bow, StringR);
            var hk = Child(bow, "ASH_BowHandIK") ?? NewChild(bow, "ASH_BowHandIK");
            rig.handTarget = hk; rig.nockRestLocal = nock; rig.restLocal = rest; rig.backLocal = back;
            if (sR)
            {
                rig.handFullLocal = bow.InverseTransformPoint(sR.position); rig.handRotLocal = Quaternion.Inverse(bow.rotation) * sR.rotation;
            }
            else { rig.handRotLocal = Quaternion.identity; rig.handFullLocal = nock + back * 0.5f; }
            var hookFull = rig.HandFullEffective + rig.handRotLocal * aR.hookB;
            rig.drawLength = Mathf.Clamp(Vector3.Dot(hookFull - rig.NockRestEffective, back), 0.05f, 1f);
            mz.localPosition = rig.RestEffective; mz.localRotation = rot;
            ar.localPosition = rig.RestEffective; ar.localRotation = rot;
            rig.draw01 = 0f; rig.Apply();
            if (w)
            {
                w.muzzle = mz; w.aimReference = ar; w.handIKTarget = hk; w.useIKOnAiming = true;
                w.freeIKOptions.use = false; w.strafeIKOptions.use = false;   // drawing hand stays free until the bow is raised
            }
            float tipToTip = pts.Count > 20 ? (pts.Max(p => p.z) - pts.Min(p => p.z)) * rig.bowScale : 0f;
            float worldH = Vector3.Distance(bow.TransformPoint(rig.HandFullEffective), g.position);
            var arrowMesh = bow.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.name.IndexOf("arrow", System.StringComparison.OrdinalIgnoreCase) >= 0);
            sb.Append($"Held_WoodenBow: Grip_L kept, scale {rig.bowScale:F2} (tip-to-tip ~{tipToTip:F2} m), nock {V(nock)}, rest {V(rest)} (centered on string plane), draw {rig.drawLength:F2} m, anchorIn {rig.anchorIn:F2} m ({worldH:F2} m grip-to-hand world){(sR ? "" : " (String_R missing)")}{(arrowMesh ? $", model arrow mesh '{arrowMesh.name}' enabled={arrowMesh.enabled}" : "")}; ");
        }

        /// <summary>Sets the bow's world scale, then shifts its local position so Grip_L stays where it was in the left hand (rotation unchanged).</summary>
        static void ScaleBowAroundGrip(Transform bow, Transform grip, float worldScale)
        {
            var keep = bow.localPosition + bow.localRotation * Vector3.Scale(bow.localScale, grip.localPosition);
            bow.localScale = Vector3.one;
            var ls = bow.lossyScale;
            var nl = new Vector3(
                worldScale / Mathf.Max(Mathf.Abs(ls.x), 1e-4f),
                worldScale / Mathf.Max(Mathf.Abs(ls.y), 1e-4f),
                worldScale / Mathf.Max(Mathf.Abs(ls.z), 1e-4f));
            bow.localScale = nl;
            bow.localPosition = keep - bow.localRotation * Vector3.Scale(nl, grip.localPosition);
        }

        static vWeaponIKAdjustList MakeIKList(Quaternion mR, Quaternion mL, Quaternion mRF, Quaternion mLF, StringBuilder sb)
        {
            var src = AssetDatabase.LoadAssetAtPath<vWeaponIKAdjustList>(VBotIKList);
            if (!src) { sb.Append("vBot IK list missing; "); return null; }
            if (!AssetDatabase.IsValidFolder("Assets/_ASH/Data/Combat")) AssetDatabase.CreateFolder("Assets/_ASH/Data", "Combat");
            if (!AssetDatabase.IsValidFolder(IKDir)) AssetDatabase.CreateFolder("Assets/_ASH/Data/Combat", "IK");
            var list = ScriptableObject.CreateInstance<vWeaponIKAdjustList>();
            list.ikTargetPositionOffsetR = mR * src.ikTargetPositionOffsetR; list.ikTargetRotationOffsetR = Conj(mR, src.ikTargetRotationOffsetR);
            list.ikTargetPositionOffsetL = mL * src.ikTargetPositionOffsetL; list.ikTargetRotationOffsetL = Conj(mL, src.ikTargetRotationOffsetL);
            list.weaponIKAdjusts = new List<vWeaponIKAdjust>();
            AssetDatabase.CreateAsset(list, IKListPath);
            foreach (var a in src.weaponIKAdjusts)
            {
                if (!a) continue;
                var c = ScriptableObject.CreateInstance<vWeaponIKAdjust>();
                c.name = "ASH_IK_" + a.name.Split('@')[0];
                c.weaponCategories = new List<string>(a.weaponCategories);
                c.ikAdjustsRight = a.ikAdjustsRight.Select(x => Convert(x, mR, mRF, mL, mLF)).ToList();
                c.ikAdjustsLeft = a.ikAdjustsLeft.Select(x => Convert(x, mL, mLF, mR, mRF)).ToList();
                AssetDatabase.CreateAsset(c, IKDir + "/" + c.name + ".asset");
                list.weaponIKAdjusts.Add(c);
            }
            EditorUtility.SetDirty(list);
            sb.Append($"ASH_WeaponIKAdjustList made from vBot ({list.weaponIKAdjusts.Count} categories, offsets converted to the ASH rig); ");
            return list;
        }

        static IKAdjust Convert(IKAdjust a, Quaternion mW, Quaternion mWH, Quaternion mS, Quaternion mSH)
        {
            var c = new IKAdjust(a.name);
            c.weaponHandOffset = Off(a.weaponHandOffset, mW); c.weaponHintOffset = Off(a.weaponHintOffset, mWH);
            c.supportHandOffset = Off(a.supportHandOffset, mS); c.supportHintOffset = Off(a.supportHintOffset, mSH);
            c.spineOffset = new IKOffsetSpine { spine = a.spineOffset != null ? a.spineOffset.spine : Vector2.zero, head = a.spineOffset != null ? a.spineOffset.head : Vector2.zero };
            return c;
        }
        static IKOffsetTransform Off(IKOffsetTransform o, Quaternion m) => o == null ? new IKOffsetTransform() : new IKOffsetTransform { position = m * o.position, eulerAngles = Conj(m, o.eulerAngles) };
        static Vector3 Conj(Quaternion m, Vector3 euler) => (m * Quaternion.Euler(euler) * Quaternion.Inverse(m)).eulerAngles;

        // Bow model ships with a baked arrow mesh; hide it so an arrow only shows while drawing (nock arrow is runtime).
        public static void HideBowModelArrow()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPath); int n = 0;
            try
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name != "Held_WoodenBow") continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true))
                        if (r.name.IndexOf("Arrow", System.StringComparison.OrdinalIgnoreCase) >= 0 && r.enabled) { r.enabled = false; n++; }
                }
                if (n > 0) PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            Debug.Log("[ASH] Grips: bow model arrow renderers hidden " + n);
        }

        static void StripArrowTrail(StringBuilder sb)
        {
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(ArrowOut)) return;
            var a = PrefabUtility.LoadPrefabContents(ArrowOut);
            try
            {
                int n = 0;
                var pc = a.GetComponent<Invector.vShooter.vProjectileControl>(); if (pc && pc.trail) { pc.trail = null; n++; }
                foreach (var t in a.GetComponentsInChildren<TrailRenderer>(true)) { Object.DestroyImmediate(t); n++; }
                foreach (var l in a.GetComponentsInChildren<LineRenderer>(true)) { Object.DestroyImmediate(l); n++; }
                PrefabUtility.SaveAsPrefabAsset(a, ArrowOut);
                sb.Append($"arrow projectile trails removed ({n}); ");
            }
            finally { PrefabUtility.UnloadPrefabContents(a); }
        }

        // ---------- helpers
        static Hand MakeHand(Animator an, bool right)
        {
            var h = new Hand();
            h.bone = an.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            var fore = an.GetBoneTransform(right ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var mid = an.GetBoneTransform(right ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal);
            var idx = an.GetBoneTransform(right ? HumanBodyBones.RightIndexProximal : HumanBodyBones.LeftIndexProximal);
            var lit = an.GetBoneTransform(right ? HumanBodyBones.RightLittleProximal : HumanBodyBones.LeftLittleProximal);
            var fW = mid.position - h.bone.position; var sW = idx.position - lit.position;
            h.qA = Frame(h.bone, fW, sW); h.qF = Frame(fore, h.bone.position - fore.position, sW);
            h.palmB = Quaternion.Inverse(h.bone.rotation) * (fW * 0.5f);
            h.hookB = Quaternion.Inverse(h.bone.rotation) * (fW * 1.15f);
            h.lossy = h.bone.lossyScale.x;
            return h;
        }
        /// <summary>Rotation from the anatomical frame (x = wrist->knuckles, y = little->index, z = x cross y) to the bone's axes.</summary>
        static Quaternion Frame(Transform bone, Vector3 fWorld, Vector3 sWorld)
        {
            var q = Quaternion.Inverse(bone.rotation);
            var f = (q * fWorld).normalized; var s = q * sWorld; s = (s - f * Vector3.Dot(s, f)).normalized; var n = Vector3.Cross(f, s);
            return Quaternion.LookRotation(n, s);
        }

        /// <summary>Invector weapon handler pose relative to the vBot hand bone (metres / rotation in the bone's axes), via its
        /// BodySnaps node, which Invector snaps onto the bone at runtime (vBodySnappingControl orientation).</summary>
        static bool Handler(GameObject vb, Animator an, string prop, string custom, bool right, out Vector3 pos, out Quaternion rot, StringBuilder sb)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var col = vb.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m && m.GetType().Name == "vCollectShooterMeleeControl");
            if (!col) return false;
            var h = new SerializedObject(col).FindProperty(prop)?.objectReferenceValue as Transform; if (!h) return false;
            var bone = an.GetBoneTransform(right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            Transform snap = h; while (snap.parent && snap.parent.name != "BodySnaps") snap = snap.parent;
            Quaternion snapRel = Quaternion.identity; bool viaSnap = false;
            if (snap.parent)
            {
                var bsc = vb.GetComponentsInChildren<MonoBehaviour>(true).FirstOrDefault(m => m && m.GetType().Name == "vBodySnappingControl");
                if (bsc)
                {
                    var it = new SerializedObject(bsc).GetIterator();
                    while (it.Next(true))
                    {
                        if (!it.propertyPath.EndsWith(".target") || it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue != snap) continue;
                        var o = it.serializedObject.FindProperty(it.propertyPath.Substring(0, it.propertyPath.Length - 7) + ".orientation");
                        int e = o != null ? o.enumValueIndex : 0;
                        var look = e == 1 ? Vector3.back : e == 2 ? Vector3.right : e == 3 ? Vector3.left : e == 4 ? Vector3.up : e == 5 ? Vector3.down : Vector3.forward;
                        snapRel = look == Vector3.up || look == Vector3.down ? Quaternion.LookRotation(look, Vector3.forward) : Quaternion.LookRotation(look, Vector3.up);
                        viaSnap = true; break;
                    }
                }
            }
            if (viaSnap)
            {
                pos = snapRel * (Quaternion.Inverse(snap.rotation) * (h.position - snap.position));
                rot = snapRel * (Quaternion.Inverse(snap.rotation) * h.rotation);
            }
            else
            {
                pos = Quaternion.Inverse(bone.rotation) * (h.position - bone.position);
                rot = Quaternion.Inverse(bone.rotation) * h.rotation;
            }
            var direct = Quaternion.Inverse(bone.rotation) * (h.position - bone.position);
            sb.Append($"vBot handler {h.name} via {(viaSnap ? "BodySnaps " + snap.name : "bone")} pos {V(pos)} (edit-pose bone offset {V(direct)}); ");
            return true;
        }

        static void PlaceFromGrip(Transform item, Transform grip)
        {
            var gRot = Quaternion.Inverse(item.rotation) * grip.rotation;   // hand bone rotation in item space
            var gPos = item.InverseTransformPoint(grip.position);           // hand bone position in item space
            var r = Quaternion.Inverse(gRot);
            item.localRotation = r;
            item.localPosition = -(r * Vector3.Scale(item.localScale, gPos));
        }

        static void Bore(Transform it, out Vector3 bore, out float zTip)
        {
            var pts = new List<Vector3>();
            foreach (var mf in it.GetComponentsInChildren<MeshFilter>(true)) { if (!mf.sharedMesh) continue; foreach (var p in mf.sharedMesh.vertices) pts.Add(it.InverseTransformPoint(mf.transform.TransformPoint(p))); }
            if (pts.Count == 0) { bore = Vector3.zero; zTip = 0.5f; return; }
            zTip = pts.Max(p => p.z); float zt = zTip;
            var tip = pts.Where(p => p.z > zt - 0.012f).ToList();
            bore = new Vector3(tip.Average(p => p.x), tip.Average(p => p.y), zTip);
        }

        static Transform Find(Transform hand, string n) { foreach (Transform c in hand) if (c.name == n) return c; return null; }
        static Transform Child(Transform t, string n) => ASHHeldRig.Child(t, n);
        static Transform NewChild(Transform p, string n) { var t = new GameObject(n).transform; t.SetParent(p, false); return t; }
    }
}

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools.Build
{
    /// <summary>Samples chop / bow clips on the ASH player rig and logs hand paths: impact time (sharpest stop after the
    /// fastest point of the right-hand swing), hand height there, and for bow clips the yaw of the arrow line vs root forward.
    /// Logs "[ASH] CLIPPROBE ...". Read-only.</summary>
    public static class ASHChopClipProbe
    {
        [MenuItem("ASH/Debug/Probe Action Clips")]
        public static void Run()
        {
            var guid = AssetDatabase.FindAssets("ASH_Player t:Prefab").FirstOrDefault();
            var prefab = guid != null ? AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid)) : null;
            var srcAnim = prefab ? prefab.GetComponentInChildren<Animator>(true) : null;
            if (!srcAnim || !srcAnim.avatar) { Debug.Log("[ASH] CLIPPROBE no player animator"); return; }
            var go = Object.Instantiate(srcAnim.gameObject); go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var an = go.GetComponent<Animator>(); an.applyRootMotion = false;
                var root = "Assets/_ASH/Animation/Imported/";
                var files = new[] { "Villager@Axe-Chopping01.fbx", "Chop/Villager@Axe-Chopping02.fbx", "Chop/Malbers_Axe2H_SwingRight.fbx", "Bow/Mixamo_BowAimIdle.fbx", "Bow/Mixamo_BowDraw.fbx" };
                foreach (var f in files)
                {
                    var clips = AssetDatabase.LoadAllAssetsAtPath(root + f).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToList();
                    if (clips.Count == 0) { Debug.Log($"[ASH] CLIPPROBE {f} NO CLIP"); continue; }
                    foreach (var c in clips) Probe(an, f, c);
                }
            }
            finally { Object.DestroyImmediate(go); }
        }

        static void Probe(Animator an, string f, AnimationClip c)
        {
            var rh = an.GetBoneTransform(HumanBodyBones.RightHand); var lh = an.GetBoneTransform(HumanBodyBones.LeftHand);
            var hips = an.GetBoneTransform(HumanBodyBones.Hips); var head = an.GetBoneTransform(HumanBodyBones.Head);
            int n = 100; var pos = new List<Vector3>(); var lpos = new List<Vector3>(); var arrowYaw = new List<float>(); var hipYaw = new List<float>();
            for (int i = 0; i <= n; i++)
            {
                c.SampleAnimation(an.gameObject, c.length * i / n);
                pos.Add(rh.position); lpos.Add(lh.position);
                var a = lh.position - rh.position; a.y = 0f; arrowYaw.Add(Vector3.SignedAngle(Vector3.forward, a, Vector3.up));
                hipYaw.Add(Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(hips.forward, Vector3.up), Vector3.up));
            }
            var sp = new float[n + 1]; for (int i = 1; i <= n; i++) sp[i] = (pos[i] - pos[i - 1]).magnitude * n / Mathf.Max(0.01f, c.length);
            int peak = 1; for (int i = 1; i <= n; i++) if (sp[i] > sp[peak]) peak = i;
            int imp = peak; float best = 0f; for (int i = peak + 1; i <= Mathf.Min(n, peak + 25); i++) { float d = sp[i - 1] - sp[i]; if (d > best) { best = d; imp = i; } }
            int low = 0; for (int i = 0; i <= n; i++) if (pos[i].y < pos[low].y) low = i;
            var tb = new System.Text.StringBuilder();
            for (int i = 0; i <= n; i += 4)
            {
                c.SampleAnimation(an.gameObject, c.length * i / n);
                var fy = Quaternion.Euler(0f, hips.eulerAngles.y, 0f); var rel = Quaternion.Inverse(fy) * (rh.position - hips.position);
                var axeDir = Quaternion.Inverse(fy) * (rh.position - an.GetBoneTransform(HumanBodyBones.RightLowerArm).position).normalized;
                tb.Append($" {i / (float)n:F2}:y{rel.y:F2}/f{rel.z:F2}/x{rel.x:F2}/d{axeDir.y:F1}");
            }
            Debug.Log($"[ASH] CLIPPROBE TABLE {f} hand vs hips:{tb}");
            Debug.Log($"[ASH] CLIPPROBE {f} '{c.name}' len={c.length:F2}s human={c.humanMotion} loop={c.isLooping} peakSpeed={sp[peak]:F1}@{peak / (float)n:F2} impact@{imp / (float)n:F2} hand y={pos[imp].y:F2} z={pos[imp].z:F2} x={pos[imp].x:F2} hands {(pos[imp] - lpos[imp]).magnitude:F2}m lowest@{low / (float)n:F2} y={pos[low].y:F2} | arrowYaw 0/50/100 {arrowYaw[0]:F0}/{arrowYaw[50]:F0}/{arrowYaw[100]:F0} hipYaw {hipYaw[0]:F0}/{hipYaw[50]:F0}/{hipYaw[100]:F0} headY={head.position.y:F2}");
        }
    }
}

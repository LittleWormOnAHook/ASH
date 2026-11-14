using System.Globalization;
using System.IO;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// Offscreen screenshot helper for the automation bridge.
    /// Arg: "path" (renders Camera.main) or "path|x,y,z|pitch,yaw[,fov]" (temporary camera at that pose).
    /// </summary>
    internal static class ASHBridgeScreenshot
    {
        public static string Capture(string arg)
        {
            string[] p = arg.Split('|');
            string path = p[0];
            Camera source = Camera.main;
            GameObject temp = null;
            Camera cam = source;
            if (p.Length >= 3)
            {
                temp = new GameObject("ASH_BridgeShotCam") { hideFlags = HideFlags.HideAndDontSave };
                cam = temp.AddComponent<Camera>();
                if (source != null) cam.CopyFrom(source);
                float[] pos = Parse(p[1]);
                float[] rot = Parse(p[2]);
                cam.transform.SetPositionAndRotation(new Vector3(pos[0], pos[1], pos[2]), Quaternion.Euler(rot[0], rot[1], 0f));
                if (rot.Length > 2) cam.fieldOfView = rot[2];
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, 6000f);
            }
            if (cam == null) return "error|no camera";

            const int w = 1280, h = 720;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;
            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                Object.DestroyImmediate(rt);
                if (temp != null) Object.DestroyImmediate(temp);
            }
            return "ok|" + path;
        }

        private static float[] Parse(string csv)
        {
            string[] s = csv.Split(',');
            var f = new float[s.Length];
            for (int i = 0; i < s.Length; i++) f[i] = float.Parse(s[i], CultureInfo.InvariantCulture);
            return f;
        }
    }
}

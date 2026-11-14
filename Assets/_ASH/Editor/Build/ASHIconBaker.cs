using ASH.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASH.EditorTools.Build
{
    /// <summary>
    /// Renders each item's world prefab into a transparent 256px sprite (preview scene, 3/4 ortho view,
    /// black/white difference matting for clean anti-aliased alpha) and assigns it as the item icon.
    /// </summary>
    public static class ASHIconBaker
    {
        public const string IconDir = ASHContentBuilder.Root + "/Art/Icons";

        static readonly (string item, string prefab, float yaw)[] Map =
        {
            ("ASH_Item_Deadwood", "ASH_Deadwood", 20f),
            ("ASH_Item_Stone", "ASH_StonePile", -30f),
            ("ASH_Item_Chokecherries", "ASH_ChokecherryBush", 15f),
            ("ASH_Item_Campfire", "ASH_Campfire", 0f),
        };

        [MenuItem("ASH/Build/5. Bake Item Icons")]
        public static void BakeAll()
        {
            ASHBuildUtil.Folder(IconDir);
            int n = 0;
            foreach (var (itemFile, prefabName, yaw) in Map)
            {
                var item = ASHContentBuilder.LoadItem(itemFile);
                var prefab = ASHContentBuilder.LoadPrefab(prefabName);
                if (!item || !prefab) { Debug.LogWarning($"[ASH] Icon bake skipped {itemFile}: item={(bool)item} prefab={(bool)prefab}"); continue; }
                string path = $"{IconDir}/{itemFile.Replace("ASH_Item_", "ASH_Icon_")}.png";
                if (!Bake(prefab, path, 256, yaw)) { Debug.LogWarning("[ASH] Icon bake failed for " + itemFile); continue; }
                item.icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                EditorUtility.SetDirty(item);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ASH] Baked {n} item icons into {IconDir}");
        }

        static bool Bake(GameObject prefab, string path, int size, float yaw)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null;
            try
            {
                var go = prefab.name == "ASH_ChokecherryBush" ? BerryCluster(prefab, scene) : (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0));
                foreach (var pr in go.GetComponentsInChildren<ParticleSystemRenderer>(true)) pr.enabled = false;
                foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
                bool has = false; Bounds b = default;
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r is ParticleSystemRenderer) continue;
                    if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
                }
                if (!has) return false;

                Light(scene, "Key", new Color(1f, 0.93f, 0.82f), 2.2f, Quaternion.Euler(42, -40, 0));
                Light(scene, "Fill", new Color(0.62f, 0.7f, 0.82f), 0.9f, Quaternion.Euler(18, 145, 0));
                Light(scene, "Front", new Color(0.9f, 0.86f, 0.8f), 0.9f, Quaternion.Euler(10, 20, 0));

                var camGo = new GameObject("IconCam");
                SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene; cam.orthographic = true; cam.allowHDR = false; cam.allowMSAA = true;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.nearClipPlane = 0.01f;
                Vector3 dir = new Vector3(-0.85f, 0.8f, -1f).normalized;
                float radius = b.extents.magnitude;
                cam.transform.position = b.center + dir * (radius * 4f + 1f);
                cam.transform.LookAt(b.center);
                cam.farClipPlane = radius * 10f + 10f;
                // fit ortho size to the projected bounds
                float ext = 0.01f;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var lp = cam.transform.InverseTransformPoint(c);
                    ext = Mathf.Max(ext, Mathf.Abs(lp.x), Mathf.Abs(lp.y));
                }
                cam.orthographicSize = ext * 1.08f;

                rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
                cam.targetTexture = rt;
                var black = Grab(cam, rt, Color.black, size);
                var white = Grab(cam, rt, Color.white, size);
                var outTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var bp = black.GetPixels(); var wp = white.GetPixels(); var o = new Color[bp.Length];
                for (int i = 0; i < bp.Length; i++)
                {
                    float a = 1f - Mathf.Clamp01(((wp[i].r - bp[i].r) + (wp[i].g - bp[i].g) + (wp[i].b - bp[i].b)) / 3f);
                    o[i] = a > 0.004f ? new Color(Mathf.Clamp01(bp[i].r / a), Mathf.Clamp01(bp[i].g / a), Mathf.Clamp01(bp[i].b / a), a) : Color.clear;
                }
                // gentle auto-exposure so dark materials (weathered wood, basalt) still read on a dark slot
                double lum = 0; int cnt = 0;
                foreach (var c in o) if (c.a > 0.5f) { lum += 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b; cnt++; }
                float mean = cnt > 0 ? (float)(lum / cnt) : 0.5f;
                float gain = Mathf.Clamp(0.42f / Mathf.Max(mean, 0.01f), 1f, 2.8f);
                if (gain > 1.01f) for (int i = 0; i < o.Length; i++) { var c = o[i]; o[i] = new Color(Mathf.Clamp01(c.r * gain), Mathf.Clamp01(c.g * gain), Mathf.Clamp01(c.b * gain), c.a); }
                outTex.SetPixels(o); outTex.Apply();
                System.IO.File.WriteAllBytes(path, outTex.EncodeToPNG());
                Object.DestroyImmediate(black); Object.DestroyImmediate(white); Object.DestroyImmediate(outTex);
                cam.targetTexture = null;

                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var imp = (TextureImporter)AssetImporter.GetAtPath(path);
                imp.textureType = TextureImporterType.Sprite; imp.spriteImportMode = SpriteImportMode.Single;
                imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.maxTextureSize = 256;
                imp.textureCompression = TextureImporterCompression.Uncompressed;
                imp.SaveAndReimport();
                return true;
            }
            finally
            {
                if (rt) { rt.Release(); Object.DestroyImmediate(rt); }
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>Close-up berry cluster for the chokecherry icon, using the bush prefab's own materials.</summary>
        static GameObject BerryCluster(GameObject bush, Scene scene)
        {
            Material berry = null, leaf = null, bark = null;
            foreach (var r in bush.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("Cluster")) berry = r.sharedMaterial;
                else if (r.name.StartsWith("Leaves")) leaf = r.sharedMaterial;
                else if (r.name == "Stem") bark = r.sharedMaterial;
            }
            var root = new GameObject("BerryIcon"); SceneManager.MoveGameObjectToScene(root, scene);
            GameObject P(PrimitiveType type, Vector3 pos, Vector3 euler, Vector3 scale, Material m)
            {
                var g = GameObject.CreatePrimitive(type); Object.DestroyImmediate(g.GetComponent<Collider>());
                g.transform.SetParent(root.transform, false); g.transform.localPosition = pos; g.transform.localEulerAngles = euler; g.transform.localScale = scale;
                if (m) g.GetComponent<MeshRenderer>().sharedMaterial = m;
                return g;
            }
            P(PrimitiveType.Cylinder, new Vector3(0, 0.42f, 0), new Vector3(0, 0, 8), new Vector3(0.025f, 0.2f, 0.025f), bark);
            P(PrimitiveType.Sphere, new Vector3(-0.17f, 0.5f, 0.02f), new Vector3(0, 0, 35), new Vector3(0.26f, 0.05f, 0.12f), leaf);
            P(PrimitiveType.Sphere, new Vector3(0.16f, 0.55f, -0.03f), new Vector3(0, 0, -30), new Vector3(0.24f, 0.05f, 0.11f), leaf);
            var rnd = new System.Random(7);
            for (int i = 0; i < 14; i++)
            {
                float y = 0.36f - i * 0.022f; float a = i * 2.4f; float r = 0.035f + 0.045f * Mathf.Sin(i * 0.45f);
                P(PrimitiveType.Sphere, new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r), Vector3.zero, Vector3.one * (0.065f + 0.012f * (float)rnd.NextDouble()), berry);
            }
            return root;
        }

        static Texture2D Grab(Camera cam, RenderTexture rt, Color bg, int size)
        {
            cam.backgroundColor = bg;
            cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            t.ReadPixels(new Rect(0, 0, size, size), 0, 0); t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        static void Light(Scene scene, string name, Color c, float intensity, Quaternion rot)
        {
            var go = new GameObject(name); SceneManager.MoveGameObjectToScene(go, scene);
            var l = go.AddComponent<Light>(); l.type = LightType.Directional; l.color = c; l.intensity = intensity; l.shadows = LightShadows.None;
            go.transform.rotation = rot;
        }
    }
}

#if UNITY_EDITOR
using System.Linq;
using ASH.EditorTools.Systems;
using ASH.UI;
using UnityEditor;
using UnityEngine;

namespace ASH.EditorTools
{
    /// <summary>
    /// B1 3D journal book setup (idempotent; saves only its own assets; never touches the EndlessBook vendor folder, the scene or import settings):
    /// Resources/ASH_JournalBookSettings, URP material copies in Assets/_ASH/Materials/Journal (cover, page-block edges, page paper),
    /// and references to the vendor Book.prefab and Demo 02 sounds (by GUID; missing in clones without the pack = flat journal).
    /// </summary>
    public static class ASHJournalBookSetup
    {
        const string SettingsPath = "Assets/_ASH/Resources/ASH_JournalBookSettings.asset";
        const string MatDir = "Assets/_ASH/Materials/Journal";
        const string VendorDir = "Assets/EndlessBook";

        [MenuItem("ASH/Systems/Journal/Setup Journal Book (idempotent)", false, 251)]
        public static void Setup()
        {
            ASHJournalSetup.Setup(); // tooltip + art first (the book uses the journal art)
            var art = AssetDatabase.LoadAssetAtPath<ASHJournalArt>(ASHJournalSetup.ArtPath);
            var S = ASHMenuInstaller.Ensure<ASHJournalBookSettings>(SettingsPath);
            ASHMenuInstaller.Folder(MatDir);

            if (!S.bookPrefab) S.bookPrefab = FindVendorPrefab();
            if (S.cameraOffset == new Vector3(0f, 3.4f, -1.25f)) S.cameraOffset = new Vector3(0f, 3.4f, -0.3f); // B2: near top-down so the live journal lines up with the pages
            if (!S.openClip) S.openClip = Clip("BookOpen");
            if (!S.closeClip) S.closeClip = Clip("BookClose");
            if (!S.pageTurnClip) S.pageTurnClip = Clip("PageTurn");
            if (!S.pagesFlippingClip) S.pagesFlippingClip = Clip("PagesFlipping");

            var lit = Shader.Find("Universal Render Pipeline/Lit"); var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (!lit || !unlit) { Debug.LogError("[ASH Journal] URP shaders not found."); return; }
            Texture cover = art ? art.journalCover.Tex : null, paper = art ? art.journalPaper.Tex : null;
            Texture edge = VendorTex("BookPageBack");

            S.coverMaterial = Mat(S.coverMaterial, "ASH_Book_Cover", lit, cover, new Color(0.86f, 0.78f, 0.7f, 1f), 0.22f);
            S.pageEdgeFrontMaterial = Mat(S.pageEdgeFrontMaterial, "ASH_Book_PageEdgeFront", lit, edge, new Color(0.91f, 0.76f, 0.55f, 1f), 0.05f);
            S.pageEdgeBackMaterial = Mat(S.pageEdgeBackMaterial, "ASH_Book_PageEdgeBack", lit, edge, new Color(0.91f, 0.76f, 0.55f, 1f), 0.05f);
            S.pageMaterial = Mat(S.pageMaterial, "ASH_Book_Page", unlit, paper, Color.white, 0f);
            if (art)
            {
                if (!S.pagePaperLeft) S.pagePaperLeft = art.pageBlankLeft.Tex;
                if (!S.pagePaperRight) S.pagePaperRight = art.pageBlankRight.Tex;
            }
            EditorUtility.SetDirty(S);
            AssetDatabase.SaveAssetIfDirty(S);
            foreach (var m in new[] { S.coverMaterial, S.pageEdgeFrontMaterial, S.pageEdgeBackMaterial, S.pageMaterial }) if (m) AssetDatabase.SaveAssetIfDirty(m);
            Debug.Log($"[ASH Journal] Book settings ready. Prefab {(S.bookPrefab ? AssetDatabase.GetAssetPath(S.bookPrefab) : "MISSING (flat journal)")}, " +
                      $"clips {(S.openClip ? 1 : 0) + (S.closeClip ? 1 : 0) + (S.pageTurnClip ? 1 : 0) + (S.pagesFlippingClip ? 1 : 0)}/4, materials in {MatDir}.");
        }

        static GameObject FindVendorPrefab()
        {
            if (!AssetDatabase.IsValidFolder(VendorDir)) return null;
            var direct = AssetDatabase.LoadAssetAtPath<GameObject>(VendorDir + "/Book/Prefabs/Book.prefab");
            if (direct && HasBook(direct)) return direct;
            foreach (var g in AssetDatabase.FindAssets("t:Prefab", new[] { VendorDir }))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(g));
                if (go && go.name == "Book" && HasBook(go)) return go;
            }
            return null;
        }

        static bool HasBook(GameObject go) => go.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c && c.GetType().FullName == "echo17.EndlessBook.EndlessBook");

        static AudioClip Clip(string name)
        {
            if (!AssetDatabase.IsValidFolder(VendorDir)) return null;
            return AssetDatabase.FindAssets(name + " t:AudioClip", new[] { VendorDir }).Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == name).Select(p => AssetDatabase.LoadAssetAtPath<AudioClip>(p)).FirstOrDefault(c => c);
        }

        static Texture VendorTex(string name)
        {
            if (!AssetDatabase.IsValidFolder(VendorDir)) return null;
            return AssetDatabase.FindAssets(name + " t:Texture2D", new[] { VendorDir }).Select(g => AssetDatabase.GUIDToAssetPath(g))
                .Where(p => System.IO.Path.GetFileNameWithoutExtension(p) == name).Select(p => AssetDatabase.LoadAssetAtPath<Texture>(p)).FirstOrDefault(t => t);
        }

        /// <summary>Create the material once; later runs only fill a missing texture (your tweaks stay).</summary>
        static Material Mat(Material existing, string name, Shader shader, Texture tex, Color col, float smooth)
        {
            string path = MatDir + "/" + name + ".mat";
            var m = existing ? existing : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m)
            {
                m = new Material(shader) { name = name };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", col);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
                AssetDatabase.CreateAsset(m, path);
            }
            if (tex && m.HasProperty("_BaseMap") && !m.GetTexture("_BaseMap")) { m.SetTexture("_BaseMap", tex); EditorUtility.SetDirty(m); }
            return m;
        }
    }
}
#endif

using System.IO;
using UnityEditor;
using UnityEngine;

// Kaziyicilar: kasik (0) ve spatula (2) eski FBX prefablari; kepce (1), kucuk kurek (3), orta kurek (4) ve
// buyuk kurek (5) Blender'da normallestirilen glb modellerinden kurulur.
// Model kurali (kasikla ayni): orijin = sinir kutusu merkezi, +Z = calisan uc, +Y = kepcenin acik yuzu, metre.
public static class ScraperSetup
{
    const string ModelDir = "Assets/_Project/Art/Models/Scrapers/";
    const string PrefabDir = "Assets/_Project/Prefabs/Scrapers/";

    static readonly string[] GlbByTier = { null, "Scraper1_Ladle", null, "Scraper3_SmallShovel", "Scraper4_MidShovel", "Scraper5_BigShovel" };

    // [0..5]: kasik, kepce, spatula, kucuk kurek, orta kurek, buyuk kurek
    public static GameObject[] BuildToolList(GameObject spoon, GameObject spatula)
    {
        AssetDatabase.Refresh();
        if (AssetDatabase.IsValidFolder(PrefabDir + "Materials")) { AssetDatabase.DeleteAsset(PrefabDir + "Materials"); AssetDatabase.Refresh(); }
        Directory.CreateDirectory(PrefabDir + "Materials");
        AssetDatabase.Refresh();
        var res = new GameObject[GlbByTier.Length];
        res[0] = spoon;
        res[2] = spatula;
        for (int t = 0; t < GlbByTier.Length; t++)
        {
            if (GlbByTier[t] == null) continue;
            string glb = ModelDir + GlbByTier[t] + ".glb";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            if (asset == null) { Debug.LogWarning("[Scraper] model yuklenemedi: " + glb); continue; }
            var root = new GameObject($"Scraper{t}");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    var copy = new Material(src);
                    copy.name = $"S{t}_{string.Concat(src.name.Split(Path.GetInvalidFileNameChars()))}_{i}";
                    // tam metal glb malzemeleri yansima olmadan camgobegi/beyaz gorunur: metali kis
                    if (copy.HasProperty("metallicFactor")) copy.SetFloat("metallicFactor", copy.GetFloat("metallicFactor") * 0.35f);
                    AssetDatabase.CreateAsset(copy, $"{PrefabDir}Materials/{copy.name}.mat");
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }
            var b = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            Debug.Log($"[Scraper] {t} {GlbByTier[t]}: merkez={b.center:F3} boyut={b.size:F3}");
            res[t] = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabDir}Scraper{t}.prefab");
            Object.DestroyImmediate(root);
        }
        AssetDatabase.SaveAssets();
        // kaydedilen prefablar malzeme referanslarini ancak yeniden ice aktarilinca cozer
        for (int t = 0; t < GlbByTier.Length; t++)
            if (GlbByTier[t] != null) AssetDatabase.ImportAsset($"{PrefabDir}Scraper{t}.prefab", ImportAssetOptions.ForceUpdate);
        for (int t = 0; t < GlbByTier.Length; t++)
            if (GlbByTier[t] != null) res[t] = AssetDatabase.LoadAssetAtPath<GameObject>($"{PrefabDir}Scraper{t}.prefab");
        return res;
    }

    // Sahneyi yeniden kurmadan: prefablari kur ve sahnedeki oyuncuya bagla
    [MenuItem("VolcanoSweepers/Rebuild Scraper Prefabs")]
    public static void RebuildMenu()
    {
        var tools = Object.FindFirstObjectByType<PlayerTools>();
        GameObject spoon = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Spoon.prefab");
        GameObject spatula = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Spatula.prefab");
        var list = BuildToolList(spoon, spatula);
        if (tools != null)
        {
            tools.toolPrefabs = list;
            tools.dustMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/TorchSmoke.mat");
            EditorUtility.SetDirty(tools);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tools.gameObject.scene);
        }
        Debug.Log("[Scraper] kaziyici prefablari yenilendi: " + list.Length);
    }
}

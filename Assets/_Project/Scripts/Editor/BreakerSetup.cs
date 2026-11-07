using System.IO;
using UnityEditor;
using UnityEngine;

// 20 kirici (cekic, balyoz, matkap, kirici): glb modellerinden el prefablarini kurar.
// Modeller Blender'da normallestirildi: +Z = calisan uc / vurus yonu, +Y = yukari, orijin = tutus noktasi, metre olcegi.
public static class BreakerSetup
{
    const string ModelDir = "Assets/_Project/Art/Models/Breakers/";
    const string PrefabDir = "Assets/_Project/Prefabs/Breakers/";

    // index 0 bos; [1..20]
    public static GameObject[] BuildPrefabs()
    {
        AssetDatabase.Refresh();
        Directory.CreateDirectory(PrefabDir);
        var res = new GameObject[Economy.BreakerCount + 1];
        for (int t = 1; t <= Economy.BreakerCount; t++)
        {
            string[] found = Directory.GetFiles(ModelDir, $"Breaker{t:00}_*.glb");
            if (found.Length == 0) { Debug.LogWarning($"[Breaker] model yok: kademe {t}"); continue; }
            string glb = found[0].Replace('\\', '/');
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            if (asset == null) { Debug.LogWarning($"[Breaker] model yuklenemedi: {glb}"); continue; }

            var root = new GameObject($"Breaker{t:00}");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
            PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var l in model.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
            foreach (var cam in model.GetComponentsInChildren<Camera>(true)) Object.DestroyImmediate(cam);
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var b = new Bounds(); bool first = true;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true)) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
            Debug.Log($"[Breaker] {t:00} {Path.GetFileNameWithoutExtension(glb)}: sinir merkez={b.center:F3} boyut={b.size:F3}");

            string pp = $"{PrefabDir}Breaker{t:00}.prefab";
            res[t] = PrefabUtility.SaveAsPrefabAsset(root, pp);
            Object.DestroyImmediate(root);
        }
        return res;
    }

    // Sahneyi yeniden kurmadan prefablari yenilemek icin
    [MenuItem("VolcanoSweepers/Rebuild Breaker Prefabs")]
    public static void RebuildMenu()
    {
        BuildPrefabs();
        AssetDatabase.SaveAssets();
        Debug.Log("[Breaker] prefablar yenilendi.");
    }
}

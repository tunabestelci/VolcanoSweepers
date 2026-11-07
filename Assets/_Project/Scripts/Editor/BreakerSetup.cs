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
        // Eski malzeme kopyalarini tamamen sil (ayni yola yeniden yazmak prefab referanslarini bozuyor), sonra bastan olustur
        if (AssetDatabase.IsValidFolder(PrefabDir + "Materials")) { AssetDatabase.DeleteAsset(PrefabDir + "Materials"); AssetDatabase.Refresh(); }
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
            if (!Directory.Exists(PrefabDir + "Materials")) { Directory.CreateDirectory(PrefabDir + "Materials"); AssetDatabase.Refresh(); }
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                // glb malzemeleri tam metalik gelebiliyor (yansima alinmadigi icin camgobegi/beyaz gorunur): kopyalayip metali kis
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (src == null) continue;
                    var copy = new Material(src);
                    string safe = string.Concat(src.name.Split(Path.GetInvalidFileNameChars()));
                    copy.name = $"B{t:00}_{safe}";
                    if (copy.HasProperty("metallicFactor")) copy.SetFloat("metallicFactor", copy.GetFloat("metallicFactor") * 0.3f);
                    // cok parlak dokular (savas tokmagi, tehlike balyozu) isikta beyaza yanmasin
                    float dim = t == 7 ? 0.55f : (t == 8 ? 0.8f : 1f);
                    if (dim < 1f && copy.HasProperty("baseColorFactor")) { var bc = copy.GetColor("baseColorFactor"); copy.SetColor("baseColorFactor", new Color(bc.r * dim, bc.g * dim, bc.b * dim, bc.a)); }
                    string mp = $"{PrefabDir}Materials/{copy.name}.mat";
                    AssetDatabase.CreateAsset(copy, mp);
                    mats[i] = copy;
                }
                r.sharedMaterials = mats;
            }

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

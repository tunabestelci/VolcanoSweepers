using System.IO;
using UnityEditor;
using UnityEngine;

// Market ikonlari: her urunun prefabini sabit bir 3/4 aci ve sicak isikla seffaf arka planli PNG'ye cizer
// (Assets/_Project/Art/Icons). ShopKeeper'a kademe sirasiyla atanir.
public static class ShopIcons
{
    const string Dir = "Assets/_Project/Art/Icons/";
    const int Size = 256;

    [MenuItem("VolcanoSweepers/Build Shop Icons")]
    public static void BuildAndAssign()
    {
        var tools = Object.FindFirstObjectByType<PlayerTools>();
        if (tools == null) { Debug.LogWarning("[Icons] PlayerTools yok"); return; }
        Directory.CreateDirectory(Dir);
        // ilk cizimlerde shader'lar arka planda derlenirken nesne gorunmez: senkron derle
        bool async = ShaderUtil.allowAsyncCompilation;
        ShaderUtil.allowAsyncCompilation = false;
        try { BuildInner(tools); } finally { ShaderUtil.allowAsyncCompilation = async; }
    }

    static void BuildInner(PlayerTools tools)
    {
        var toolIcons = RenderSet("Tool", tools.toolPrefabs, IconKind.Scraper);
        var breakerIcons = RenderSet("Breaker", tools.breakerPrefabs, IconKind.Breaker);
        var vesselIcons = RenderSet("Vessel", tools.vesselPrefabs, IconKind.Vessel);
        foreach (var k in Object.FindObjectsByType<ShopKeeper>(FindObjectsInactive.Include))
        {
            k.toolIcons = toolIcons; k.breakerIcons = breakerIcons; k.vesselIcons = vesselIcons;
            EditorUtility.SetDirty(k);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(k.gameObject.scene);
        }
        Debug.Log($"[Icons] {toolIcons.Length}+{breakerIcons.Length}+{vesselIcons.Length} ikon");
    }

    enum IconKind { Scraper, Breaker, Vessel }

    static Texture2D[] RenderSet(string prefix, GameObject[] prefabs, IconKind kind)
    {
        if (prefabs == null) return new Texture2D[0];
        var res = new Texture2D[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            if (prefabs[i] == null) continue;
            string path = $"{Dir}{prefix}{i:00}.png";
            Render(prefabs[i], path, kind);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = TextureImporterType.GUI; ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
            ti.wrapMode = TextureWrapMode.Clamp; ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
            res[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return res;
    }

    static void Render(GameObject prefab, string path, IconKind kind)
    {
        var pru = new PreviewRenderUtility();
        try
        {
            var go = pru.InstantiatePrefabInScene(prefab);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (var l in go.GetComponentsInChildren<Light>(true)) l.enabled = false;
            var water = go.transform.Find("Water"); if (water != null) water.gameObject.SetActive(false);
            var body = go.transform.Find("WaterBody"); if (body != null) body.gameObject.SetActive(false);
            // cam kap ikonda gorunmez kalir: ikon icin buzlu cam rengi opak malzeme
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var ms = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < ms.Length; i++)
                    if (ms[i] != null && ms[i].renderQueue >= 2900)
                    {
                        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                        m.SetColor("_BaseColor", new Color(0.72f, 0.84f, 0.9f)); m.SetFloat("_Smoothness", 0.85f);
                        ms[i] = m; changed = true;
                    }
                if (changed) r.sharedMaterials = ms;
            }
            // urunu capraz yatir: kaziyici/kirici uzun ekseni ekran kosegenine, kaplar dik
            Quaternion rot = kind == IconKind.Vessel ? Quaternion.Euler(-12f, 30f, 0f)
                           : kind == IconKind.Breaker && IsTall(go) ? Quaternion.Euler(0f, -35f, -38f)
                           : Quaternion.Euler(-28f, -50f, 0f) * Quaternion.Euler(0f, 0f, -12f);
            if (kind == IconKind.Scraper && IsTall(go)) rot = Quaternion.Euler(0f, -35f, -38f);
            go.transform.rotation = rot;
            var b = new Bounds(); bool f = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds);
            }
            go.transform.position -= b.center;
            float ext = Mathf.Max(b.extents.x, b.extents.y) * 1.08f;
            var cam = pru.camera;
            cam.orthographic = true; cam.orthographicSize = ext;
            cam.transform.position = new Vector3(0f, 0f, -10f); cam.transform.rotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 30f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            pru.lights[0].intensity = 1.25f; pru.lights[0].color = new Color(1f, 0.93f, 0.84f);
            pru.lights[0].transform.rotation = Quaternion.Euler(35f, -30f, 0f);
            pru.lights[1].intensity = 0.55f; pru.lights[1].color = new Color(1f, 0.55f, 0.3f);
            pru.lights[1].transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            pru.ambientColor = new Color(0.32f, 0.27f, 0.25f);
            var rect = new Rect(0, 0, Size, Size);
            // seffaf arka plan: siyah ve beyaz zeminde iki kez cizip farktan alfa cikar
            cam.backgroundColor = Color.black;
            pru.BeginStaticPreview(rect); pru.Render(true); var texB = pru.EndStaticPreview();
            cam.backgroundColor = Color.white;
            pru.BeginStaticPreview(rect); pru.Render(true); var texW = pru.EndStaticPreview();
            var pb = texB.GetPixels(); var pw = texW.GetPixels();
            var pc = new Color[pb.Length];
            for (int i = 0; i < pb.Length; i++)
            {
                float a = 1f - Mathf.Clamp01(((pw[i].r - pb[i].r) + (pw[i].g - pb[i].g) + (pw[i].b - pb[i].b)) / 3f);
                pc[i] = a > 0.004f ? new Color(pb[i].r / a, pb[i].g / a, pb[i].b / a, a) : new Color(0f, 0f, 0f, 0f);
            }
            var tex = new Texture2D(texB.width, texB.height, TextureFormat.RGBA32, false);
            tex.SetPixels(pc); tex.Apply();
            Object.DestroyImmediate(texB); Object.DestroyImmediate(texW);
            // kenarlara koyu bir dis cizgi: cel-shaded gorunume uysun
            var outp = Outline(tex);
            File.WriteAllBytes(path, outp.EncodeToPNG());
            Object.DestroyImmediate(outp);
            Object.DestroyImmediate(tex);
        }
        finally { pru.Cleanup(); }
    }

    static bool IsTall(GameObject go)
    {
        var b = new Bounds(); bool f = true;
        foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (f) { b = r.bounds; f = false; } else b.Encapsulate(r.bounds); }
        return b.size.y > b.size.z * 1.4f;
    }

    static Texture2D Outline(Texture2D src)
    {
        int w = src.width, h = src.height;
        var px = src.GetPixels();
        var o = new Color[px.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                if (c.a > 0.5f) { o[y * w + x] = c; continue; }
                float near = 0f;
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        int xx = x + dx, yy = y + dy;
                        if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                        if (px[yy * w + xx].a > 0.5f) near = Mathf.Max(near, 1f - (Mathf.Abs(dx) + Mathf.Abs(dy)) * 0.18f);
                    }
                o[y * w + x] = near > 0f ? new Color(0.06f, 0.03f, 0.02f, near) : new Color(0f, 0f, 0f, 0f);
            }
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.SetPixels(o); t.Apply();
        return t;
    }
}

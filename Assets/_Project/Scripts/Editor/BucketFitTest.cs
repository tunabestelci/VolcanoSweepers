using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Suyun kovaya tam oturdugunu (ic duvardan tasmadigini) olcer ve kesit goruntuleri alir.
// batch: -executeMethod BucketFitTest.RunBatch   (grafik cihazi gerekir: -nographics KULLANMA)
public static class BucketFitTest
{
    public static void RunBatch()
    {
        VolcanoSweepersSetup.BuildAll();
        Run();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);   // test nesnelerini at
        BrimView.Run();
        EditorApplication.Exit(0);
    }

    public static void RunViewOnly()
    {
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Volcano.unity", OpenSceneMode.Single);
        Run();
        EditorApplication.Exit(0);
    }

    [MenuItem("VolcanoSweepers/Test Bucket Fit")]
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Bucket.prefab");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var bw = inst.GetComponentInChildren<BucketWater>(true);
        var body = inst.transform.Find("WaterBody");
        var waterMesh = body.GetComponent<MeshFilter>().sharedMesh;
        var model = inst.transform.Find("Model");

        // Suyun yaricapi (y) -> mesh halkalarindan
        var wr = new List<Vector2>();
        foreach (var v in waterMesh.vertices)
            if (Mathf.Abs(v.z) < 1e-5f && v.x >= 0f) wr.Add(new Vector2(v.y, v.x));
        wr.Sort((a, b) => { int c = a.x.CompareTo(b.x); return c != 0 ? c : b.y.CompareTo(a.y); });   // ayni yukseklikte genis halka once
        // Ayni yukseklikteki halkalardan genis olani tut (taban merkez noktasi koni yapmasin)
        var wd = new List<Vector2>();
        foreach (var v in wr)
            if (wd.Count > 0 && Mathf.Abs(wd[wd.Count - 1].x - v.x) < 1e-5f) wd[wd.Count - 1] = new Vector2(v.x, Mathf.Max(wd[wd.Count - 1].y, v.y));
            else wd.Add(v);
        float WaterR(float y)
        {
            for (int i = 1; i < wd.Count; i++)
                if (wd[i].x >= y)
                {
                    float d = wd[i].x - wd[i - 1].x;
                    float k = d > 1e-6f ? (y - wd[i - 1].x) / d : 0f;
                    return Mathf.Lerp(wd[i - 1].y, wd[i].y, k);
                }
            return wd[wd.Count - 1].y;
        }

        // 1) Gercek mesh uzerinde isin testi: eksenden disari atilan isin ic duvara ne kadar uzakta carpiyor?
        var mc = model.GetComponentInChildren<MeshFilter>().gameObject.AddComponent<MeshCollider>();
        mc.sharedMesh = model.GetComponentInChildren<MeshFilter>().sharedMesh;
        Physics.queriesHitBackfaces = true;
        Physics.SyncTransforms();
        var sb = new StringBuilder("[FitTest] y | su yaricapi | ic duvara min uzaklik | bosluk\n");
        float minGap = 1e9f; float minGapY = 0f; int missed = 0;
        for (int i = 0; i <= 28; i++)
        {
            float y = Mathf.Lerp(bw.emptyY + 0.004f, bw.fullY, i / 28f);
            float rw = WaterR(y);
            float dmin = 1e9f;
            for (int a = 0; a < 16; a++)
            {
                float ang = a / 16f * Mathf.PI * 2f;
                var origin = new Vector3(0f, y, 0f);
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                if (Physics.Raycast(origin, dir, out var hit, 0.5f)) dmin = Mathf.Min(dmin, hit.distance);
                else missed++;
            }
            float gap = dmin - rw;
            if (gap < minGap) { minGap = gap; minGapY = y; }
            sb.AppendLine($"[FitTest] {y:F3} | {rw:F3} | {dmin:F3} | {gap * 1000f:F1} mm");
        }
        Debug.Log(sb.ToString());
        Debug.Log($"[FitTest] YAN DUVAR: en kucuk bosluk = {minGap * 1000f:F1} mm (y={minGapY:F3}), iskalayan isin={missed} -> {(minGap > 0.001f ? "SIGIYOR" : "TASIYOR")}");

        // Taban: su tabani ic tabanin ustunde mi
        float floorGap = 1e9f;
        for (int k = 0; k < 8; k++)
        {
            float rx = Mathf.Lerp(0.0f, WaterR(bw.emptyY + 0.01f), k / 7f);
            var origin = new Vector3(rx, bw.emptyY + 0.06f, 0f);
            if (Physics.Raycast(origin, Vector3.down, out var hit, 0.5f))
                floorGap = Mathf.Min(floorGap, bw.emptyY - (origin.y - hit.distance));
        }
        Debug.Log($"[FitTest] TABAN: su tabani ile ic taban arasi = {floorGap * 1000f:F1} mm (>=0 olmali)");

        // 2) Hacim/seviye tutarliligi
        float Vol(float y)
        {
            float v = 0f; float py = wr[0].x; float pr = wr[0].y;
            foreach (var p in wr)
            {
                if (p.x <= py) continue;
                float top = Mathf.Min(p.x, y);
                if (top <= py) break;
                float r2 = Mathf.Lerp(pr, p.y, (top - py) / (p.x - py));
                v += Mathf.PI * (top - py) * (pr * pr + pr * r2 + r2 * r2) / 3f;
                py = p.x; pr = p.y;
                if (top >= y) break;
            }
            return v;
        }
        float total = Vol(wr[wr.Count - 1].x);
        var sb2 = new StringBuilder("[FitTest] doluluk -> seviye -> gercek hacim orani: ");
        foreach (float f in new[] { 0.25f, 0.5f, 0.75f, 1f })
            sb2.Append($"{f:F2}->{bw.LevelY(f):F3}->{Vol(bw.LevelY(f)) / total:F2}  ");
        Debug.Log(sb2.ToString());
        Debug.Log($"[FitTest] ic hacim = {bw.totalVolume * 1000f:F2} L (kova birimi), agiz yaricapi={bw.rimRadius:F3}, taban={bw.emptyY:F3}, agiz={bw.fullY:F3}");

        // 3) Egimde dokulme baslangici (PlayerTools ile ayni formul)
        var sb3 = new StringBuilder("[FitTest] egim -> dudak ustu yukseklik (head, mm) [doluluk 0.3 / 0.6 / 1.0]\n");
        foreach (int tilt in new[] { 0, 20, 35, 50, 65, 80, 95, 110 })
        {
            sb3.Append($"[FitTest] {tilt,3} derece: ");
            foreach (float f in new[] { 0.3f, 0.6f, 1.0f })
            {
                inst.transform.rotation = Quaternion.Euler(tilt, 0f, 0f);
                Vector3 u = inst.transform.up;
                float yPlane = inst.transform.TransformPoint(new Vector3(0f, bw.LevelY(f), 0f)).y;
                Vector3 rimC = inst.transform.TransformPoint(new Vector3(0f, bw.fullY, 0f));
                Vector3 dl = Vector3.down - u * Vector3.Dot(Vector3.down, u);
                Vector3 lipDir = dl.magnitude > 1e-3f ? dl.normalized : Vector3.forward;
                float head = yPlane - (rimC + lipDir * bw.rimRadius).y;
                sb3.Append($"{head * 1000f,7:F1}  ");
            }
            sb3.AppendLine();
        }
        Debug.Log(sb3.ToString());
        inst.transform.rotation = Quaternion.identity;
        Object.DestroyImmediate(mc);

        // 4) Kesit goruntuleri
        RenderSections(inst, bw, body, model);
        Object.DestroyImmediate(inst);
    }

    static void RenderSections(GameObject inst, BucketWater bw, Transform body, Transform model)
    {
        string dir = "Library/BucketFit";
        Directory.CreateDirectory(dir);
        EditorSceneManagerNew();
        var lightGo = new GameObject("L");
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 0.6f;
        lightGo.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.35f, 0.4f);

        var camGo = new GameObject("C");
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.45f, 0.5f, 0.58f);
        cam.orthographic = true;
        cam.orthographicSize = 0.26f;
        cam.transform.position = new Vector3(0f, 0f, 1.5f);
        cam.transform.rotation = Quaternion.LookRotation(Vector3.back);

        var secMat = new Material(Shader.Find("VolcanoSweepers/DebugSection"));
        secMat.SetFloat("_SectionZ", 0f);
        foreach (var r in model.GetComponentsInChildren<Renderer>()) r.sharedMaterial = secMat;
        var waterMat = new Material(body.GetComponent<Renderer>().sharedMaterial);
        waterMat.SetFloat("_SectionZ", 0f);
        body.GetComponent<Renderer>().sharedMaterial = waterMat;
        body.gameObject.SetActive(true);

        var cases = new[] { (0, 0.35f), (0, 0.7f), (0, 1f), (50, 0.7f), (80, 0.5f) };
        foreach (var (tilt, fill) in cases)
        {
            inst.transform.rotation = Quaternion.Euler(0f, 0f, tilt);
            bw.SetFill(fill);
            bw.Refresh();
            var rt = new RenderTexture(512, 512, 24);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(512, 512, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
            tex.Apply();
            File.WriteAllBytes($"{dir}/section_t{tilt}_f{(int)(fill * 100)}.png", tex.EncodeToPNG());
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
        }
        // Gercek materyallerle, ustten 3/4 gorunum: dolu kovanin agzinda tasma/kubbe var mi?
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Bucket.prefab");
            var real = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            real.transform.position = new Vector3(0f, 0f, 0f);
            real.transform.localScale = Vector3.one * 2.289f;
            var rbw = real.GetComponentInChildren<BucketWater>(true);
            rbw.gameObject.SetActive(true);
            real.transform.Find("WaterBody").gameObject.SetActive(true);   // editor modunda OnEnable calismaz
            inst.SetActive(false);
            cam.orthographic = false;
            cam.fieldOfView = 35f;
            foreach (float f in new[] { 1f, 0.5f })
            {
                rbw.SetFill(f);
                rbw.Refresh();
                cam.transform.position = new Vector3(0f, 1.0f, -1.9f);
                cam.transform.LookAt(new Vector3(0f, 0.15f, 0f));
                var rt = new RenderTexture(768, 512, 24);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(768, 512, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, 768, 512), 0, 0);
                tex.Apply();
                File.WriteAllBytes($"{dir}/perspective_f{(int)(f * 100)}.png", tex.EncodeToPNG());
                cam.targetTexture = null;
                RenderTexture.active = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
            Object.DestroyImmediate(real);
        }
        Debug.Log("[FitTest] kesit goruntuleri: " + Path.GetFullPath(dir));
    }

    static void EditorSceneManagerNew()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
    }
}

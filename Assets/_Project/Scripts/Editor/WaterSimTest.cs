using System.Diagnostics;
using UnityEditor;
using UnityEngine;

// Su simulasyonunu editorde (oyun calistirmadan) sayisal olarak dener.
// Menu: VolcanoSweepers/Test Water Sim   |   batch: -executeMethod WaterSimTest.RunBatch
public static class WaterSimTest
{
    [MenuItem("VolcanoSweepers/Test Water Sim")]
    public static void RunMenu() { Run(); }

    public static void RunBatch()
    {
        Run();
        VolcanoSweepersSetup.BuildAll();
        EditorApplication.Exit(0);
    }

    static void Run()
    {
        var go = new GameObject("WaterSimTest");
        var sim = go.AddComponent<WaterSim>();
        sim.SetupFlat(0f);

        // 1) Baraj yikilmasi: duz zeminde 0.45 x 0.6 x 0.6 m'lik su blogu
        const float s = WaterSim.Spacing;
        int n = 0;
        for (float x = 0.1f; x < 0.55f; x += s)
            for (float y = 0.05f; y < 0.65f; y += s)
                for (float z = 0.1f; z < 0.7f; z += s)
                {
                    WaterSim.Emit(new Vector3(x, y, z) + Random.insideUnitSphere * 0.003f, Vector3.zero, 1000f);
                    n++;
                }
        UnityEngine.Debug.Log($"[SimTest] baslangic parcacik: {n}");
        var sw = Stopwatch.StartNew();
        for (int step = 1; step <= 360; step++)
        {
            sim.Step();
            if (step == 30 || step == 120 || step == 360) Report(sim, "adim " + step);
        }
        sw.Stop();
        UnityEngine.Debug.Log($"[SimTest] 360 adim: {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / 360f:F2} ms/adim, {sim.Alive} parcacik)");

        // 2) Kap testi: kovanin ustunden 400 parcacik dok
        var bgo = new GameObject("TestBucket");
        var bb = bgo.AddComponent<BigBucket>();
        bb.fillPerParticle = 1f / 400f;
        WaterSim.RegisterContainer(bb, new Vector3(3f, 0.4f, 3f), 0.3f, 0.34f, 0f, 0.8f);
        // 400 parcacik, 5 sn boyunca saniyede 80 (dar bir akis)
        int emitted = 0;
        for (int step = 0; step < 420; step++)
        {
            if (emitted < 400 && step % 1 == 0 && step < 300)
            {
                int per = step % 3 == 0 ? 2 : 1;
                for (int q = 0; q < per && emitted < 400; q++, emitted++)
                {
                    Vector2 o = Random.insideUnitCircle * 0.1f;
                    WaterSim.Emit(new Vector3(3f + o.x, 1.4f, 3f + o.y), new Vector3(0f, -2f, 0f), 50f);
                }
            }
            sim.Step();
        }
        UnityEngine.Debug.Log($"[SimTest] kap dolulugu: {bb.Fill:F2} (beklenen ~1.00)");

        Object.DestroyImmediate(bgo);
        Object.DestroyImmediate(go);
    }

    static void Report(WaterSim sim, string label)
    {
        int n = sim.Alive;
        Vector3 min = Vector3.one * 1e9f, max = -Vector3.one * 1e9f;
        float sumSp = 0f, maxSp = 0f, sumY = 0f;
        int nan = 0, fluid = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = sim.PositionOf(i);
            Vector3 v = sim.VelocityOf(i);
            if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z)) { nan++; continue; }
            if (sim.KindOf(i) != 1) continue;
            fluid++;
            min = Vector3.Min(min, p); max = Vector3.Max(max, p);
            float sp = v.magnitude;
            sumSp += sp; maxSp = Mathf.Max(maxSp, sp);
            sumY += p.y;
        }
        // yatay dagilim (agirlik merkezine uzaklik yuzdelikleri)
        var radii = new System.Collections.Generic.List<float>();
        Vector3 cen = Vector3.zero; int cn = 0;
        for (int i = 0; i < n; i++) { if (sim.KindOf(i) != 1) continue; Vector3 p = sim.PositionOf(i); cen += new Vector3(p.x, 0f, p.z); cn++; }
        if (cn > 0) cen /= cn;
        for (int i = 0; i < n; i++) { if (sim.KindOf(i) != 1) continue; Vector3 p = sim.PositionOf(i); radii.Add(new Vector2(p.x - cen.x, p.z - cen.z).magnitude); }
        radii.Sort();
        if (radii.Count > 0)
            UnityEngine.Debug.Log($"[SimTest] {label} yatay yayilim: medyan={radii[radii.Count / 2]:F2} p90={radii[(int)(radii.Count * 0.9f)]:F2} max={radii[radii.Count - 1]:F2} (beklenen yaklasik p90 1.0-1.3)");
        UnityEngine.Debug.Log($"[SimTest] {label}: canli={n} akiskan={fluid} nan={nan} ort.hiz={sumSp / Mathf.Max(1, fluid):F2} max.hiz={maxSp:F2} ort.y={sumY / Mathf.Max(1, fluid):F3} min={min} max={max}");
    }
}

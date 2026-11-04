using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

// Kovanin gercek ic seklini mesh'ten cikarir ve suyun hacmini bu sekle tam oturtur.
public static class BucketProfile
{
    // Kova kokunun yerel uzayinda ic yuz profili: (y, ic yaricap), y artan sirada
    public static List<Vector2> Inner(GameObject root)
    {
        var mf = root.GetComponentInChildren<MeshFilter>();
        var mesh = mf.sharedMesh;
        var tr = mf.transform;
        var verts = mesh.vertices;
        var norms = mesh.normals;
        Bounds b = new Bounds();
        bool first = true;
        var pts = new Vector3[verts.Length];
        var nrm = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            pts[i] = root.transform.InverseTransformPoint(tr.TransformPoint(verts[i]));
            nrm[i] = root.transform.InverseTransformDirection(tr.TransformDirection(norms[i])).normalized;
            if (first) { b = new Bounds(pts[i], Vector3.zero); first = false; } else b.Encapsulate(pts[i]);
        }
        float cx = b.center.x, cz = b.center.z;
        var groups = new SortedDictionary<int, float>();
        var counts = new Dictionary<int, int>();
        var sectors = new Dictionary<int, int>();
        var allSectors = new Dictionary<int, int>();
        var allCounts = new Dictionary<int, int>();
        for (int i = 0; i < pts.Length; i++)
        {
            Vector3 radial = new Vector3(pts[i].x - cx, 0f, pts[i].z - cz);
            float r = radial.magnitude;
            if (r < 1e-4f) continue;
            // Govde halkasi (ic veya dis yuz): kovanin gercek agzini bulmak icin
            {
                int ak = Mathf.RoundToInt(pts[i].y * 500f);
                int asec = Mathf.Clamp((int)((Mathf.Atan2(radial.z, radial.x) + Mathf.PI) / (2f * Mathf.PI) * 16f), 0, 15);
                allSectors[ak] = (allSectors.TryGetValue(ak, out int am) ? am : 0) | (1 << asec);
                allCounts[ak] = allCounts.TryGetValue(ak, out int ac) ? ac + 1 : 1;
            }
            if (Vector3.Dot(nrm[i], radial / r) > -0.3f) continue;   // yalniz ice bakan yuzler
            int key = Mathf.RoundToInt(pts[i].y * 500f);                           // 2 mm
            if (!groups.TryGetValue(key, out float cur) || r < cur) groups[key] = r;
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
            int sector = Mathf.Clamp((int)((Mathf.Atan2(radial.z, radial.x) + Mathf.PI) / (2f * Mathf.PI) * 16f), 0, 15);
            sectors[key] = (sectors.TryGetValue(key, out int sm) ? sm : 0) | (1 << sector);
        }
        // Kovanin gercek agzi: her yone dolu en ust halka (sap teli yalniz iki yanda oldugu icin sayilmaz)
        float bodyTop = float.MinValue;
        foreach (var kv in allSectors)
        {
            int cov = 0;
            for (int b3 = 0; b3 < 16; b3++) if ((kv.Value & (1 << b3)) != 0) cov++;
            if (cov >= 12 && allCounts[kv.Key] >= 30) bodyTop = Mathf.Max(bodyTop, kv.Key / 500f);
        }
        // Gercek agiz yuksekligi ve agizdaki ic yaricap: gercek mesh uzerinde isinlarla olcum
        float rimR = -1f;
        {
            var mcGo = mf.gameObject;
            var mc = mcGo.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            Physics.SyncTransforms();
            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            var tops = new List<float>();
            for (int a = 0; a < 16; a++)
            {
                float ang = a / 16f * Mathf.PI * 2f;
                float best = -1f;
                for (float rr = 0.12f; rr <= 0.17f; rr += 0.004f)
                {
                    var o = root.transform.TransformPoint(new Vector3(cx + Mathf.Cos(ang) * rr, 0.4f, cz + Mathf.Sin(ang) * rr));
                    if (Physics.Raycast(o, -root.transform.up, out var hit, 0.9f))
                        best = Mathf.Max(best, root.transform.InverseTransformPoint(hit.point).y);
                }
                if (best > -1f) tops.Add(best);
            }
            tops.Sort();
            if (tops.Count > 0)
            {
                float rimTop = tops[tops.Count / 2];       // ortanca: sap teli yalniz iki yanda
                if (rimTop > bodyTop) bodyTop = rimTop;
            }
            var rs = new List<float>();
            for (int a = 0; a < 16; a++)
            {
                float ang = a / 16f * Mathf.PI * 2f;
                var o = root.transform.TransformPoint(new Vector3(cx, bodyTop - 0.006f, cz));
                var d = root.transform.TransformDirection(new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)));
                if (Physics.Raycast(o, d, out var hit, 0.5f)) rs.Add(hit.distance);
            }
            rs.Sort();
            if (rs.Count > 0) rimR = rs[rs.Count / 2];
            Physics.queriesHitBackfaces = prevBack;
            Object.DestroyImmediate(mc);
        }
        var list = new List<Vector2>();
        foreach (var kv in groups)
        {
            if (kv.Key / 500f > bodyTop - 0.004f) continue;     // son halka olcumle eklenecek
            // Govde halkasi her yone dolu olmali; sapin (tel) parcalari yalniz iki yanda bulunur
            int covered = 0;
            for (int b2 = 0; b2 < 16; b2++) if ((sectors[kv.Key] & (1 << b2)) != 0) covered++;
            if (counts[kv.Key] >= 3 && covered >= 8) list.Add(new Vector2(kv.Key / 500f, kv.Value));
        }
        if (list.Count > 0)
        {
            // Agza kadar uzat: olculen ic yaricap, onceki halkayi gecmesin (dudak disa dogru acilmasin)
            float prevR = list[list.Count - 1].y;
            float topR = rimR > 0f ? Mathf.Min(rimR, prevR) : prevR;
            list.Add(new Vector2(bodyTop - 0.0005f, topR));
        }
        Debug.Log($"[Bucket] govde agzi y={bodyTop:F3}, ic profil son nokta y={(list.Count > 0 ? list[list.Count - 1].x : 0f):F3}");
        return list;
    }

    // Su hacmi: icerideki profile %5 pay birakarak oturan bir donel mesh
    public static Mesh BuildWaterMesh(List<Vector2> inner, out float volume, out float[] levelTable, out float rimRadius, out float floorY, out float fullY)
    {
        const float clearance = 0.988f;
        const float margin = 0.0012f;
        const int seg = 40;
        var prof = new List<Vector2>();
        foreach (var p in inner) prof.Add(new Vector2(p.x, Mathf.Max(0.005f, p.y * clearance - margin)));

        prof[0] = new Vector2(prof[0].x + 0.004f, prof[0].y);      // su ic tabana gomulmesin
        floorY = prof[0].x;
        float topY = prof[prof.Count - 1].x;
        rimRadius = inner[inner.Count - 1].y;
        fullY = topY - 0.0008f;                      // su agzin hemen altinda biter (tasmaz)

        // Hacim (kesik koniler) ve seviye tablosu (doluluk -> yukseklik)
        var cum = new float[prof.Count];
        for (int i = 1; i < prof.Count; i++)
        {
            float h = prof[i].x - prof[i - 1].x;
            float r1 = prof[i - 1].y, r2 = prof[i].y;
            cum[i] = cum[i - 1] + Mathf.PI * h * (r1 * r1 + r1 * r2 + r2 * r2) / 3f;
        }
        volume = cum[prof.Count - 1];
        levelTable = new float[65];
        for (int t = 0; t <= 64; t++)
        {
            float target = volume * t / 64f;
            float y = topY;
            for (int i = 1; i < prof.Count; i++)
            {
                if (cum[i] >= target)
                {
                    float k = cum[i] > cum[i - 1] ? (target - cum[i - 1]) / (cum[i] - cum[i - 1]) : 0f;
                    y = Mathf.Lerp(prof[i - 1].x, prof[i].x, k);
                    break;
                }
            }
            levelTable[t] = Mathf.Min(y, fullY);
        }

        // Mesh: taban merkezi + halkalar + yuzey kesmesi icin ustte uzanti
        var rings = new List<Vector2> { new Vector2(floorY, 0f) };
        rings.AddRange(prof);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int ri = 0; ri < rings.Count; ri++)
            for (int s = 0; s <= seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * rings[ri].y, rings[ri].x, Mathf.Sin(a) * rings[ri].y));
                uvs.Add(new Vector2(s / (float)seg * 2f, rings[ri].x * 4f));
            }
        for (int ri = 0; ri < rings.Count - 1; ri++)
            for (int s = 0; s < seg; s++)
            {
                int a = ri * (seg + 1) + s, b = a + seg + 1;
                tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var mesh = new Mesh { name = "BucketWaterVolume" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    [MenuItem("VolcanoSweepers/Dump Bucket Profile")]
    public static void Dump()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Bucket.prefab");
        var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var inner = Inner(inst);
        var sb = new StringBuilder("[Bucket] ic profil (y, r): ");
        foreach (var p in inner) sb.Append($"({p.x:F3},{p.y:F3}) ");
        Debug.Log(sb.ToString());
        Object.DestroyImmediate(inst);
    }
}

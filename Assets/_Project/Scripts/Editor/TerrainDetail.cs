using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// Arazi icin ek detay: gurultu yardimcilari, prosedurel kaya dokulari (bazalt/curuf) ve dusuk poligonlu kaya/sutun modelleri.
public static class TerrainDetail
{
    // GLSL tarzi smoothstep(kenar0, kenar1, x): 0..1 (Mathf.SmoothStep'in imzasi farklidir)
    public static float S(float e0, float e1, float x)
    {
        float t = Mathf.Clamp01((x - e0) / (e1 - e0));
        return t * t * (3f - 2f * t);
    }

    // ---------- Gurultu ----------

    public static float Perlin(float x, float y) { return Mathf.PerlinNoise(x + 1000f, y + 1000f); }

    public static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Perlin(x, y) * amp; norm += amp;
            x = x * 2.03f + 17.1f; y = y * 2.03f - 9.7f; amp *= 0.5f;
        }
        return sum / norm;
    }

    // Sirt (keskin tepe) gurultusu 0..1: 1 = sirtin tam ustu
    public static float Ridged(float x, float y, int octaves)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            float n = 1f - Mathf.Abs(Perlin(x, y) * 2f - 1f);
            n *= n;
            sum += n * amp; norm += amp;
            x = x * 2.1f + 5.2f; y = y * 2.1f + 1.3f; amp *= 0.5f;
        }
        return sum / norm;
    }

    static uint Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 144665);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }
    static float Rnd(int x, int y, int seed) { return (Hash(x, y, seed) & 0xFFFFFF) / (float)0xFFFFFF; }

    // ---------- Duzgun tekrar eden (tileable) dokular ----------

    // Voronoi: F1, F2 ve en yakin hucre kimligi. Tekrar eden: n x n hucre.
    static void Voronoi(float u, float v, int n, int seed, out float f1, out float f2, out int cx, out int cy)
    {
        float px = u * n, py = v * n;
        int ix = Mathf.FloorToInt(px), iy = Mathf.FloorToInt(py);
        f1 = f2 = 10f; cx = cy = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = ix + dx, gy = iy + dy;
                int wx = ((gx % n) + n) % n, wy = ((gy % n) + n) % n;
                float jx = gx + 0.15f + 0.7f * Rnd(wx, wy, seed);
                float jy = gy + 0.15f + 0.7f * Rnd(wx, wy, seed + 11);
                float d = Mathf.Sqrt((jx - px) * (jx - px) + (jy - py) * (jy - py));
                if (d < f1) { f2 = f1; f1 = d; cx = wx; cy = wy; }
                else if (d < f2) f2 = d;
            }
    }

    static float VNoise(float u, float v, int period, int seed)
    {
        float x = u * period, y = v * period;
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
        float fx = x - ix, fy = y - iy;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int x0 = ((ix % period) + period) % period, x1 = (x0 + 1) % period;
        int y0 = ((iy % period) + period) % period, y1 = (y0 + 1) % period;
        float a = Mathf.Lerp(Rnd(x0, y0, seed), Rnd(x1, y0, seed), fx);
        float b = Mathf.Lerp(Rnd(x0, y1, seed), Rnd(x1, y1, seed), fx);
        return Mathf.Lerp(a, b, fy);
    }

    static float TFbm(float u, float v, int basePeriod, int octaves, int seed)
    {
        float s = 0f, a = 0.5f, nrm = 0f; int p = basePeriod;
        for (int i = 0; i < octaves; i++) { s += VNoise(u, v, p, seed + i * 7) * a; nrm += a; a *= 0.5f; p *= 2; }
        return s / nrm;
    }

    static void WriteTexture(string colorPath, string normalPath, Color[] col, float[] height, int n, float normalStrength)
    {
        var tc = new Texture2D(n, n, TextureFormat.RGB24, false);
        tc.SetPixels(col);
        File.WriteAllBytes(colorPath, tc.EncodeToPNG());
        Object.DestroyImmediate(tc);

        var nm = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float hl = height[y * n + (x + n - 1) % n], hr = height[y * n + (x + 1) % n];
                float hd = height[((y + n - 1) % n) * n + x], hu = height[((y + 1) % n) * n + x];
                var nv = new Vector3((hl - hr) * normalStrength, (hd - hu) * normalStrength, 1f).normalized;
                nm[y * n + x] = new Color(nv.x * 0.5f + 0.5f, nv.y * 0.5f + 0.5f, nv.z * 0.5f + 0.5f, 1f);
            }
        var tn = new Texture2D(n, n, TextureFormat.RGB24, false);
        tn.SetPixels(nm);
        File.WriteAllBytes(normalPath, tn.EncodeToPNG());
        Object.DestroyImmediate(tn);

        AssetDatabase.ImportAsset(colorPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(normalPath) as TextureImporter;
        if (ti != null && ti.textureType != TextureImporterType.NormalMap)
        {
            ti.textureType = TextureImporterType.NormalMap;
            ti.SaveAndReimport();
        }
    }

    // Bazalt: kose kose catlak aglariyla bolunmus koyu sutunlar
    public static void GenerateBasalt(string colorPath, string normalPath)
    {
        const int N = 512;
        var col = new Color[N * N]; var hgt = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                Voronoi(u, v, 9, 3, out float f1, out float f2, out int cx, out int cy);
                float edge = f2 - f1;
                float crack = S(0.02f, 0.16f, edge);
                float cellTone = 0.78f + 0.44f * Rnd(cx, cy, 91);
                float bulge = Mathf.Clamp01(1f - f1 * 1.15f);
                float grain = TFbm(u, v, 24, 3, 5);
                float lum = (0.17f + 0.13f * bulge) * cellTone * (0.8f + 0.4f * grain) * Mathf.Lerp(0.18f, 1f, crack);
                // soguk gri-mavi ton; bazi hucrelerde sicak pas lekeleri
                float rust = S(0.62f, 0.9f, Rnd(cx, cy, 33)) * 0.55f;
                col[y * N + x] = new Color(lum * (0.92f + rust), lum * (0.95f + rust * 0.35f), lum * 1.06f, 1f);
                hgt[y * N + x] = bulge * crack * 0.7f + grain * 0.12f;
            }
        WriteTexture(colorPath, normalPath, col, hgt, N, 14f);
    }

    // Curuf: sicak kahverengi/kirmizi, ince cakilli yuzey
    public static void GenerateScoria(string colorPath, string normalPath)
    {
        const int N = 512;
        var col = new Color[N * N]; var hgt = new float[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float u = x / (float)N, v = y / (float)N;
                Voronoi(u, v, 30, 8, out float f1, out float f2, out int cx, out int cy);
                float pebble = Mathf.Clamp01(1f - f1 * 1.3f);
                float tone = Rnd(cx, cy, 52);
                float broad = TFbm(u, v, 6, 4, 14);
                float lum = Mathf.Lerp(0.55f, 1.05f, tone) * Mathf.Lerp(0.55f, 1.15f, broad) * (0.7f + 0.4f * pebble);
                lum *= Mathf.Lerp(0.5f, 1f, S(0.0f, 0.18f, f2 - f1));
                col[y * N + x] = new Color(0.36f * lum, 0.165f * lum, 0.105f * lum, 1f);
                hgt[y * N + x] = pebble * 0.6f + broad * 0.25f;
            }
        WriteTexture(colorPath, normalPath, col, hgt, N, 10f);
    }

    // ---------- Dusuk poligonlu modeller ----------

    static void Add(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris, Vector3 a, Vector3 b, Vector3 c, float uvScale)
    {
        Vector3 n = Vector3.Cross(b - a, c - a).normalized;
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        norms.Add(n); norms.Add(n); norms.Add(n);
        // baskin eksene gore duzlemsel UV
        Vector3 an = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
        System.Func<Vector3, Vector2> uv;
        if (an.y >= an.x && an.y >= an.z) uv = p => new Vector2(p.x, p.z) * uvScale;
        else if (an.x >= an.z) uv = p => new Vector2(p.z, p.y) * uvScale;
        else uv = p => new Vector2(p.x, p.y) * uvScale;
        uvs.Add(uv(a)); uvs.Add(uv(b)); uvs.Add(uv(c));
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
    }

    static Mesh Finish(string name, List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)
    {
        var m = new Mesh { name = name };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0);
        m.RecalculateBounds();
        return m;
    }

    // Yuzu keskin, tabani duz, cokgen bir kaya
    public static Mesh MakeBoulder(string name, int seed, Vector3 stretch)
    {
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var p = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
        };
        for (int i = 0; i < p.Count; i++) p[i] = p[i].normalized;
        var f = new List<int[]>
        {
            new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
            new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
            new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
            new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1},
        };
        // bir kademe bolme
        var cache = new Dictionary<long, int>();
        System.Func<int, int, int> mid = (a, b) =>
        {
            long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(k, out int idx)) return idx;
            p.Add(((p[a] + p[b]) * 0.5f).normalized);
            cache[k] = p.Count - 1;
            return p.Count - 1;
        };
        var f2 = new List<int[]>();
        foreach (var tr in f)
        {
            int a = mid(tr[0], tr[1]), b = mid(tr[1], tr[2]), c = mid(tr[2], tr[0]);
            f2.Add(new[] { tr[0], a, c }); f2.Add(new[] { tr[1], b, a }); f2.Add(new[] { tr[2], c, b }); f2.Add(new[] { a, b, c });
        }
        // bozulma: dusuk frekansli kabartilar + keskin faset
        var rnd = new System.Random(seed);
        float ox = (float)rnd.NextDouble() * 50f, oy = (float)rnd.NextDouble() * 50f, oz = (float)rnd.NextDouble() * 50f;
        var q = new Vector3[p.Count];
        for (int i = 0; i < p.Count; i++)
        {
            Vector3 u = p[i];
            float n = (Perlin(u.x * 1.6f + ox, u.y * 1.6f + oy) + Perlin(u.y * 1.6f + oy, u.z * 1.6f + oz) + Perlin(u.z * 1.6f + oz, u.x * 1.6f + ox)) / 3f;
            float r = 0.72f + (n - 0.5f) * 1.1f + ((i * 2654435761u) % 100) / 100f * 0.12f;
            Vector3 w = new Vector3(u.x * stretch.x, u.y * stretch.y, u.z * stretch.z) * r;
            w.y = Mathf.Max(w.y, -0.38f * stretch.y);     // tabani duz: yere oturur
            q[i] = w;
        }
        var v = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>(); var tt = new List<int>();
        foreach (var tr in f2) Add(v, nn, uv, tt, q[tr[0]], q[tr[1]], q[tr[2]], 0.55f);
        return Finish(name, v, nn, uv, tt);
    }

    // Bazalt sutun kumesi: farkli boyda, hafif egik, ustu egimli altigen prizmalar
    public static Mesh MakeColumns(string name, int seed)
    {
        var rnd = new System.Random(seed);
        var v = new List<Vector3>(); var nn = new List<Vector3>(); var uv = new List<Vector2>(); var tt = new List<int>();
        int count = 7;
        for (int c = 0; c < count; c++)
        {
            Vector2 off = c == 0 ? Vector2.zero : new Vector2(Mathf.Cos(c * 1.05f + (float)rnd.NextDouble() * 0.4f), Mathf.Sin(c * 1.05f + (float)rnd.NextDouble() * 0.4f)) * (0.95f + (float)rnd.NextDouble() * 0.25f);
            float rad = 0.46f + (float)rnd.NextDouble() * 0.12f;
            float h = c == 0 ? 6.5f : 2.2f + (float)rnd.NextDouble() * 4f;
            float tilt = ((float)rnd.NextDouble() - 0.5f) * 0.18f;
            Vector3 lean = new Vector3(tilt, 0f, ((float)rnd.NextDouble() - 0.5f) * 0.18f);
            var bot = new Vector3[6]; var top = new Vector3[6];
            float topSlope = ((float)rnd.NextDouble() - 0.5f) * 0.9f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + 0.3f;
                float x = Mathf.Cos(a) * rad, z = Mathf.Sin(a) * rad;
                bot[i] = new Vector3(off.x + x, -1.2f, off.y + z);
                top[i] = new Vector3(off.x + x, h + x * topSlope, off.y + z) + lean * h;
            }
            for (int i = 0; i < 6; i++)
            {
                int j = (i + 1) % 6;
                Add(v, nn, uv, tt, bot[i], top[i], top[j], 0.5f);
                Add(v, nn, uv, tt, bot[i], top[j], bot[j], 0.5f);
            }
            Vector3 center = Vector3.zero; for (int i = 0; i < 6; i++) center += top[i]; center /= 6f;
            for (int i = 0; i < 6; i++) Add(v, nn, uv, tt, center, top[(i + 1) % 6], top[i], 0.5f);
        }
        return Finish(name, v, nn, uv, tt);
    }

    public static GameObject SavePrefab(string path, Mesh mesh, Material mat)
    {
        string meshPath = path.Replace(".prefab", ".mesh.asset");
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
        var go = new GameObject(Path.GetFileNameWithoutExtension(path));
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }
}

using Unity.Collections;
using UnityEngine;

public enum MineResult { None, Chipped, Broken }

// Kraterin tamamini dolduran surekli lav golu. Iki katmani CPU'da tutar:
//   crust: lavin ustundeki obsidyen kabugu (0..1)
//   dig:   asagi dogru kazilmis derinlik (0..1, 1 = ana kayaya ulasildi)
// Ikisi de bir dokuya yazilir; shader yuzeyi bu dokudan sekillendirir.
// Lav seviyesi dunya y = transform.position.y; arazi bu seviyenin altinda kalan yerler golu olusturur.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LavaField : MonoBehaviour
{
    public static LavaField I;

    public Material material;
    public int res = 512;
    public int meshRes = 384;
    public float size = 116f;
    public float poolRadius = 55f;
    public float digDepth = 12f;
    public float crustHeight = 0.14f;
    public float digStep = 0.1f;

    NativeArray<float> crust;
    NativeArray<float> dig;
    Texture2D maskTex;
    Color32[] pixels;
    bool dirty;
    Material matInstance;
    Terrain terrain;
    float baseY;
    float progressCache;
    float progressTime = -10f;

    public float BaseY => baseY;

    void Awake()
    {
        I = this;
        baseY = transform.position.y;
        terrain = FindFirstObjectByType<Terrain>();
        crust = new NativeArray<float>(res * res, Allocator.Persistent);
        dig = new NativeArray<float>(res * res, Allocator.Persistent);
        pixels = new Color32[res * res];
        maskTex = new Texture2D(res, res, TextureFormat.RGBA32, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        matInstance = new Material(material);
        matInstance.SetTexture("_Mask", maskTex);
        matInstance.SetFloat("_FieldSize", size);
        matInstance.SetFloat("_CrustHeight", crustHeight);
        matInstance.SetFloat("_DigDepth", digDepth);
        matInstance.SetFloat("_PoolRadius", poolRadius);
        GetComponent<MeshRenderer>().sharedMaterial = matInstance;
        GetComponent<MeshFilter>().sharedMesh = BuildMesh();
        dirty = true;
        Upload();
    }

    Mesh BuildMesh()
    {
        int n = meshRes;
        var verts = new Vector3[n * n];
        var uvs = new Vector2[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)(n - 1), v = y / (float)(n - 1);
                verts[y * n + x] = new Vector3((u - 0.5f) * size, 0f, (v - 0.5f) * size);
                uvs[y * n + x] = new Vector2(u, v);
            }
        var tris = new int[(n - 1) * (n - 1) * 6];
        int t = 0;
        for (int y = 0; y < n - 1; y++)
            for (int x = 0; x < n - 1; x++)
            {
                int i = y * n + x;
                tris[t++] = i; tris[t++] = i + n; tris[t++] = i + 1;
                tris[t++] = i + 1; tris[t++] = i + n; tris[t++] = i + n + 1;
            }
        var mesh = new Mesh { name = "LavaFieldMesh", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.triangles = tris;
        mesh.bounds = new Bounds(new Vector3(0f, -digDepth * 0.5f, 0f), new Vector3(size, digDepth + 2f, size));
        return mesh;
    }

    void LateUpdate()
    {
        if (dirty) Upload();
    }

    void Upload()
    {
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32((byte)(crust[i] * 255f), (byte)(dig[i] * 255f), 0, 255);
        maskTex.SetPixels32(pixels);
        maskTex.Apply(false);
        dirty = false;
    }

    // ---------- Koordinat ----------

    Vector2 ToGrid(Vector3 world)
    {
        Vector3 l = world - transform.position;
        return new Vector2((l.x / size + 0.5f) * (res - 1), (l.z / size + 0.5f) * (res - 1));
    }

    public NativeArray<float> CrustNA => crust;
    public NativeArray<float> DigNA => dig;

    void OnDestroy()
    {
        if (crust.IsCreated) crust.Dispose();
        if (dig.IsCreated) dig.Dispose();
    }

    float Sample(NativeArray<float> a, Vector2 g)
    {
        float x = Mathf.Clamp(g.x, 0f, res - 1.001f), y = Mathf.Clamp(g.y, 0f, res - 1.001f);
        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        float a0 = Mathf.Lerp(a[y0 * res + x0], a[y0 * res + x0 + 1], fx);
        float a1 = Mathf.Lerp(a[(y0 + 1) * res + x0], a[(y0 + 1) * res + x0 + 1], fx);
        return Mathf.Lerp(a0, a1, fy);
    }

    static float CrustShape(float c) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.6f, c)); }

    public float CrustAt(Vector3 world) { return Sample(crust, ToGrid(world)); }
    public float DigAt(Vector3 world) { return Sample(dig, ToGrid(world)); }

    public float TerrainHeight(Vector3 world)
    {
        if (terrain == null) return baseY - 1000f;
        return terrain.SampleHeight(world) + terrain.transform.position.y;
    }

    float PlanarDist(Vector3 world)
    {
        Vector3 l = world - transform.position;
        return Mathf.Sqrt(l.x * l.x + l.z * l.z);
    }

    // Burasi lav golunun icinde mi (arazi lav seviyesinin altinda)?
    public bool IsLava(Vector3 world, float shrink = 0f)
    {
        if (PlanarDist(world) > poolRadius - shrink) return false;
        return TerrainHeight(world) < baseY - 0.15f;
    }

    // Yuzeyin dunya yuksekligi: lav yuzeyi veya arazi (hangisi yuksekse)
    public float HeightAt(Vector3 world)
    {
        float th = TerrainHeight(world);
        if (PlanarDist(world) > poolRadius) return th;
        Vector2 g = ToGrid(world);
        float surface = baseY - Sample(dig, g) * digDepth + CrustShape(Sample(crust, g)) * crustHeight;
        return Mathf.Max(th, surface);
    }

    // Isini yuzeyle kesistirir
    public bool Raycast(Ray ray, float maxDist, out Vector3 hit)
    {
        float step = 0.08f;
        Vector3 prev = ray.origin;
        for (float d = 0f; d < maxDist; d += step)
        {
            Vector3 p = ray.origin + ray.direction * d;
            if (p.y <= HeightAt(p))
            {
                Vector3 a = prev, b = p;
                for (int i = 0; i < 5; i++)
                {
                    Vector3 mid = (a + b) * 0.5f;
                    if (mid.y <= HeightAt(mid)) b = mid; else a = mid;
                }
                hit = b;
                return true;
            }
            prev = p;
        }
        hit = Vector3.zero;
        return false;
    }

    // ---------- Boyama ----------

    delegate void Brush(int i, float w);

    void Paint(Vector3 world, float radius, Brush brush)
    {
        Vector2 g = ToGrid(world);
        float cell = size / (res - 1);
        int rad = Mathf.CeilToInt(radius / cell);
        int cx = Mathf.RoundToInt(g.x), cy = Mathf.RoundToInt(g.y);
        for (int y = cy - rad; y <= cy + rad; y++)
        {
            if (y < 0 || y >= res) continue;
            for (int x = cx - rad; x <= cx + rad; x++)
            {
                if (x < 0 || x >= res) continue;
                float d = Mathf.Sqrt((x - g.x) * (x - g.x) + (y - g.y) * (y - g.y)) * cell;
                if (d >= radius) continue;
                float w = 1f - d / radius;
                w = w * w * (3f - 2f * w);
                Vector3 wp = transform.position + new Vector3((x / (float)(res - 1) - 0.5f) * size, 0f, (y / (float)(res - 1) - 0.5f) * size);
                if (!IsLava(wp, 1.5f)) continue;
                brush(y * res + x, w);
            }
        }
        dirty = true;
    }

    // Su damlasi dustugu yerde kabuk olusturur
    public void AddCrust(Vector3 world, float radius, float amount)
    {
        Paint(world, radius, (i, w) =>
        {
            if (dig[i] > 0.97f) return;
            crust[i] = Mathf.Min(1f, crust[i] + amount * w);
        });
    }

    // Kazma vurusu: kabugu yipratir, kirilinca asagiya bir kademe kazar
    public MineResult Mine(Vector3 world, float radius = 0.6f)
    {
        float center = CrustAt(world);
        if (center < 0.45f) return MineResult.None;
        Paint(world, radius, (i, w) => crust[i] = Mathf.Max(0f, crust[i] - 0.34f * w));
        if (CrustAt(world) >= 0.3f) return MineResult.Chipped;

        Paint(world, radius * 1.15f, (i, w) =>
        {
            crust[i] = Mathf.Max(0f, crust[i] - w * 1.2f);
            dig[i] = Mathf.Min(1f, dig[i] + digStep * w);
        });
        return MineResult.Broken;
    }

    // ---------- Durum ----------

    // Kazilan derinligin ortalamasi (0..1); sik hesaplanmasin diye onbellekli
    public float Progress
    {
        get
        {
            if (Time.unscaledTime - progressTime < 0.3f) return progressCache;
            progressTime = Time.unscaledTime;
            float sum = 0f; int n = 0;
            float rr = poolRadius - 6f;
            for (int y = 0; y < res; y += 4)
                for (int x = 0; x < res; x += 4)
                {
                    float wx = (x / (float)(res - 1) - 0.5f) * size, wz = (y / (float)(res - 1) - 0.5f) * size;
                    if (wx * wx + wz * wz > rr * rr) continue;
                    sum += dig[y * res + x]; n++;
                }
            progressCache = n == 0 ? 0f : sum / n;
            return progressCache;
        }
    }

    // Kabarcik/damla icin oyuncunun yakinindan acik lav noktasi
    public bool RandomLavaPoint(Vector3 around, float radius, out Vector3 p)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector2 o = Random.insideUnitCircle * radius;
            Vector3 w = new Vector3(around.x + o.x, baseY, around.z + o.y);
            if (!IsLava(w, 2f)) continue;
            Vector2 g = ToGrid(w);
            if (Sample(crust, g) < 0.3f && Sample(dig, g) < 0.9f)
            {
                p = new Vector3(w.x, HeightAt(w), w.z);
                return true;
            }
        }
        p = Vector3.zero;
        return false;
    }
}

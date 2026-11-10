using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

public enum MineResult { None, Chipped, Broken, AlreadySoft }

// Kraterin agzina kadar dolu, akiskan lav golu. CPU'da uc katman tutar:
//   crust: lavin ustundeki obsidyen kabugu (0..1)
//   dig:   lav yuzeyinin baslangic seviyesinin (dunya y = transform.position.y) altina inme miktari (0..1 = 0..digDepth metre)
//   bed:   gol tabaninin ayni olcekte derinligi (0 = arazi lav seviyesinin ustunde, 1 = digDepth ve altinda)
// Lav bir sivi gibi davranir: yuzey seviyesi farki kadar komsu hucrelere akar (viskoz, yavas), hacim korunur.
// Kirilan her obsidyen parcasi lavdan hacim ceker ("breaksToEmpty" kirilista golun tamami biter);
// kazilan cukur komsu lavla dolar, genel seviye yavasca duser, sigi kiyilar kuruyup arazi gorunur.
// Hepsi bir dokuya yazilir (r crust, g dig, b gol maskesi, a bed); shader yuzeyi bu dokudan sekillendirir.
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class LavaField : MonoBehaviour
{
    public static LavaField I;

    public Material material;
    public TextAsset basinData;          // res*res bayt: 255 = lav golu icinde
    public int res = 1024;
    public int meshRes = 512;
    public float size = 200f;
    public float digDepth = 12f;
    public float crustHeight = 0.14f;
    public float digStep = 0.1f;

    [Header("Akiskanlik")]
    [Range(0.02f, 0.24f)] public float flowRate = 0.14f;   // kucuk = daha viskoz (bal gibi), 0.24 stabilite siniri
    public int breaksToEmpty = 1200;                       // kirilan obsidyen sayisi: golun tum hacmi bu kadar parcayla biter

    const float Tick = 1f / 30f;
    const float DryDepth = 0.004f;       // bu derinligin altinda hucre kuru sayilir (~5 cm)

    NativeArray<float> crust;
    NativeArray<float> dig;
    NativeArray<float> bed;
    NativeArray<float> digNext;
    NativeArray<byte> basin;
    NativeArray<byte> basinSoft;
    NativeArray<Unity.Mathematics.half4> packed;
    // Katmanlar: suyla olusan obsidyenin kullanilabilir kalinligi (kabuk 1 -> 0.3) = FullBytes = 9 kasik katmani.
    // Derinlikler "bayt" biriminde (ScrapeMarks dokusuyla ayni, ustten mutlak derinlik).
    //   broken: bu dongudeki kirma ilerlemesi 0..1 (catlaklar)
    //   soft:   kirici ile ufalanmis derinlik siniri; kaziyici bu derinlige kadar inebilir
    //   cut:    hucrede kazinan ortalama derinlik; cutMax: hucredeki en derin texel
    NativeArray<float> broken, soft, cut, cutMax;
    NativeArray<byte> brokenPacked;
    Texture2D brokenTex;
    bool brokenDirty;
    float brokenUploadT;
    public const float UsableDepth = 0.7f;     // kabuk 1.0 -> 0.3 (lav esigi)
    public const float FullBytes = 252f;       // kullanilabilir kalinligin tamami
    public const float LayerBytes = FullBytes / 9f;   // bir kasik katmani
    const float CrustPerBreak = 16f;           // eski "bir kirilis" hacmi (kabuk birimi): lav cekilmesi bununla olceklenir
    int basinCount;
    Texture2D maskTex;
    bool dirty;
    Material matInstance;
    Terrain terrain;
    float baseY;

    float bedSum;            // tum golun hacmi (dig birimi)
    int wetCount;
    float digSum;
    int refCell;
    float pendingDrain;
    float activeTimer;
    float tickAcc;
    float statsTimer;

    public float BaseY => baseY;
    public NativeArray<float> CrustNA => crust;
    public NativeArray<float> DigNA => dig;
    public NativeArray<byte> BasinNA => basin;

    void Awake()
    {
        I = this;
        baseY = transform.position.y;
        terrain = FindAnyObjectByType<Terrain>();
        int n2 = res * res;
        crust = new NativeArray<float>(n2, Allocator.Persistent);
        dig = new NativeArray<float>(n2, Allocator.Persistent);
        bed = new NativeArray<float>(n2, Allocator.Persistent);
        digNext = new NativeArray<float>(n2, Allocator.Persistent);
        basin = new NativeArray<byte>(n2, Allocator.Persistent);
        basinSoft = new NativeArray<byte>(n2, Allocator.Persistent);
        packed = new NativeArray<Unity.Mathematics.half4>(n2, Allocator.Persistent);
        broken = new NativeArray<float>(n2, Allocator.Persistent);
        soft = new NativeArray<float>(n2, Allocator.Persistent);
        cut = new NativeArray<float>(n2, Allocator.Persistent);
        cutMax = new NativeArray<float>(n2, Allocator.Persistent);
        brokenPacked = new NativeArray<byte>(n2 * 2, Allocator.Persistent);
        if (basinData != null)
        {
            var bytes = basinData.bytes;
            int n = Mathf.Min(bytes.Length, basin.Length);
            for (int i = 0; i < n; i++) { basin[i] = bytes[i]; if (bytes[i] != 0) basinCount++; }
        }
        BuildSoftBasin();
        BuildBed();
        RefreshStats();

        maskTex = new Texture2D(res, res, TextureFormat.RGBAHalf, false, true)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        matInstance = new Material(material);
        matInstance.SetTexture("_Mask", maskTex);
        brokenTex = new Texture2D(res, res, TextureFormat.RG16, false, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        matInstance.SetTexture("_Broken", brokenTex);
        brokenDirty = true;
        matInstance.SetFloat("_FieldSize", size);
        matInstance.SetFloat("_CrustHeight", crustHeight);
        matInstance.SetFloat("_DigDepth", digDepth);
        GetComponent<MeshRenderer>().sharedMaterial = matInstance;
        GetComponent<MeshFilter>().sharedMesh = BuildMesh();
        dirty = true;
        Upload();
        UploadBroken();
    }

    void OnDestroy()
    {
        if (crust.IsCreated) crust.Dispose();
        if (dig.IsCreated) dig.Dispose();
        if (bed.IsCreated) bed.Dispose();
        if (digNext.IsCreated) digNext.Dispose();
        if (basin.IsCreated) basin.Dispose();
        if (basinSoft.IsCreated) basinSoft.Dispose();
        if (packed.IsCreated) packed.Dispose();
        if (broken.IsCreated) broken.Dispose();
        if (soft.IsCreated) soft.Dispose();
        if (cut.IsCreated) cut.Dispose();
        if (cutMax.IsCreated) cutMax.Dispose();
        if (brokenPacked.IsCreated) brokenPacked.Dispose();
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

    // Havuz maskesini bulaniklastirir: lav kiyisi basamak basamak degil, puruzsuz bir egri olur
    void BuildSoftBasin()
    {
        int n = res;
        var a = new float[n * n];
        var b = new float[n * n];
        for (int i = 0; i < a.Length; i++) a[i] = basin[i] != 0 ? 1f : 0f;
        const int r = 3;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int y = 0; y < n; y++)          // yatay
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += a[y * n + Mathf.Clamp(k, 0, n - 1)];
                for (int x = 0; x < n; x++)
                {
                    b[y * n + x] = sum / (2 * r + 1);
                    sum += a[y * n + Mathf.Min(x + r + 1, n - 1)] - a[y * n + Mathf.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < n; x++)          // dikey
            {
                float sum = 0f;
                for (int k = -r; k <= r; k++) sum += b[Mathf.Clamp(k, 0, n - 1) * n + x];
                for (int y = 0; y < n; y++)
                {
                    a[y * n + x] = sum / (2 * r + 1);
                    sum += b[Mathf.Min(y + r + 1, n - 1) * n + x] - b[Mathf.Max(y - r, 0) * n + x];
                }
            }
        }
        for (int i = 0; i < a.Length; i++) basinSoft[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(a[i] * 255f), 0, 255);
    }

    // Gol tabani: arazi yuksekliginden (en fazla digDepth metre derinlige kadar)
    void BuildBed()
    {
        bedSum = 0f;
        float bestScore = float.MaxValue;
        refCell = (res / 2) * res + res / 2;
        float cell = size / (res - 1);
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                int i = y * res + x;
                if (basin[i] == 0) { bed[i] = 0f; continue; }
                Vector3 wp = transform.position + new Vector3((x / (float)(res - 1) - 0.5f) * size, 0f, (y / (float)(res - 1) - 0.5f) * size);
                float th = TerrainHeight(wp);
                if (th >= baseY - 0.15f) { bed[i] = 0f; continue; }
                float b = Mathf.Clamp01((baseY - th) / digDepth);
                bed[i] = b;
                bedSum += b;
                // referans hucre: merkeze en yakin tam derin hucre (HUD seviye gostergesi)
                if (b > 0.99f)
                {
                    float dx = x - res * 0.5f, dy = y - res * 0.5f;
                    float s = dx * dx + dy * dy;
                    if (s < bestScore) { bestScore = s; refCell = i; }
                }
            }
    }

    // ---------- Akis simulasyonu ----------

    [BurstCompile]
    struct FlowJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> dig, bed;
        public NativeArray<float> next;
        public int res;
        public float c;          // akis katsayisi (hucre basina, adim basina)
        public float drain;      // bu adimda her islak hucreden cekilen hacim

        public void Execute(int i)
        {
            int x = i % res, y = i / res;
            float di = dig[i];
            float bi = bed[i];
            float depthI = bi - di;
            float delta = 0f;
            for (int k = 0; k < 4; k++)
            {
                int j;
                if (k == 0) { if (x == 0) continue; j = i - 1; }
                else if (k == 1) { if (x == res - 1) continue; j = i + 1; }
                else if (k == 2) { if (y == 0) continue; j = i - res; }
                else { if (y == res - 1) continue; j = i + res; }
                float dj = dig[j];
                float diff = dj - di;
                if (diff > 0f)          // j'nin seviyesi daha dusuk: i lav verir
                    delta += Mathf.Min(c * diff, 0.25f * Mathf.Min(depthI, dj));
                else if (diff < 0f)     // j'nin seviyesi daha yuksek: j lav verir
                    delta -= Mathf.Min(-c * diff, 0.25f * Mathf.Min(bed[j] - dj, di));
            }
            float v = di + delta;
            if (depthI > 0.004f) v += Mathf.Min(drain, depthI * 0.5f);
            next[i] = Mathf.Clamp(v, 0f, bi);
        }
    }

    [BurstCompile]
    struct StatsJob : IJob
    {
        [ReadOnly] public NativeArray<float> dig, bed;
        public NativeArray<float> outp;     // 0: islak hucre sayisi, 1: toplam dig (gol hucrelerinde)
        public void Execute()
        {
            float wet = 0f, sum = 0f;
            for (int i = 0; i < dig.Length; i++)
            {
                float b = bed[i];
                if (b <= 0f) continue;
                sum += dig[i];
                if (b - dig[i] > 0.004f) wet += 1f;
            }
            outp[0] = wet; outp[1] = sum;
        }
    }

    [BurstCompile]
    struct PackJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> crust, dig, bed;
        [ReadOnly] public NativeArray<byte> soft;
        public NativeArray<Unity.Mathematics.half4> outp;
        public void Execute(int i)
        {
            outp[i] = new Unity.Mathematics.half4(new Unity.Mathematics.float4(crust[i], dig[i], soft[i] * (1f / 255f), bed[i]));
        }
    }

    [BurstCompile]
    struct PackBrokenJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> src, soft;
        [NativeDisableParallelForRestriction] public NativeArray<byte> outp;
        public void Execute(int i)
        {
            outp[i * 2] = (byte)Unity.Mathematics.math.clamp((int)(src[i] * 255f + 0.5f), 0, 255);
            outp[i * 2 + 1] = (byte)Unity.Mathematics.math.clamp((int)(soft[i] + 0.5f), 0, 255);
        }
    }

    void RefreshStats()
    {
        var o = new NativeArray<float>(2, Allocator.TempJob);
        new StatsJob { dig = dig, bed = bed, outp = o }.Run();
        wetCount = Mathf.RoundToInt(o[0]);
        digSum = o[1];
        o.Dispose();
    }

    void Step()
    {
        float take = pendingDrain * (1f - Mathf.Exp(-Tick / 1.2f));
        pendingDrain -= take;
        if (pendingDrain < 1e-3f) { take += pendingDrain; pendingDrain = 0f; }
        float per = wetCount > 0 ? take / wetCount : 0f;
        new FlowJob { dig = dig, bed = bed, next = digNext, res = res, c = flowRate, drain = per }
            .Schedule(res * res, 8192).Complete();
        NativeArray<float>.Copy(digNext, dig);
        dirty = true;
    }

    void Update()
    {
        if (pendingDrain > 1e-4f) activeTimer = Mathf.Max(activeTimer, 6f);
        if (activeTimer <= 0f) return;
        float dt = Time.deltaTime;
        activeTimer -= dt;
        if (activeTimer <= 0f) { RefreshStats(); return; }
        tickAcc += dt;
        int steps = 0;
        while (tickAcc >= Tick && steps < 3) { tickAcc -= Tick; steps++; Step(); }
        if (tickAcc > Tick * 3f) tickAcc = 0f;

        statsTimer -= dt;
        if (statsTimer <= 0f) { statsTimer = 0.5f; RefreshStats(); }
    }

    void LateUpdate()
    {
        if (dirty) Upload();
        // ufalanma dokusu en fazla saniyede ~12 kez yuklenir (kazirken her kare yukleme takilmaya yol aciyordu)
        if (brokenDirty && Time.unscaledTime >= brokenUploadT) UploadBroken();
    }

    void UploadBroken()
    {
        new PackBrokenJob { src = broken, soft = soft, outp = brokenPacked }.Schedule(res * res, 8192).Complete();
        brokenTex.SetPixelData(brokenPacked, 0);
        brokenTex.Apply(false);
        brokenDirty = false;
        brokenUploadT = Time.unscaledTime + 0.08f;
    }

    void Upload()
    {
        new PackJob { crust = crust, dig = dig, bed = bed, soft = basinSoft, outp = packed }
            .Schedule(res * res, 8192).Complete();
        maskTex.SetPixelData(packed, 0);
        maskTex.Apply(false);
        dirty = false;
    }

    // ---------- Koordinat ----------

    Vector2 ToGrid(Vector3 world)
    {
        Vector3 l = world - transform.position;
        return new Vector2((l.x / size + 0.5f) * (res - 1), (l.z / size + 0.5f) * (res - 1));
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
    public float BrokenAt(Vector3 world) { return Sample(broken, ToGrid(world)); }

    public float SoftBytesAt(Vector3 world) { return Sample(soft, ToGrid(world)); }
    public float CellArea { get { float c = size / (res - 1); return c * c; } }

    // Bu noktadaki kullanilabilir obsidyen kalinligi (bayt; FullBytes = 9 kasik katmani)
    public float UsableBytesAt(Vector3 world)
    {
        float c = Sample(crust, ToGrid(world));
        return c < 0.3f ? 0f : (c - 0.3f) / UsableDepth * FullBytes;
    }

    // Kaziyici bu texelde en fazla bu derinlige inebilir: ufalanmis sinir ve obsidyenin dibi
    public float ScrapeLimitBytes(Vector3 world)
    {
        Vector2 g = ToGrid(world);
        float c = Sample(crust, g);
        if (c < 0.3f) return 0f;
        return Mathf.Min(Sample(soft, g), (c - 0.3f) / UsableDepth * FullBytes);
    }

    int CellIndex(Vector3 world)
    {
        Vector2 g = ToGrid(world);
        int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
        if (x < 0 || y < 0 || x >= res || y >= res) return -1;
        return y * res + x;
    }

    float UsableBytes(int i) { return crust[i] < 0.3f ? 0f : (crust[i] - 0.3f) / UsableDepth * FullBytes; }

    // Bu noktadaki lav derinligi (0..1 olcek)
    float DepthN(Vector2 g) { return Sample(bed, g) - Sample(dig, g); }

    public float TerrainHeight(Vector3 world)
    {
        if (terrain == null) return baseY - 1000f;
        return terrain.SampleHeight(world) + terrain.transform.position.y;
    }

    bool InBasin(Vector3 world)
    {
        Vector2 g = ToGrid(world);
        int x = Mathf.RoundToInt(g.x), y = Mathf.RoundToInt(g.y);
        if (x < 0 || y < 0 || x >= res || y >= res) return false;
        return basin[y * res + x] != 0;
    }

    // Burasi lav mi? (gol icinde ve tabanin ustunde lav var)
    public bool IsLava(Vector3 world, float shrink = 0f)
    {
        if (!InBasin(world)) return false;
        if (TerrainHeight(world) >= baseY - 0.15f) return false;
        if (DepthN(ToGrid(world)) < DryDepth) return false;
        if (shrink > 0f)
        {
            if (!InBasin(world + new Vector3(shrink, 0f, 0f)) || !InBasin(world - new Vector3(shrink, 0f, 0f)) ||
                !InBasin(world + new Vector3(0f, 0f, shrink)) || !InBasin(world - new Vector3(0f, 0f, shrink))) return false;
        }
        return true;
    }

    // Yuzeyin dunya yuksekligi: lav yuzeyi veya arazi (hangisi yuksekse)
    public float HeightAt(Vector3 world)
    {
        float th = TerrainHeight(world);
        if (!InBasin(world)) return th;
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

    void Paint(Vector3 world, float radius, Brush brush, bool crustChanged = true)
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
                int idx = y * res + x;
                if (basin[idx] == 0 || bed[idx] <= 0f) continue;
                brush(idx, w);
            }
        }
        if (crustChanged) dirty = true;
    }

    // Su parcacigi dustugu yerde kabuk olusturur
    public void AddCrust(Vector3 world, float radius, float amount)
    {
        Paint(world, radius, (i, w) =>
        {
            if (bed[i] - dig[i] < 0.03f) return;      // kuru/cok sigi: kabuk olusmaz
            // kazinmis kalinlik dusulur, yeni su ustune obsidyen ekler; ufalanma ve kazima izi sifirlanir (taze tabaka)
            float eff = crust[i] >= 0.3f ? crust[i] - cut[i] / FullBytes * UsableDepth : crust[i];
            crust[i] = Mathf.Min(1f, eff + amount * w);
            if (cut[i] > 0f || soft[i] > 0f || broken[i] > 0f)
            {
                cut[i] = 0f; cutMax[i] = 0f; soft[i] = 0f; broken[i] = 0f;
                brokenDirty = true;
            }
        });
        if (ScrapeMarks.I != null) ScrapeMarks.I.Clear(world, radius);
    }

    // Kirici vurusu: kabugu DELMEZ ve parcalamaz; vurdukca catlatir (broken 0..1). Tam kirilinca ust tabakadan
    // 'depthLayers' kasik katmani kadar derinlik ufalanir (soft); kaziyici yalnizca bu derinlige kadar kazir.
    // Ufalanan kisim kazinmadan tekrar kirilamaz; kazininca bir sonraki katman grubu kirilir.
    public MineResult Mine(Vector3 world, float radius = 0.6f, float power = 0.34f, float depthLayers = 3f)
    {
        int c0 = CellIndex(world);
        if (c0 < 0 || CrustAt(world) < 0.3f || crust[c0] < 0.3f) return MineResult.None;       // kabuk yok: acik lav
        float depthB = depthLayers * LayerBytes;
        // ufalanabilecek en derin nokta: hucrenin en derin kazinan yerinin 'depthLayers' alti (obsidyenin dibini asmaz).
        // Ufalanan katmanlar kazinmadan daha derine inilemez.
        if (soft[c0] >= Mathf.Min(UsableBytes(c0), cutMax[c0] + depthB) - 0.5f) return MineResult.AlreadySoft;
        float before = broken[c0] >= 0.999f ? 0f : broken[c0];
        float add = power * 1.6f;
        Paint(world, radius, (i, w) =>
        {
            if (crust[i] < 0.3f) return;
            float full = Mathf.Min(UsableBytes(i), cutMax[i] + depthB);
            if (soft[i] >= full - 0.5f) return;                // bu derinlik zaten ufalandi
            if (broken[i] >= 0.999f) broken[i] = 0f;            // kazinmis yerde yeni dongu: alttaki katmanlar
            broken[i] = Mathf.Min(1f, broken[i] + add * w);
            float f = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.95f, broken[i]));
            float target = Mathf.Min(full, cutMax[i] + depthB * f);
            if (target > soft[i]) soft[i] = target;
        }, false);
        brokenDirty = true;
        return before < 0.9f && broken[c0] >= 0.9f ? MineResult.Broken : MineResult.Chipped;
    }

    // ScrapeMarks bir texeli oldB -> newB derinligine kazidi: hucrenin kazima hesabi, lav cekilmesi ve
    // obsidyen bitince lavin acilmasi. Dondurulen: bu hucrede lav acildi mi.
    public bool AddCut(Vector3 wp, int oldB, int newB, float areaRatio)
    {
        int i = CellIndex(wp);
        if (i < 0 || crust[i] < 0.3f) return false;
        float d = (newB - oldB) * areaRatio;
        cut[i] += d;
        if (newB > cutMax[i]) cutMax[i] = newB;
        pendingDrain += bedSum / Mathf.Max(1, breaksToEmpty) * (d / FullBytes * UsableDepth / CrustPerBreak);
        // hucrenin cogu dibe kadar kazininca obsidyen biter: lav acilir
        if (cut[i] >= UsableBytes(i) * 0.4f && cutMax[i] >= UsableBytes(i) - 1f)
        {
            crust[i] = 0f; cut[i] = 0f; cutMax[i] = 0f; soft[i] = 0f; broken[i] = 0f;
            dirty = true; brokenDirty = true;
            return true;
        }
        return false;
    }

    // Kasik/spatula: yalnizca ust tabakayi ince ince alir; kabuk 'floor' altina inmez (asla delmez, lav acilmaz).
    // Dondurulen: merkezdeki agirlikli ortalama alinan miktar.
    public float Skim(Vector3 world, float radius, float amount, float floor = 0.55f)
    {
        float removed = 0f, wsum = 0f;
        Paint(world, radius, (i, w) =>
        {
            wsum += w;
            float c = crust[i];
            if (c <= floor) return;
            float dlt = Mathf.Min(amount * w, c - floor);
            crust[i] = c - dlt;
            removed += dlt;
        });
        return wsum > 0f ? removed / wsum : 0f;
    }

    // ---------- Durum ----------

    // Golun bosalan hacim orani (0..1)
    public float Progress => bedSum > 0f ? Mathf.Clamp01(digSum / bedSum) : 0f;

    // Lavin merkezdeki seviyesi baslangica gore (metre, negatif = dusmus)
    public float LavaLevel => -dig[refCell] * digDepth;

    // Kabarcik/damla icin oyuncunun yakinindan acik lav noktasi
    public bool RandomLavaPoint(Vector3 around, float radius, out Vector3 p)
    {
        for (int i = 0; i < 20; i++)
        {
            Vector2 o = Random.insideUnitCircle * radius;
            Vector3 w = new Vector3(around.x + o.x, baseY, around.z + o.y);
            if (!IsLava(w, 2f)) continue;
            Vector2 g = ToGrid(w);
            if (Sample(crust, g) < 0.3f && DepthN(g) > 0.03f)
            {
                p = new Vector3(w.x, HeightAt(w), w.z);
                return true;
            }
        }
        p = Vector3.zero;
        return false;
    }
}

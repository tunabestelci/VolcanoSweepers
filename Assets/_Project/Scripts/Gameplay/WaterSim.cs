using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Parcacik tabanli gercek su akiskani: Position Based Fluids (Macklin & Muller 2013).
// - Her parcacik bir su kutlesi; yogunluk kisiti ile sikismaz sivi gibi davranir (basinc, yayilma, birikme).
// - Burst job: spatial hash ile komsu arama, 3 yinelemeli yogunluk cozumu, XSPH viskozite.
// - Zemin/lav: yukseklik alani (arazi + lav yuzeyi); lava dusen su kabuk olusturur ve buharlasir.
// - Kapli kaplar (buyuk kova) ve kuyu govdesi kendi cisim olarak modellenir; icine dusen su toplanir.
// - Hizli carpmalarda ikincil sicrama damlalari uretilir.
public struct WaterObstacle
{
    public float3 c;
    public float rIn, rOut, y0, y1;
    public int kind;    // 1: ust acik kap (icine dusen su toplanir), 2: kuyu (delik + tas halka)
    public int id;
    public float funnel;   // kap agzinin ustunde de su toplanan yukseklik (disari dokulmesin)
}

public struct WaterSimParams
{
    public float dt, h, rho0, eps, poly6, spiky, scorrK, wdq, xsph, radius, friction;
    public int iterations;
    public float baseY, size, digDepth, crustH;
    public int res;
    public float3 lavaC;
    public float gridOx, gridOz, gridCell;
    public int gridN;
    public int obsCount;
    public uint seed;
    public float lowest;      // bu yuksekligin altina inen parcacik oldurulur
}

public class WaterSim : MonoBehaviour
{
    public static WaterSim I;

    public Mesh mesh;
    public Material material;

    public const int Capacity = 2600;
    public const float Spacing = 0.075f;
    public const float H = 0.17f;
    const int Table = 8192;
    const int MaxNbr = 48;
    const int MaxObs = 8;
    const int MaxEvents = 512;

    NativeArray<float3> pos, vel, pred, vnew, dp;
    NativeArray<float> lambda, life, impact;
    NativeArray<byte> kind, contact;
    NativeArray<int3> cellOf;
    NativeArray<int> count, cellStart, cellFill, sorted, nbr, nbrCount;
    NativeArray<float> terrain;
    NativeArray<WaterObstacle> obs;
    NativeArray<float4> evtPos;
    NativeArray<int2> evtInfo;
    NativeArray<int> evtCount;
    NativeArray<float> dummy;
    NativeArray<float> crustNA, digNA;
    NativeArray<byte> basinNA;
    bool ownsMasks;

    WaterSimParams P;
    bool ready;
    float accumulator;
    uint frame;
    float steamCooldown;
    float sizzleCooldown;

    readonly List<BigBucket> containers = new List<BigBucket>();
    Matrix4x4[] batch = new Matrix4x4[1023];

    public int Alive => ready ? count[0] : 0;
    public float crustPerParticle = 0.1f;

    // ---------------- kurulum ----------------

    void Awake()
    {
        I = this;
        AllocateAll();
    }

    void AllocateAll()
    {
        if (pos.IsCreated) return;
        pos = new NativeArray<float3>(Capacity, Allocator.Persistent);
        vel = new NativeArray<float3>(Capacity, Allocator.Persistent);
        pred = new NativeArray<float3>(Capacity, Allocator.Persistent);
        vnew = new NativeArray<float3>(Capacity, Allocator.Persistent);
        dp = new NativeArray<float3>(Capacity, Allocator.Persistent);
        lambda = new NativeArray<float>(Capacity, Allocator.Persistent);
        life = new NativeArray<float>(Capacity, Allocator.Persistent);
        impact = new NativeArray<float>(Capacity, Allocator.Persistent);
        kind = new NativeArray<byte>(Capacity, Allocator.Persistent);
        contact = new NativeArray<byte>(Capacity, Allocator.Persistent);
        cellOf = new NativeArray<int3>(Capacity, Allocator.Persistent);
        count = new NativeArray<int>(1, Allocator.Persistent);
        cellStart = new NativeArray<int>(Table + 1, Allocator.Persistent);
        cellFill = new NativeArray<int>(Table, Allocator.Persistent);
        sorted = new NativeArray<int>(Capacity, Allocator.Persistent);
        nbr = new NativeArray<int>(Capacity * MaxNbr, Allocator.Persistent);
        nbrCount = new NativeArray<int>(Capacity, Allocator.Persistent);
        obs = new NativeArray<WaterObstacle>(MaxObs, Allocator.Persistent);
        evtPos = new NativeArray<float4>(MaxEvents, Allocator.Persistent);
        evtInfo = new NativeArray<int2>(MaxEvents, Allocator.Persistent);
        evtCount = new NativeArray<int>(1, Allocator.Persistent);
        dummy = new NativeArray<float>(4, Allocator.Persistent);
    }

    void Start()
    {
        if (ready) return;
        var field = LavaField.I;
        if (field == null) { Debug.LogWarning("[WaterSim] LavaField yok"); return; }
        SetupFields(field);
    }

    void SetupFields(LavaField field)
    {
        // Arazi yukseklik izgarasi (statik)
        float half = field.size * 0.5f + 2f;
        const float cell = 0.5f;
        int n = Mathf.CeilToInt(half * 2f / cell) + 1;
        terrain = new NativeArray<float>(n * n, Allocator.Persistent);
        Vector3 c = field.transform.position;
        float ox = c.x - half, oz = c.z - half;
        for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                terrain[z * n + x] = field.TerrainHeight(new Vector3(ox + x * cell, 0f, oz + z * cell));

        crustNA = field.CrustNA;
        digNA = field.DigNA;
        basinNA = field.BasinNA;

        InitParams();
        P.baseY = field.BaseY;
        P.size = field.size;
        P.digDepth = field.digDepth;
        P.crustH = field.crustHeight;
        P.res = field.res;
        P.lavaC = new float3(c.x, c.y, c.z);
        P.gridOx = ox; P.gridOz = oz; P.gridCell = cell; P.gridN = n;
        P.lowest = c.y - 40f;
        ready = true;
    }

    // Test icin: duz zemin, lav yok
    public void SetupFlat(float groundY)
    {
        I = this;
        AllocateAll();
        int n = 101;
        terrain = new NativeArray<float>(n * n, Allocator.Persistent);
        for (int i = 0; i < terrain.Length; i++) terrain[i] = groundY;
        crustNA = new NativeArray<float>(4, Allocator.Persistent);
        digNA = new NativeArray<float>(4, Allocator.Persistent);
        basinNA = new NativeArray<byte>(4, Allocator.Persistent);
        ownsMasks = true;
        InitParams();
        P.baseY = groundY - 1000f;
        P.size = 1f; P.res = 2; P.digDepth = 1f; P.crustH = 0f;
        P.gridOx = -10f; P.gridOz = -10f; P.gridCell = 0.2f; P.gridN = n;
        P.lowest = groundY - 50f;
        ready = true;
    }

    void InitParams()
    {
        P = new WaterSimParams();
        P.dt = 1f / 60f;
        P.h = H;
        P.poly6 = 315f / (64f * Mathf.PI * Mathf.Pow(H, 9));
        P.spiky = -45f / (Mathf.PI * Mathf.Pow(H, 6));
        // dinlenme yogunlugu: Spacing araliginda kafes
        float rho = 0f;
        for (int i = -3; i <= 3; i++)
            for (int j = -3; j <= 3; j++)
                for (int k = -3; k <= 3; k++)
                {
                    float r2 = (i * i + j * j + k * k) * Spacing * Spacing;
                    float d = Mathf.Max(H * H - r2, 0f);
                    rho += P.poly6 * d * d * d;
                }
        P.rho0 = rho;
        P.eps = 400f;
        P.scorrK = 0.002f;
        float dq = 0.2f * H;
        float dd = H * H - dq * dq;
        P.wdq = P.poly6 * dd * dd * dd;
        P.xsph = 0.15f;
        P.radius = Spacing * 0.5f;
        P.friction = 0.92f;
        P.iterations = 3;
    }

    void OnDestroy()
    {
        ready = false;
        Dispose(ref pos); Dispose(ref vel); Dispose(ref pred); Dispose(ref vnew); Dispose(ref dp);
        Dispose(ref lambda); Dispose(ref life); Dispose(ref impact);
        Dispose(ref kind); Dispose(ref contact);
        if (cellOf.IsCreated) cellOf.Dispose();
        Dispose(ref count); Dispose(ref cellStart); Dispose(ref cellFill); Dispose(ref sorted); Dispose(ref nbr); Dispose(ref nbrCount);
        Dispose(ref terrain);
        if (obs.IsCreated) obs.Dispose();
        if (evtPos.IsCreated) evtPos.Dispose();
        if (evtInfo.IsCreated) evtInfo.Dispose();
        Dispose(ref evtCount);
        Dispose(ref dummy);
        if (ownsMasks) { Dispose(ref crustNA); Dispose(ref digNA); Dispose(ref basinNA); }
        if (I == this) I = null;
    }

    static void Dispose<T>(ref NativeArray<T> a) where T : struct
    {
        if (a.IsCreated) a.Dispose();
    }

    // ---------------- cisimler ----------------

    public static int RegisterContainer(BigBucket bucket, Vector3 center, float rIn, float rOut, float y0, float y1, float funnel = 0.1f)
    {
        if (I == null || I.P.obsCount >= MaxObs) return -1;
        int id = I.containers.Count;
        I.containers.Add(bucket);
        I.obs[I.P.obsCount++] = new WaterObstacle { c = center, rIn = rIn, rOut = rOut, y0 = y0, y1 = y1, kind = 1, id = id, funnel = funnel };
        return id;
    }

    public static void RegisterWell(Vector3 center, float rIn, float rOut, float waterY, float rimY)
    {
        if (I == null || I.P.obsCount >= MaxObs) return;
        I.obs[I.P.obsCount++] = new WaterObstacle { c = center, rIn = rIn, rOut = rOut, y0 = waterY, y1 = rimY, kind = 2, id = 0 };
    }

    // ---------------- parcacik uretimi ----------------

    public static void Emit(Vector3 position, Vector3 velocity, float lifetime = 7f)
    {
        var s = I;
        if (s == null || !s.ready) return;
        int n = s.count[0];
        if (n >= Capacity - 16) return;
        s.pos[n] = position;
        s.vel[n] = velocity;
        s.pred[n] = position;
        s.kind[n] = 1;
        s.life[n] = lifetime;
        s.count[0] = n + 1;
    }

    // ---------------- adim ----------------

    void Update()
    {
        if (!ready) return;
        accumulator += Mathf.Min(Time.deltaTime, 0.05f);
        int steps = 0;
        while (accumulator >= P.dt && steps < 2)
        {
            accumulator -= P.dt;
            Step();
            steps++;
        }
        if (steps == 2) accumulator = 0f;
    }

    void LateUpdate()
    {
        if (ready) Render();
    }

    public void Step()
    {
        if (!ready) return;
        if (count[0] == 0) return;
        P.seed = ++frame * 747796405u + 12345u;
        evtCount[0] = 0;
        var job = new StepJob
        {
            P = P,
            pos = pos, vel = vel, pred = pred, vnew = vnew, dp = dp,
            lambda = lambda, life = life, impact = impact,
            kind = kind, contact = contact, cellOf = cellOf,
            count = count, cellStart = cellStart, cellFill = cellFill, sorted = sorted, nbr = nbr, nbrCount = nbrCount,
            terrain = terrain, crust = crustNA, dig = digNA, basin = basinNA, obs = obs,
            evtPos = evtPos, evtInfo = evtInfo, evtCount = evtCount,
        };
        job.Schedule().Complete();
        HandleEvents();
    }

    void HandleEvents()
    {
        int ec = Mathf.Min(evtCount[0], MaxEvents);
        if (ec == 0) return;
        var field = LavaField.I;
        int lavaHits = 0;
        Vector3 lavaSum = Vector3.zero;
        for (int e = 0; e < ec; e++)
        {
            var info = evtInfo[e];
            var ep = evtPos[e];
            var p = new Vector3(ep.x, ep.y, ep.z);
            switch (info.x)
            {
                case 1:   // lava
                    if (field != null) field.AddCrust(p, 0.32f, crustPerParticle);
                    lavaHits++;
                    lavaSum += p;
                    break;
                case 2:   // kap
                    if (info.y >= 0 && info.y < containers.Count && containers[info.y] != null) containers[info.y].AddParticle();
                    break;
                case 3:   // kuyu
                    if (UnityEngine.Random.value < 0.03f) Fx.Splash(p);
                    break;
            }
        }
        if (lavaHits >= 2 && Time.time >= sizzleCooldown && SfxPlayer.I != null)
        {
            SfxPlayer.PlaySnippet(SfxPlayer.I.lavaSizzle, lavaSum / lavaHits, 2.4f, Mathf.Clamp(0.3f + 0.04f * lavaHits, 0.3f, 0.9f));
            sizzleCooldown = Time.time + 1.6f;   // temiz: ust uste binmesin
        }
        if (lavaHits >= 4 && Time.time >= steamCooldown)
        {
            Fx.Steam(lavaSum / lavaHits, 0.22f + 0.015f * Mathf.Min(lavaHits, 10));
            steamCooldown = Time.time + 0.6f;
        }
    }

    // ---------------- cizim ----------------

    void Render()
    {
        if (mesh == null || material == null) return;
        int n = count[0];
        if (n == 0) return;
        var rp = new RenderParams(material)
        {
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = false,
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 4000f),
        };
        float dFluid = Spacing * 1.25f;
        int b = 0;
        for (int i = 0; i < n; i++)
        {
            byte k = kind[i];
            if (k == 0) continue;
            float s = k == 1 ? dFluid : dFluid * 0.42f;
            float l = life[i];
            if (l < 0.8f) s *= Mathf.Max(0.05f, l / 0.8f);
            float3 v = vel[i];
            float sp = math.length(v);
            Quaternion q = Quaternion.identity;
            float stretch = 1f;
            if (sp > 0.8f)
            {
                q = Quaternion.LookRotation(new Vector3(v.x, v.y, v.z) / sp);
                stretch = 1f + Mathf.Min(sp * 0.05f, 0.7f);
            }
            float3 p = pos[i];
            batch[b++] = Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), q, new Vector3(s, s, s * stretch));
            if (b == 1023)
            {
                Graphics.RenderMeshInstanced(rp, mesh, 0, batch, b);
                b = 0;
            }
        }
        if (b > 0) Graphics.RenderMeshInstanced(rp, mesh, 0, batch, b);
    }

    // ---------------- test yardimcilari ----------------

    public Vector3 PositionOf(int i) { var p = pos[i]; return new Vector3(p.x, p.y, p.z); }
    public Vector3 VelocityOf(int i) { var v = vel[i]; return new Vector3(v.x, v.y, v.z); }
    public int KindOf(int i) { return kind[i]; }

    // ---------------- Burst job ----------------

    [BurstCompile]
    struct StepJob : IJob
    {
        public WaterSimParams P;
        public NativeArray<float3> pos, vel, pred, vnew, dp;
        public NativeArray<float> lambda, life, impact;
        public NativeArray<byte> kind, contact;
        public NativeArray<int3> cellOf;
        public NativeArray<int> count, cellStart, cellFill, sorted, nbr, nbrCount;
        [Unity.Collections.ReadOnly] public NativeArray<float> terrain, crust, dig;
        [Unity.Collections.ReadOnly] public NativeArray<byte> basin;
        [Unity.Collections.ReadOnly] public NativeArray<WaterObstacle> obs;
        public NativeArray<float4> evtPos;
        public NativeArray<int2> evtInfo;
        public NativeArray<int> evtCount;

        float Poly6(float r2)
        {
            float d = P.h * P.h - r2;
            return d > 0f ? P.poly6 * d * d * d : 0f;
        }

        float3 SpikyGrad(float3 d, float r)
        {
            if (r >= P.h || r < 1e-6f) return float3.zero;
            float t = P.h - r;
            return d * (P.spiky * t * t / r);
        }

        static int Key(int3 c)
        {
            uint h = (uint)(c.x * 73856093) ^ (uint)(c.y * 19349663) ^ (uint)(c.z * 83492791);
            return (int)(h & (uint)(Table - 1));
        }

        float TerrainAt(float x, float z)
        {
            float gx = (x - P.gridOx) / P.gridCell;
            float gz = (z - P.gridOz) / P.gridCell;
            int n = P.gridN;
            gx = math.clamp(gx, 0f, n - 1.001f);
            gz = math.clamp(gz, 0f, n - 1.001f);
            int x0 = (int)gx, z0 = (int)gz;
            float fx = gx - x0, fz = gz - z0;
            float a = math.lerp(terrain[z0 * n + x0], terrain[z0 * n + x0 + 1], fx);
            float b = math.lerp(terrain[(z0 + 1) * n + x0], terrain[(z0 + 1) * n + x0 + 1], fx);
            return math.lerp(a, b, fz);
        }

        float SampleMask(NativeArray<float> a, float u, float v)
        {
            int res = P.res;
            float x = math.clamp(u, 0f, res - 1.001f), y = math.clamp(v, 0f, res - 1.001f);
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            float a0 = math.lerp(a[y0 * res + x0], a[y0 * res + x0 + 1], fx);
            float a1 = math.lerp(a[(y0 + 1) * res + x0], a[(y0 + 1) * res + x0 + 1], fx);
            return math.lerp(a0, a1, fy);
        }

        // Yuzey yuksekligi; lav ise lava = true
        float Surface(float x, float z, out bool lava)
        {
            float th = TerrainAt(x, z);
            lava = false;
            float dx = x - P.lavaC.x, dz = z - P.lavaC.z;
            float u = (dx / P.size + 0.5f) * (P.res - 1);
            float v = (dz / P.size + 0.5f) * (P.res - 1);
            int bx = (int)(u + 0.5f), by = (int)(v + 0.5f);
            if (bx < 0 || by < 0 || bx >= P.res || by >= P.res) return th;
            if (basin[by * P.res + bx] == 0 || th >= P.baseY - 0.15f) return th;
            float c = SampleMask(crust, u, v);
            float d = SampleMask(dig, u, v);
            float t = math.saturate((c - 0.3f) / 0.3f);
            float cs = t * t * (3f - 2f * t);
            float surf = P.baseY - d * P.digDepth + cs * P.crustH;
            lava = true;
            return math.max(th, surf);
        }

        void AddEvent(float3 p, float speed, int type, int id)
        {
            int e = evtCount[0];
            if (e >= MaxEvents) return;
            evtPos[e] = new float4(p.x, p.y, p.z, speed);
            evtInfo[e] = new int2(type, id);
            evtCount[0] = e + 1;
        }

        void Collide(int i)
        {
            if (kind[i] == 0) return;
            float3 p = pred[i];
            float rad = kind[i] == 1 ? P.radius : P.radius * 0.5f;
            float speed = math.length(vel[i]);
            bool inHole = false;

            for (int k = 0; k < P.obsCount; k++)
            {
                WaterObstacle o = obs[k];
                float dx = p.x - o.c.x, dz = p.z - o.c.z;
                float r = math.sqrt(dx * dx + dz * dz);
                if (o.kind == 1)
                {
                    if (p.y < o.y1 + o.funnel && p.y > o.y0 - rad)
                    {
                        if (r < o.rIn)
                        {
                            AddEvent(p, speed, 2, o.id);
                            kind[i] = 0;
                            return;
                        }
                        if (r < o.rOut && p.y < o.y1)
                        {
                            float nx = r > 1e-4f ? dx / r : 1f, nz = r > 1e-4f ? dz / r : 0f;
                            p.x = o.c.x + nx * o.rOut;
                            p.z = o.c.z + nz * o.rOut;
                            contact[i] = 1;
                        }
                    }
                }
                else if (o.kind == 2)
                {
                    if (r < o.rIn)
                    {
                        inHole = true;
                        if (p.y < o.y0)
                        {
                            AddEvent(p, speed, 3, 0);
                            kind[i] = 0;
                            return;
                        }
                    }
                    else if (r < o.rOut && p.y < o.y1 + rad)
                    {
                        if (p.y > o.y1 - 0.14f) { p.y = o.y1 + rad; }
                        else
                        {
                            float nx = dx / math.max(r, 1e-4f), nz = dz / math.max(r, 1e-4f);
                            p.x = o.c.x + nx * o.rOut;
                            p.z = o.c.z + nz * o.rOut;
                        }
                        contact[i] = 1;
                    }
                }
            }

            if (!inHole)
            {
                bool lava;
                float h = Surface(p.x, p.z, out lava);
                if (p.y < h + rad * 0.5f)
                {
                    if (lava)
                    {
                        AddEvent(new float3(p.x, h, p.z), speed, 1, 0);
                        kind[i] = 0;
                        return;
                    }
                    p.y = h + rad * 0.5f;
                    contact[i] = 1;
                    if (impact[i] < speed) impact[i] = speed;
                }
            }
            if (p.y < P.lowest) { kind[i] = 0; return; }
            pred[i] = p;
        }

        public void Execute()
        {
            int n = count[0];
            float dt = P.dt;

            // 1) Tahmin
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == 0) continue;
                float3 v = vel[i];
                v.y -= 9.81f * dt;
                vel[i] = v;
                pred[i] = pos[i] + v * dt;
                contact[i] = 0;
                impact[i] = 0f;
            }
            for (int i = 0; i < n; i++) Collide(i);

            // 2) Izgara (yalniz akiskan parcaciklar)
            for (int k = 0; k < Table; k++) cellFill[k] = 0;
            for (int i = 0; i < n; i++)
            {
                if (kind[i] != 1) continue;
                int3 c = (int3)math.floor(pred[i] / P.h);
                cellOf[i] = c;
                cellFill[Key(c)]++;
            }
            int acc = 0;
            for (int k = 0; k < Table; k++) { cellStart[k] = acc; acc += cellFill[k]; cellFill[k] = 0; }
            cellStart[Table] = acc;
            for (int i = 0; i < n; i++)
            {
                if (kind[i] != 1) continue;
                int k = Key(cellOf[i]);
                sorted[cellStart[k] + cellFill[k]] = i;
                cellFill[k]++;
            }

            // 3) Komsu listeleri
            float rr = P.h * 1.25f; rr *= rr;
            for (int i = 0; i < n; i++)
            {
                nbrCount[i] = 0;
                if (kind[i] != 1) continue;
                int3 c = cellOf[i];
                float3 pi = pred[i];
                int cnt = 0;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int3 c2 = c + new int3(dx, dy, dz);
                            int k = Key(c2);
                            int s0 = cellStart[k], s1 = cellStart[k + 1];
                            for (int s = s0; s < s1; s++)
                            {
                                int j = sorted[s];
                                if (j == i) continue;
                                int3 cj = cellOf[j];
                                if (cj.x != c2.x || cj.y != c2.y || cj.z != c2.z) continue;
                                float3 d = pi - pred[j];
                                if (math.dot(d, d) < rr && cnt < MaxNbr) nbr[i * MaxNbr + cnt++] = j;
                            }
                        }
                nbrCount[i] = cnt;
            }

            // 4) Yogunluk kisiti
            float w0 = Poly6(0f);
            float invRho0 = 1f / P.rho0;
            for (int it = 0; it < P.iterations; it++)
            {
                for (int i = 0; i < n; i++)
                {
                    if (kind[i] != 1) continue;
                    float3 pi = pred[i];
                    float rho = w0;
                    float3 gI = float3.zero;
                    float sg = 0f;
                    int cnt = nbrCount[i];
                    for (int q = 0; q < cnt; q++)
                    {
                        int j = nbr[i * MaxNbr + q];
                        float3 d = pi - pred[j];
                        float r2 = math.dot(d, d);
                        rho += Poly6(r2);
                        float3 g = SpikyGrad(d, math.sqrt(r2));
                        gI += g;
                        sg += math.dot(g, g);
                    }
                    gI *= invRho0;
                    sg = sg * invRho0 * invRho0 + math.dot(gI, gI);
                    float C = math.min(rho * invRho0 - 1f, 10f);
                    lambda[i] = -C / (sg + P.eps);
                }
                for (int i = 0; i < n; i++)
                {
                    if (kind[i] != 1) continue;
                    float3 pi = pred[i];
                    float3 sum = float3.zero;
                    int cnt = nbrCount[i];
                    for (int q = 0; q < cnt; q++)
                    {
                        int j = nbr[i * MaxNbr + q];
                        float3 d = pi - pred[j];
                        float r2 = math.dot(d, d);
                        float w = Poly6(r2) / P.wdq;
                        float w2 = w * w;
                        float scorr = -P.scorrK * w2 * w2;
                        sum += (lambda[i] + lambda[j] + scorr) * SpikyGrad(d, math.sqrt(r2));
                    }
                    float3 move = sum * invRho0;
                    float ml = math.length(move);
                    if (ml > 0.05f) move *= 0.05f / ml;     // tek yinelemede asiri itme olmasin
                    dp[i] = move;
                }
                for (int i = 0; i < n; i++)
                {
                    if (kind[i] != 1) continue;
                    pred[i] = pred[i] + dp[i];
                    Collide(i);
                }
            }

            // 5) Hiz guncelleme
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == 0) continue;
                float3 v = (pred[i] - pos[i]) / dt;
                if (contact[i] == 1) { v.x *= P.friction; v.z *= P.friction; }
                float sp = math.length(v);
                if (sp > 14f) v *= 14f / sp;
                vel[i] = v;
            }
            // XSPH viskozite
            for (int i = 0; i < n; i++)
            {
                if (kind[i] != 1) { vnew[i] = vel[i]; continue; }
                float3 vi = vel[i];
                float3 acc3 = float3.zero;
                float wsum = 1e-6f;
                int cnt = nbrCount[i];
                for (int q = 0; q < cnt; q++)
                {
                    int j = nbr[i * MaxNbr + q];
                    float3 d = pred[i] - pred[j];
                    float w = Poly6(math.dot(d, d));
                    acc3 += w * (vel[j] - vi);
                    wsum += w;
                }
                vnew[i] = vi + P.xsph * acc3 / wsum;
            }
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == 0) continue;
                vel[i] = vnew[i];
                pos[i] = pred[i];
                life[i] -= dt;
                if (life[i] <= 0f) kind[i] = 0;
            }

            // 6) Ikincil sicrama damlalari
            var rnd = new Unity.Mathematics.Random(P.seed | 1u);
            int n0 = n;
            int spawned = 0;
            for (int i = 0; i < n0 && spawned < 2; i++)
            {
                if (kind[i] != 1 || impact[i] < 3.6f) continue;
                for (int s = 0; s < 1; s++)
                {
                    if (n >= Capacity - 2) break;
                    float2 lat = rnd.NextFloat2(-1f, 1f) * impact[i] * 0.22f;
                    pos[n] = pos[i] + new float3(0f, 0.03f, 0f);
                    pred[n] = pos[n];
                    vel[n] = new float3(lat.x, impact[i] * 0.26f + rnd.NextFloat(0f, 1f), lat.y);
                    kind[n] = 2;
                    life[n] = 0.5f + rnd.NextFloat(0f, 0.6f);
                    n++;
                    spawned++;
                }
                impact[i] = 0f;
            }

            // 7) Olu parcaciklari at
            int w1 = 0;
            for (int i = 0; i < n; i++)
            {
                if (kind[i] == 0) continue;
                if (w1 != i)
                {
                    pos[w1] = pos[i]; vel[w1] = vel[i]; pred[w1] = pred[i];
                    kind[w1] = kind[i]; life[w1] = life[i];
                }
                w1++;
            }
            count[0] = w1;
        }
    }
}

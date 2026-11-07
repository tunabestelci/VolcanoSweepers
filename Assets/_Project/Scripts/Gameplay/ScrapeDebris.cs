using System.Collections.Generic;
using UnityEngine;

// Kazima aletinin kepce/bicak geometrisi (alet prefab'inin olceksiz yerel uzayi: +Z uca dogru, +Y kepcenin acik yuzu).
// Degerler Spoon/Spatula modellerinin vertekslerinden olculdu.
public struct ScrapeToolShape
{
    public float zCenter, halfLen, halfWidth;   // kepce bolgesi
    public float rimY, depth, thickness;        // ic yuzey: kenarda rimY, ortada rimY - depth
    public bool ellipse;                        // kasik: elips kase, spatula: duz dikdortgen bicak
    public Vector3 contact;                     // ucun yere degen en alt noktasi

    public static readonly ScrapeToolShape Spoon = new ScrapeToolShape
    {
        zCenter = 0.1525f, halfLen = 0.0475f, halfWidth = 0.032f,
        rimY = -0.0075f, depth = 0.0115f, thickness = 0.003f, ellipse = true,
        contact = new Vector3(0f, -0.0115f, 0.196f)
    };
    public static readonly ScrapeToolShape Spatula = new ScrapeToolShape
    {
        zCenter = 0.1825f, halfLen = 0.0775f, halfWidth = 0.056f,
        rimY = -0.0185f, depth = 0.0015f, thickness = 0.0065f, ellipse = false,
        contact = new Vector3(0f, -0.025f, 0.259f)
    };

    // Kepce ic yuzeyinin yuksekligi ve normali (yerel). inside: nokta kepce izdusumunde mi
    public float Surface(float x, float z, float margin, out bool inside, out Vector3 n)
    {
        float ux = x / halfWidth, uz = (z - zCenter) / halfLen;
        float bowl;
        if (ellipse)
        {
            float mx = x / (halfWidth + margin), mz = (z - zCenter) / (halfLen + margin);
            inside = mx * mx + mz * mz <= 1f;
            bowl = 1f - ux * ux - uz * uz;
        }
        else
        {
            inside = Mathf.Abs(x) <= halfWidth + margin && Mathf.Abs(z - zCenter) <= halfLen + margin;
            bowl = 1f - ux * ux;
        }
        float dx = 0f, dz = 0f;
        if (bowl > 0f)
        {
            dx = depth * 2f * x / (halfWidth * halfWidth);
            if (ellipse) dz = depth * 2f * (z - zCenter) / (halfLen * halfLen);
        }
        else bowl = 0f;
        n = new Vector3(-dx, 1f, -dz).normalized;
        return rimY - depth * bowl;
    }
}

// Kazinan obsidyen kirintilari: kendi kucuk konum-tabanli fizigiyle yere duser, kepceye tirmanir, ucta yigilir ve itilir.
// Kirici ile kirilan cevherler de alet yaklasinca bu simulasyona katilir. Sol tik birakilinca yigin kepceye alinip kaba dokulur.
public class ScrapeDebris : MonoBehaviour
{
    public static ScrapeDebris I;

    class Body
    {
        public Vector3 p, prev, v;
        public float r, rv, invMass, grams;
        public Quaternion rot, localRot;
        public Transform tr;
        public LithicCoreItem core;
        public bool isCore, sleeping, grounded, toolHit, carried, delivering, dying, release, attached;
        public int side;                    // kepceye gore: 1 ust/on, -1 alt/arka, 0 bolge disi
        public int slotIdx = -1;
        public float still, groundY, lastToolHit, age, dieT, visScale, phase;
        public Vector3 toolN, toolVs;
        // kepceye yerlesme / tasima / dokme
        public Vector3 from, slot;          // slot: alet yerel (olceksiz) konumu
        public float t0, dur;
        public Vector3 target;
        public bool Kinematic => attached || carried || delivering;
    }

    const int MaxChips = 900;
    const float Gravity = 9.81f;
    const float Cell = 0.035f;

    readonly List<Body> bodies = new List<Body>();
    readonly Stack<GameObject> pool = new Stack<GameObject>();
    Mesh[] chipMeshes;
    Material[] chipMats;

    // Alet durumu (PlayerTools her kare verir)
    bool toolOn, toolHad;
    Vector3 toolPos, toolPrevPos;
    Quaternion toolRot = Quaternion.identity, toolPrevRot = Quaternion.identity;
    float toolScale = 1f, capacity = 100f, chipR = 0.01f;
    ScrapeToolShape shape;
    Transform carryTool;

    // Kepce yuvalari: kasigin icinde biriken yigin (yerel, olceksiz). Dolum sirasi: alttan, uca yakin olandan.
    readonly List<Vector3> slots = new List<Vector3>();
    readonly List<bool> slotUsed = new List<bool>();
    float slotKeyScale, slotKeyR; bool slotKeyEllipse; float slotKeyW;
    // Kasigin burnu: kirici kirintilari (cevherler) ucun hemen onunde yerde, aletle birlikte surunur
    readonly Body[] nose = new Body[2];
    int noseCount, coreRoom;
    public int NoseCores => noseCount;

    public float TipLoadGrams { get; private set; }     // kepcede biriken
    public float PileGrams { get; private set; }        // ucun onunde itilen serbest yigin
    public int TipLoadCount { get; private set; }
    public int ChipCount { get; private set; }
    public int CarriedCount { get; private set; }
    float loadT;

    public System.Action<float, Vector3> onGramsDelivered;          // dokulen kirintilar (gram, nokta)
    public System.Action<LithicCoreItem> onCoreDelivered;          // kaba ulasan cevher (PlayerTools kaba koyar)

    public static ScrapeDebris Ensure()
    {
        if (I != null) return I;
        var go = new GameObject("ScrapeDebris");
        I = go.AddComponent<ScrapeDebris>();
        return I;
    }

    void Awake()
    {
        I = this;
        BuildAssets();
    }

    void OnDestroy() { if (I == this) I = null; }

    // ---------- Gorsel kaynaklar: koseli obsidyen pulcuklari ----------

    void BuildAssets()
    {
        var rng = new System.Random(1234);
        chipMeshes = new Mesh[6];
        for (int i = 0; i < chipMeshes.Length; i++) chipMeshes[i] = MakeChipMesh(rng, i);
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        chipMats = new Material[3];
        chipMats[0] = MakeMat(sh, new Color(0.36f, 0.30f, 0.28f), 0.35f, 0.0f);      // kulle kapli ust kabuk (cüruf)
        chipMats[1] = MakeMat(sh, new Color(0.26f, 0.21f, 0.24f), 0.45f, 0.0f);      // morumsu gri kabuk
        chipMats[2] = MakeMat(sh, new Color(0.035f, 0.03f, 0.045f), 0.92f, 0.0f);    // altindan cikan cam gibi siyah obsidyen
    }

    static Material MakeMat(Shader sh, Color c, float smooth, float metal)
    {
        var m = new Material(sh);
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        m.SetFloat("_Metallic", metal);
        m.enableInstancing = true;
        return m;
    }

    // Ikosahedrondan turetilmis, duzensiz, basik ve keskin yuzlu (duz golgeli) pulcuk
    static Mesh MakeChipMesh(System.Random rng, int seed)
    {
        float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var v = new List<Vector3>
        {
            new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
            new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
            new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
        };
        int[] f =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1
        };
        float flat = 0.42f + 0.18f * (float)rng.NextDouble();
        float sx = 0.85f + 0.35f * (float)rng.NextDouble();
        for (int i = 0; i < v.Count; i++)
        {
            Vector3 p = v[i].normalized * (0.72f + 0.42f * (float)rng.NextDouble());
            p.x *= sx; p.y *= flat;
            v[i] = p;
        }
        var verts = new Vector3[f.Length];
        var tris = new int[f.Length];
        for (int i = 0; i < f.Length; i++) { verts[i] = v[f[i]]; tris[i] = i; }
        var m = new Mesh { name = "ObsidianChip" + seed };
        m.vertices = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    GameObject TakeChipObject(int variant)
    {
        GameObject go;
        if (pool.Count > 0) { go = pool.Pop(); go.SetActive(true); }
        else
        {
            go = new GameObject("Chip");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            mr.receiveShadows = true;
        }
        go.GetComponent<MeshFilter>().sharedMesh = chipMeshes[variant % chipMeshes.Length];
        float pick = Random.value;
        go.GetComponent<MeshRenderer>().sharedMaterial = chipMats[pick < 0.5f ? 0 : (pick < 0.75f ? 1 : 2)];
        return go;
    }

    // ---------- Disaridan ----------

    // capacityGrams: kepceye sigan kirinti; chipRadius: ortalama pulcuk yaricapi (yuva araligi icin)
    // coreRoomNow: kaplarda kac cevherlik yer var (o kadar cevher kasigin burnuna takilir)
    public void SetTool(bool on, Vector3 pos, Quaternion rot, float scale, ScrapeToolShape s, float capacityGrams = 100f, float chipRadius = 0.01f, int coreRoomNow = 0)
    {
        toolOn = on;
        if (!on) return;
        toolPos = pos; toolRot = rot; toolScale = scale; shape = s;
        capacity = capacityGrams; chipR = chipRadius; coreRoom = coreRoomNow;
        EnsureSlots();
    }

    public Vector3 ToolContactWorld => toolPos + toolRot * (shape.contact * toolScale);

    // Test/teshis: kirinti durumlari
    public string Stats()
    {
        int awake = 0, sleep = 0, touch = 0, att = 0, carried = 0;
        float now = Time.time;
        string cs = "";
        Vector3 c = ToolContactWorld;
        Vector3 hd = toolRot * Vector3.forward; hd.y = 0f; hd.Normalize();
        foreach (var core in LithicCoreItem.All)
        {
            if (core == null) continue;
            Vector3 d = core.transform.position - c;
            bool inSim = bodies.Exists(x => x.core == core);
            cs += $" [f={Vector3.Dot(d, hd):F2} l={Vector3.Dot(d, new Vector3(hd.z, 0f, -hd.x)):F2} h={d.y:F2} ext={core.external} sim={inSim}]";
        }
        foreach (var b in bodies)
        {
            if (b.isCore) continue;
            if (b.carried) carried++;
            if (b.attached) att++;
            else if (b.sleeping) sleep++; else awake++;
            if (now - b.lastToolHit < 0.2f) touch++;
        }
        return $"chips={ChipCount} awake={awake} sleep={sleep} toolTouch={touch} inBowl={att} carried={carried} bowl={TipLoadGrams:F1}g pile={PileGrams:F1}g noseCores={noseCount} cores:{cs}";
    }

    void EnsureSlots()
    {
        if (slots.Count > 0 && Mathf.Approximately(slotKeyScale, toolScale) && Mathf.Approximately(slotKeyR, chipR)
            && slotKeyEllipse == shape.ellipse && Mathf.Approximately(slotKeyW, shape.halfWidth)) return;
        // yerlesik kirintilar varsa once serbest birak (alet degisti)
        foreach (var b in bodies) if (b.attached) DetachBody(b);
        slotKeyScale = toolScale; slotKeyR = chipR; slotKeyEllipse = shape.ellipse; slotKeyW = shape.halfWidth;
        slots.Clear(); slotUsed.Clear();
        float s = toolScale;
        float step = chipR * 1.55f / s;
        float lift = chipR * 0.5f / s, layerH = chipR * 0.8f / s;
        var tmp = new List<Vector3>();
        for (int layer = 0; layer < 7; layer++)
        {
            float shrink = Mathf.Max(0.18f, 1f - layer * 0.15f);
            float hw = shape.halfWidth * 0.86f * shrink, hl = shape.halfLen * 0.84f * shrink;
            float zc = shape.zCenter + shape.halfLen * 0.08f;           // yigin uca dogru kayik (alet egik)
            tmp.Clear();
            float rowStep = step * 0.87f;
            int rows = Mathf.Max(1, Mathf.FloorToInt(2f * hl / rowStep));
            for (int ri = 0; ri <= rows; ri++)
            {
                float z = -hl + ri * rowStep;
                float off = (ri % 2) * step * 0.5f;
                for (float x = -hw + off; x <= hw + 1e-5f; x += step)
                {
                    float ux = x / hw, uz = z / hl;
                    if (shape.ellipse && ux * ux + uz * uz > 1f) continue;
                    float lz = zc + z;
                    float ys = shape.Surface(x, lz, 0f, out _, out _);
                    tmp.Add(new Vector3(x, ys + lift + layer * layerH, lz));
                }
            }
            if (tmp.Count == 0) tmp.Add(new Vector3(0f, shape.rimY + lift + layer * layerH, zc));
            Vector3 low = new Vector3(0f, 0f, shape.zCenter + shape.halfLen * 0.4f);
            tmp.Sort((a, b) => (a.x * a.x + (a.z - low.z) * (a.z - low.z)).CompareTo(b.x * b.x + (b.z - low.z) * (b.z - low.z)));
            foreach (var p in tmp) { slots.Add(p); slotUsed.Add(false); }
        }
    }

    int FreeSlot()
    {
        for (int i = 0; i < slotUsed.Count; i++) if (!slotUsed[i]) return i;
        return -1;
    }

    Vector3 SlotWorld(Vector3 local) { return toolPos + toolRot * (local * toolScale); }

    // Kirinti kepceye yerlesir (kapasite ve yer varsa): yumusakca yuvasina kivrilir ve aletle birlikte gider
    bool TryAttach(Body b)
    {
        if (!toolOn || b.isCore || b.attached || b.Kinematic || b.dying) return false;
        if (TipLoadGrams + b.grams > capacity + 0.01f) return false;
        int si = FreeSlot();
        if (si < 0) return false;
        slotUsed[si] = true;
        b.attached = true; b.slotIdx = si; b.slot = slots[si];
        b.from = b.p; b.t0 = Time.time; b.dur = 0.1f + Random.Range(0f, 0.06f);
        b.localRot = Quaternion.Inverse(toolRot) * b.rot;
        b.sleeping = false; b.side = 0;
        b.phase = Random.Range(0f, 6.28f);
        TipLoadGrams += b.grams; TipLoadCount++;
        return true;
    }

    bool TryAttachCore(Body b)
    {
        if (!toolOn || !b.isCore || b.Kinematic || b.core == null) return false;
        if (noseCount >= Mathf.Min(nose.Length, coreRoom)) return false;
        int idx = nose[0] == null ? 0 : 1;
        nose[idx] = b; noseCount++;
        b.attached = true; b.slotIdx = -10 - idx;
        b.from = b.p; b.t0 = Time.time; b.dur = 0.15f;
        b.sleeping = false; b.side = 0; b.release = false;
        return true;
    }

    void DetachBody(Body b)
    {
        if (!b.attached) return;
        if (b.isCore)
        {
            for (int i = 0; i < nose.Length; i++) if (nose[i] == b) { nose[i] = null; noseCount--; }
            b.attached = false; b.slotIdx = -1;
            b.prev = b.p; b.v = Vector3.zero; b.still = 0f; b.side = 0;
            return;
        }
        if (b.slotIdx >= 0 && b.slotIdx < slotUsed.Count) slotUsed[b.slotIdx] = false;
        b.attached = false; b.slotIdx = -1;
        b.prev = b.p; b.v = Vector3.zero; b.still = 0f; b.side = 0;
        TipLoadGrams = Mathf.Max(0f, TipLoadGrams - b.grams); TipLoadCount = Mathf.Max(0, TipLoadCount - 1);
    }

    void DetachAll()
    {
        foreach (var b in bodies) if (b.attached) DetachBody(b);
        TipLoadGrams = 0f; TipLoadCount = 0;
        nose[0] = nose[1] = null; noseCount = 0;
    }

    // Kazinan kabuk: ucun kenarinda kalkan pulcuk. attach: kepcede yer varsa dogrudan yigina katilir
    public void SpawnChip(Vector3 pos, Vector3 vel, float r, float grams, bool attach = true)
    {
        if (ChipCount >= MaxChips) Cull();
        var b = new Body
        {
            p = pos, prev = pos, v = vel, r = r * 0.8f, rv = r * 0.48f, invMass = 1f, grams = grams,
            rot = Quaternion.Euler(Random.Range(-25f, 25f), Random.Range(0f, 360f), Random.Range(-25f, 25f)),
            visScale = r
        };
        b.tr = TakeChipObject(Random.Range(0, 6)).transform;
        b.tr.SetPositionAndRotation(pos, b.rot);
        b.tr.localScale = Vector3.one * (r * 0.05f);       // dogarken kucuk, hizla buyur
        bodies.Add(b);
        ChipCount++;
        if (attach) TryAttach(b);
    }

    // En eski, uzaktaki uyuyan kirintilari erit
    void Cull()
    {
        Vector3 c = ToolContactWorld;
        int need = ChipCount - MaxChips + 40;
        for (int i = 0; i < bodies.Count && need > 0; i++)
        {
            var b = bodies[i];
            if (b.isCore || b.Kinematic || b.dying) continue;
            if (toolOn && (b.p - c).sqrMagnitude < 0.6f * 0.6f) continue;
            b.dying = true; b.dieT = 0f; need--;
        }
    }

    // Ucun onundeki serbest yigin: aletin degdigi ve birbirine degen kirintilar
    readonly List<int> bfs = new List<int>();
    readonly HashSet<int> bfsSet = new HashSet<int>();

    void CollectPile(float maxDist)
    {
        bfs.Clear(); bfsSet.Clear();
        if (!toolHad) return;
        Vector3 c = ToolContactWorld;
        float seedR = shape.halfWidth * toolScale + 0.03f;
        float now = Time.time;
        for (int i = 0; i < bodies.Count; i++)
        {
            var b = bodies[i];
            if (b.Kinematic || b.dying) continue;
            bool seed = now - b.lastToolHit < 0.3f || (b.p - c).sqrMagnitude < (seedR + b.r) * (seedR + b.r);
            if (seed && bfsSet.Add(i)) bfs.Add(i);
        }
        for (int k = 0; k < bfs.Count; k++)
        {
            var a = bodies[bfs[k]];
            for (int j = 0; j < bodies.Count; j++)
            {
                if (bfsSet.Contains(j)) continue;
                var b = bodies[j];
                if (b.Kinematic || b.dying) continue;
                float rr = (a.r + b.r) * 1.35f + 0.004f;
                if ((a.p - b.p).sqrMagnitude > rr * rr) continue;
                if ((b.p - c).sqrMagnitude > maxDist * maxDist) continue;
                bfsSet.Add(j); bfs.Add(j);
            }
        }
    }

    // Kepceyi kaldir: kepcedeki yigin ve (yer kaldiysa) ucun onundeki kirintilar/cevherler aletin kasesinde tasinir
    public float Capture(Transform tool, float capacityGrams, int coreRoom, out int coresTaken, out int chipsTaken)
    {
        coresTaken = 0; chipsTaken = 0;
        carryTool = tool;
        float now = Time.time;
        float grams = 0f;
        // 1) zaten kepcede olan kirintilar
        foreach (var b in bodies)
        {
            if (!b.attached || b.isCore) continue;
            b.attached = false; b.carried = true;
            b.from = b.p; b.t0 = now; b.dur = 0.01f;
            grams += b.grams; chipsTaken++;
        }
        float topY = shape.rimY;
        for (int i = 0; i < slotUsed.Count; i++) if (slotUsed[i]) topY = Mathf.Max(topY, slots[i].y);
        // 2) burundaki cevherler yiginin tepesine
        int k = 0;
        for (int ni = 0; ni < nose.Length; ni++)
        {
            var b = nose[ni];
            if (b == null) continue;
            nose[ni] = null;
            b.attached = false; b.carried = true; b.sleeping = false;
            float rvl = b.rv / toolScale;
            b.slot = new Vector3((ni == 0 ? -0.15f : 0.15f) * shape.halfWidth, topY + rvl * 0.8f, shape.zCenter);
            b.from = b.p; b.t0 = now + k * 0.05f; b.dur = 0.2f; k++;
            b.localRot = Quaternion.Inverse(toolRot) * b.rot;
            coresTaken++;
        }
        if (k > 0) topY += 0.6f * (nose.Length > 0 ? 0.03f / toolScale : 0f);
        noseCount = 0;
        // 3) ucun onundeki yigin (yakindan uzaga), bos yuvalara
        CollectPile(0.32f);
        Vector3 bowlC = ToolContactWorld;
        bfs.Sort((x, y) => (bodies[x].p - bowlC).sqrMagnitude.CompareTo((bodies[y].p - bowlC).sqrMagnitude));
        foreach (int i in bfs)
        {
            var b = bodies[i];
            if (b.isCore)
            {
                if (coreRoom - coresTaken <= 0) continue;
                coresTaken++;
                topY += b.rv / toolScale;
                b.slot = new Vector3(0f, topY + b.rv / toolScale * 0.5f, shape.zCenter);
                topY += b.rv / toolScale;
                b.core.external = true;
            }
            else
            {
                if (grams + b.grams > capacityGrams) continue;
                grams += b.grams; chipsTaken++;
                int si = FreeSlot();
                if (si >= 0) { slotUsed[si] = true; b.slot = slots[si]; }
                else
                {
                    b.slot = new Vector3(Random.Range(-0.3f, 0.3f) * shape.halfWidth, topY + chipR / toolScale, shape.zCenter + Random.Range(-0.2f, 0.2f) * shape.halfLen);
                    topY += chipR * 0.25f / toolScale;
                }
            }
            b.carried = true; b.sleeping = false; b.side = 0;
            b.from = b.p;
            b.t0 = now + Mathf.Min(0.1f, k * 0.004f);
            b.dur = 0.14f + Random.Range(0f, 0.05f);
            b.localRot = Quaternion.Inverse(toolRot) * b.rot;
            k++;
        }
        for (int i = 0; i < slotUsed.Count; i++) slotUsed[i] = false;
        TipLoadGrams = 0f; TipLoadCount = 0;
        CarriedCount = chipsTaken + coresTaken;
        return grams;
    }

    // Tasinan her seyi hedefe (kabin agzina) dok: sirayla firlar, kuculur ve teslim edilir
    public void Deliver(Vector3 target, float spread)
    {
        float now = Time.time;
        int k = 0;
        foreach (var b in bodies)
        {
            if (!b.carried) continue;
            b.carried = false; b.delivering = true;
            b.from = b.p; b.target = target + Random.insideUnitSphere * 0.02f;
            b.t0 = now + Mathf.Min(spread, k * 0.006f);
            b.dur = 0.22f + Random.Range(0f, 0.08f);
            k++;
        }
        carryTool = null;
        CarriedCount = 0;
    }

    // Aninda teslim (oyuncu imleci birakti vb.)
    public void DeliverNow()
    {
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (!b.carried && !b.delivering) continue;
            Arrive(i);
        }
        carryTool = null;
        CarriedCount = 0;
    }

    void Arrive(int i)
    {
        var b = bodies[i];
        if (b.isCore)
        {
            if (b.core != null)
            {
                b.core.external = false;
                onCoreDelivered?.Invoke(b.core);
            }
        }
        else onGramsDelivered?.Invoke(b.grams, b.p);
        RemoveAt(i);
    }

    void RemoveAt(int i)
    {
        var b = bodies[i];
        if (b.attached) DetachBody(b);
        if (!b.isCore)
        {
            if (b.tr != null) { b.tr.gameObject.SetActive(false); pool.Push(b.tr.gameObject); }
            ChipCount--;
        }
        else if (b.core != null) b.core.external = false;
        int last = bodies.Count - 1;
        bodies[i] = bodies[last];
        bodies.RemoveAt(last);
    }

    // ---------- Simulasyon ----------

    readonly Dictionary<long, int> head = new Dictionary<long, int>();
    int[] next = new int[256];

    static long CellKey(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    void LateUpdate()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
        if (dt <= 0f) return;
        var field = LavaField.I;
        if (!toolOn)
        {
            if (toolHad && TipLoadCount > 0) DetachAll();      // alet kalkti: kepcedekiler duser
            toolHad = false;
        }
        if (toolOn && !toolHad) { toolPrevPos = toolPos; toolPrevRot = toolRot; toolHad = true; }

        AdoptCores();

        Vector3 contact = ToolContactWorld;
        float wakeR = 0.5f;
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.isCore && b.core == null) { RemoveAt(i); continue; }
            if (b.isCore && !b.Kinematic && (b.tr.position - b.p).sqrMagnitude > 0.01f)
            {
                // cevher disaridan tasindi: yeni konumu benimse
                b.p = b.prev = b.tr.position; b.v = Vector3.zero; b.sleeping = false; b.still = 0f;
            }
            b.age += dt;
            if (b.sleeping && toolOn && (b.p - contact).sqrMagnitude < wakeR * wakeR) { b.sleeping = false; b.still = 0f; }
            if (!b.sleeping && !b.Kinematic && field != null)
                b.groundY = field.HeightAt(b.p);
            else if (b.sleeping && field != null && (i + Time.frameCount) % 60 == 0)
            {
                // uyuyan parcanin zemini degistiyse (lav seviyesi dustu, kabuk kirildi) uyan ve otur
                float g = field.HeightAt(b.p);
                if (Mathf.Abs(g - b.groundY) > 0.01f) { b.groundY = g; b.sleeping = false; b.still = 0f; b.prev = b.p; b.v = Vector3.zero; }
            }
        }

        // ucun hemen onundeki cevher kasigin burnuna takilir (kaplarda yer varsa)
        if (toolOn && noseCount < Mathf.Min(nose.Length, coreRoom))
        {
            Vector3 hd = toolRot * Vector3.forward; hd.y = 0f;
            hd = hd.sqrMagnitude > 1e-6f ? hd.normalized : Vector3.forward;
            foreach (var b in bodies)
            {
                if (!b.isCore || b.Kinematic || b.core == null) continue;
                Vector3 d = b.p - contact; d.y = 0f;
                float f = Vector3.Dot(d, hd);
                if (f < -b.r * 0.4f || d.magnitude > b.r + 0.035f) continue;
                if (b.p.y - b.groundY > b.rv + 0.06f) continue;
                if (!TryAttachCore(b)) break;
            }
        }

        // kepcedeki yigin once aletle birlikte tasinir (serbest kirintilar ona carpar)
        UpdateAttached(dt);

        int sub = toolOn ? 3 : 2;
        float h = dt / sub;
        for (int s = 0; s < sub; s++)
        {
            float a0 = s / (float)sub, a1 = (s + 1) / (float)sub;
            Vector3 pPrev = Vector3.Lerp(toolPrevPos, toolPos, a0), pNow = Vector3.Lerp(toolPrevPos, toolPos, a1);
            Quaternion rPrev = Quaternion.Slerp(toolPrevRot, toolRot, a0), rNow = Quaternion.Slerp(toolPrevRot, toolRot, a1);
            Substep(h, pPrev, rPrev, pNow, rNow);
        }
        toolPrevPos = toolPos; toolPrevRot = toolRot;
        ReleaseSleepingCores();

        UpdateCarried(dt);
        UpdateVisuals(dt, field);

        loadT -= dt;
        if (loadT <= 0f)
        {
            loadT = 0.15f;
            if (toolOn && CarriedCount == 0)
            {
                CollectPile(0.32f);
                float g = 0f;
                foreach (int i in bfs) if (!bodies[i].isCore) g += bodies[i].grams;
                PileGrams = g;
            }
            else PileGrams = 0f;
        }
    }

    // Alet yaklasinca kirici kirintilari (cevherler) simulasyona katilir
    void AdoptCores()
    {
        if (!toolOn) return;
        Vector3 c = ToolContactWorld;
        foreach (var core in LithicCoreItem.All)
        {
            if (core == null || core.external) continue;
            Vector3 d = core.transform.position - c; d.y = 0f;
            if (d.sqrMagnitude > 0.55f * 0.55f) continue;
            var lf = LavaField.I;
            if (lf != null && core.transform.position.y - lf.HeightAt(core.transform.position) > 0.3f) continue;   // havada: once dussun
            var rend = core.GetComponentInChildren<Renderer>();
            Vector3 ext = rend != null ? rend.bounds.extents : Vector3.one * 0.06f;
            core.external = true;
            var b = new Body
            {
                p = core.transform.position, prev = core.transform.position, r = Mathf.Max(0.02f, (ext.x + ext.z) * 0.45f),
                rv = Mathf.Max(0.015f, ext.y * 0.85f), invMass = 0.12f, isCore = true, core = core, tr = core.transform,
                rot = core.transform.rotation
            };
            bodies.Add(b);
        }
    }

    void UpdateAttached(float dt)
    {
        if (TipLoadCount == 0 && noseCount == 0) return;
        float now = Time.time;
        UpdateNose(now);
        float speed = (toolPos - toolPrevPos).magnitude / Mathf.Max(dt, 1e-4f);
        float jig = Mathf.Clamp01(speed * 2f) * 0.0012f;
        foreach (var b in bodies)
        {
            if (!b.attached || b.isCore) continue;
            Vector3 sw = SlotWorld(b.slot);
            sw += toolRot * new Vector3(Mathf.Sin(now * 27f + b.phase), 0f, Mathf.Cos(now * 23f + b.phase * 1.3f)) * jig;
            float u = Mathf.Clamp01((now - b.t0) / b.dur);
            float e = u * u * (3f - 2f * u);
            b.p = u >= 1f ? sw : Vector3.Lerp(b.from, sw, e) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * b.r * 0.8f);
            b.prev = b.p; b.v = Vector3.zero;
        }
    }

    // Burundaki cevherler: ucun hemen onunde, zeminde; aletle birlikte surunur ve yuvarlanir
    void UpdateNose(float now)
    {
        if (noseCount == 0) return;
        var field = LavaField.I;
        Vector3 c = ToolContactWorld;
        Vector3 hd = toolRot * Vector3.forward; hd.y = 0f;
        hd = hd.sqrMagnitude > 1e-6f ? hd.normalized : Vector3.forward;
        Vector3 lat = new Vector3(hd.z, 0f, -hd.x);
        for (int i = 0; i < nose.Length; i++)
        {
            var b = nose[i];
            if (b == null) continue;
            if (b.core == null) { nose[i] = null; noseCount--; continue; }
            float side = noseCount == 1 ? 0f : (i == 0 ? -1f : 1f);
            Vector3 want = c + hd * (b.r * 0.85f + 0.004f) + lat * (side * b.r * 0.9f);
            want.y = (field != null ? field.HeightAt(want) : want.y) + b.rv;
            float u = Mathf.Clamp01((now - b.t0) / b.dur);
            Vector3 np = u >= 1f ? want : Vector3.Lerp(b.from, want, u * u * (3f - 2f * u));
            Vector3 mv = np - b.p; mv.y = 0f;
            float dist = mv.magnitude;
            if (dist > 1e-5f) b.rot = Quaternion.AngleAxis(dist / Mathf.Max(0.01f, b.r) * Mathf.Rad2Deg * 0.8f, Vector3.Cross(Vector3.up, mv / dist)) * b.rot;
            b.p = np; b.prev = np; b.v = Vector3.zero;
        }
    }

    void Substep(float h, Vector3 tpPrev, Quaternion trPrev, Vector3 tpNow, Quaternion trNow)
    {
        int n = bodies.Count;
        if (next.Length < n) next = new int[n * 2];
        // tahmin
        for (int i = 0; i < n; i++)
        {
            var b = bodies[i];
            b.toolHit = false;
            if (b.sleeping || b.Kinematic) continue;
            b.v.y -= Gravity * h;
            b.v *= 1f - 0.4f * h;
            b.prev = b.p;
            b.p += b.v * h;
        }
        // izgara (kirintilar; kepcedekiler sabit engel olarak dahil)
        head.Clear();
        for (int i = 0; i < n; i++)
        {
            var b = bodies[i];
            if (b.isCore || b.carried || b.delivering || b.dying) { next[i] = -1; continue; }
            long k = CellKey(Mathf.FloorToInt(b.p.x / Cell), Mathf.FloorToInt(b.p.z / Cell));
            next[i] = head.TryGetValue(k, out int hd) ? hd : -1;
            head[k] = i;
        }

        Quaternion invNow = Quaternion.Inverse(trNow);
        float s = toolScale;
        for (int it = 0; it < 2; it++)
        {
            for (int i = 0; i < n; i++)
            {
                var b = bodies[i];
                if (b.sleeping || b.Kinematic) continue;
                // zemin
                float floor = b.groundY + b.rv;
                b.grounded = false;
                if (b.p.y < floor)
                {
                    // derin gomulme (dogarken/ışınlanınca): enerji uretmeden cikar
                    if (b.p.y < floor - b.rv * 0.5f) b.prev.y += floor - b.p.y;
                    b.p.y = floor; b.grounded = true;
                }
                else if (b.p.y < floor + 0.002f) b.grounded = true;

                if (!toolOn) continue;
                // alet kepcesi: ust yuzune degen kirinti yigina katilir (yer varsa), yoksa yuzeyde kayar
                Vector3 q = invNow * (b.p - tpNow) / s;
                float rl = b.r / s;
                float ys = shape.Surface(q.x, q.z, rl * 0.35f, out bool inside, out Vector3 nl);
                if (!inside || q.y > ys + rl + 0.08f / s || q.y < ys - shape.thickness - rl - 0.08f / s) b.side = 0;
                else
                {
                    if (b.side == 0) b.side = q.y >= ys - shape.thickness * 0.5f ? 1 : -1;
                    bool hit = false;
                    if (b.side > 0 && q.y < ys + rl * 0.85f) { q.y = ys + rl * 0.85f; hit = true; }
                    else if (b.side < 0 && !b.isCore && q.y > ys - shape.thickness - rl * 0.85f) { q.y = ys - shape.thickness - rl * 0.85f; hit = true; }
                    if (hit)
                    {
                        Vector3 wNow = tpNow + trNow * (q * s);
                        Vector3 wPrev = tpPrev + trPrev * (q * s);
                        b.p = wNow;
                        b.toolHit = true;
                        b.lastToolHit = Time.time;
                        b.toolN = trNow * (b.side > 0 ? nl : -nl);
                        b.toolVs = (wNow - wPrev) / h;
                        if (b.p.y < floor) b.p.y = floor;
                        if (b.side > 0 && (b.isCore ? TryAttachCore(b) : TryAttach(b))) continue;
                    }
                }
                Plow(b, h, tpPrev, trPrev, tpNow, trNow);
            }
            // kirinti-kirinti
            for (int i = 0; i < n; i++)
            {
                var a = bodies[i];
                if (a.sleeping || a.Kinematic || a.isCore || a.dying) continue;
                int cx = Mathf.FloorToInt(a.p.x / Cell), cz = Mathf.FloorToInt(a.p.z / Cell);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (!head.TryGetValue(CellKey(cx + dx, cz + dz), out int j)) continue;
                        for (; j >= 0; j = next[j])
                        {
                            if (j == i) continue;
                            var b = bodies[j];
                            bool bDyn = !b.sleeping && !b.attached;
                            if (bDyn && j < i) continue;     // iki dinamik cift bir kez
                            Collide(a, b);
                        }
                    }
            }
            // cevherler (az sayida): herkesle kaba kontrol
            for (int i = 0; i < n; i++)
            {
                var a = bodies[i];
                if (!a.isCore || a.Kinematic) continue;
                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    var b = bodies[j];
                    if (b.carried || b.delivering || b.dying) continue;
                    if (b.isCore && j < i) continue;
                    if (a.sleeping && (b.sleeping || b.attached)) continue;
                    Collide(a, b);
                }
            }
        }

        // hiz + surtunme + uyku
        Vector3 contactNow = tpNow + trNow * (shape.contact * s);
        for (int i = 0; i < n; i++)
        {
            var b = bodies[i];
            if (b.sleeping || b.Kinematic) continue;
            b.v = (b.p - b.prev) / h;
            float maxV = b.isCore ? 2.5f : 3.5f;          // konum duzeltmesi asla patlamaya donusmesin
            if (b.v.sqrMagnitude > maxV * maxV) b.v = b.v.normalized * maxV;
            if (b.toolHit)
            {
                Vector3 rel = b.v - b.toolVs;
                float rn = Vector3.Dot(rel, b.toolN);
                Vector3 relT = rel - b.toolN * rn;
                b.v = b.toolVs + b.toolN * Mathf.Max(rn, 0f) + relT * (1f - 0.6f);
            }
            if (b.grounded)
            {
                Vector3 vh = new Vector3(b.v.x, 0f, b.v.z);
                float sp = vh.magnitude, dec = (b.isCore ? 1.1f : 0.85f) * Gravity * h;
                vh = sp <= dec ? Vector3.zero : vh * ((sp - dec) / sp);
                b.v = new Vector3(vh.x, Mathf.Clamp(b.v.y, -0.5f, 0.4f), vh.z);    // zemin duzeltmesi ziplatmaz
            }
            float sp2 = b.v.sqrMagnitude;
            bool near = toolOn && (b.p - contactNow).sqrMagnitude < 0.5f * 0.5f;
            if (!near && sp2 < 0.03f * 0.03f && b.grounded)
            {
                b.still += h;
                if (b.still > 0.4f) { b.sleeping = true; b.v = Vector3.zero; if (b.isCore) b.release = true; }
            }
            else b.still = 0f;
        }
    }

    // Kepcenin yere degen on kenari bir pulluk gibi davranir: yerdeki kirintilar kenarin arkasina gecemez, ucun onunde
    // kavisli bir yigin olusur ve aletle birlikte itilir (kasikta kenar elips, spatulada duz).
    void Plow(Body b, float h, Vector3 tpPrev, Quaternion trPrev, Vector3 tpNow, Quaternion trNow)
    {
        float s = toolScale;
        Vector3 hd = trNow * Vector3.forward; hd.y = 0f;
        if (hd.sqrMagnitude < 1e-6f) return;
        hd.Normalize();
        Vector3 c = tpNow + trNow * (shape.contact * s);
        float hgt = b.p.y - b.groundY;
        if (hgt > 0.035f + b.r) return;
        Vector3 d = b.p - c;
        float f = d.x * hd.x + d.z * hd.z;
        if (f < -0.08f || f > 0.06f) return;
        Vector3 lat = new Vector3(hd.z, 0f, -hd.x);
        float hwW = shape.halfWidth * s;
        float l = d.x * lat.x + d.z * lat.z;
        if (Mathf.Abs(l) > hwW + b.r * 0.5f) return;
        float need = PlowEdge(l, hwW, s) + b.r;
        if (f >= need) return;
        // onceki alt adimda kenarin onunde miydi? (yanlardan kepcenin altina girenler itilmez)
        Vector3 hp = trPrev * Vector3.forward; hp.y = 0f; hp = hp.sqrMagnitude > 1e-6f ? hp.normalized : hd;
        Vector3 cp = tpPrev + trPrev * (shape.contact * s);
        Vector3 dp = b.prev - cp;
        Vector3 latp = new Vector3(hp.z, 0f, -hp.x);
        float fp = dp.x * hp.x + dp.z * hp.z, lp = dp.x * latp.x + dp.z * latp.z;
        if (fp < PlowEdge(lp, hwW, s) + b.r * 0.2f - 0.003f) return;
        b.p += hd * (need - f);
        b.toolHit = true;
        b.lastToolHit = Time.time;
        b.toolN = hd;
        b.toolVs = (c - cp) / h;
        if (b.isCore) TryAttachCore(b);
    }

    float PlowEdge(float l, float hwW, float s)
    {
        if (!shape.ellipse) return 0f;
        float u = Mathf.Clamp(l / hwW, -1f, 1f);
        return -shape.halfLen * s * 0.8f * (1f - Mathf.Sqrt(1f - u * u));
    }

    // Uyuyan cevher kendi basit fizigine geri doner (E ile alinabilir); alt adimlardan sonra cagrilir
    void ReleaseSleepingCores()
    {
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (!b.release) continue;
            b.release = false;
            if (!b.sleeping || b.Kinematic) continue;
            if (b.core != null) b.core.transform.position = b.p;
            RemoveAt(i);
        }
    }

    void Collide(Body a, Body b)
    {
        Vector3 d = a.p - b.p;
        float rr = a.r + b.r;
        float d2 = d.sqrMagnitude;
        if (d2 >= rr * rr || d2 < 1e-12f) return;
        float dist = Mathf.Sqrt(d2);
        float pen = rr - dist;
        float wa = a.sleeping || a.Kinematic ? 0f : a.invMass, wb = b.sleeping || b.Kinematic ? 0f : b.invMass;
        if (b.sleeping && pen > 0.0015f) { b.sleeping = false; b.still = 0f; b.prev = b.p; b.v = Vector3.zero; wb = b.invMass; }
        if (a.sleeping && pen > 0.0015f) { a.sleeping = false; a.still = 0f; a.prev = a.p; a.v = Vector3.zero; wa = a.invMass; }
        float w = wa + wb;
        if (w <= 0f) return;
        Vector3 nrm = d / dist;
        Vector3 corr = nrm * (pen / w);
        a.p += corr * wa;
        b.p -= corr * wb;
        if (pen > rr * 0.3f)
        {
            // derin cakisma (ic ice dogan parcalar): hiz uretmeden ayir
            a.prev += corr * wa;
            b.prev -= corr * wb;
        }
        // surtunme: tegetsel goreli kaymayi azalt (yigin bir arada durur)
        Vector3 rel = (a.p - a.prev) - (b.p - b.prev);
        Vector3 relT = rel - nrm * Vector3.Dot(rel, nrm);
        const float mu = 0.3f;
        a.p -= relT * (mu * wa / w);
        b.p += relT * (mu * wb / w);
    }

    void UpdateCarried(float dt)
    {
        float now = Time.time;
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.carried)
            {
                if (carryTool == null) continue;
                Vector3 slotW = carryTool.TransformPoint(b.slot);
                float u = Mathf.Clamp01((now - b.t0) / b.dur);
                if (u <= 0f) { b.p = b.from; continue; }
                float e = u * u * (3f - 2f * u);
                b.p = Vector3.Lerp(b.from, slotW, e) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * (0.03f + b.r));
                b.prev = b.p; b.v = Vector3.zero;
            }
            else if (b.delivering)
            {
                float u = Mathf.Clamp01((now - b.t0) / b.dur);
                if (u <= 0f) { b.from = b.p; continue; }
                float e = u * u;
                b.p = Vector3.Lerp(b.from, b.target, e) + Vector3.up * (Mathf.Sin(u * Mathf.PI) * 0.12f);
                if (u >= 1f) { Arrive(i); continue; }
            }
        }
    }

    void UpdateVisuals(float dt, LavaField field)
    {
        for (int i = bodies.Count - 1; i >= 0; i--)
        {
            var b = bodies[i];
            if (b.tr == null) { if (b.isCore) RemoveAt(i); continue; }
            if (b.dying)
            {
                b.dieT += dt;
                float k = 1f - b.dieT / 0.35f;
                if (k <= 0f) { RemoveAt(i); continue; }
                b.tr.localScale = Vector3.one * (b.visScale * k);
                continue;
            }
            // acik lava dusen kirinti erir
            if (!b.isCore && !b.Kinematic && field != null && (i + Time.frameCount) % 20 == 0 && field.IsLava(b.p) && field.CrustAt(b.p) < 0.3f)
            {
                b.dying = true; b.dieT = 0f; continue;
            }
            if ((b.attached && toolHad && !b.isCore) || (b.carried && carryTool != null))
            {
                // kepcede: alete gore sabit, yuzeye yatik durur
                Quaternion want = (carryTool != null ? carryTool.rotation : toolRot) * b.localRot;
                Vector3 up = want * Vector3.up;
                Vector3 n = carryTool != null ? carryTool.up : toolRot * Vector3.up;
                want = Quaternion.FromToRotation(up, Vector3.Slerp(up, n, 0.7f)) * want;
                b.rot = Quaternion.Slerp(b.rot, want, 1f - Mathf.Exp(-18f * dt));
            }
            else if (!b.sleeping && !b.Kinematic)
            {
                Vector3 vh = new Vector3(b.v.x, 0f, b.v.z);
                float sp = vh.magnitude;
                if (sp > 0.02f && (b.grounded || b.toolHit))
                {
                    Vector3 axis = Vector3.Cross(Vector3.up, vh / sp);
                    float ang = sp * dt / Mathf.Max(0.004f, b.r) * Mathf.Rad2Deg * (b.isCore ? 0.9f : 0.55f);
                    b.rot = Quaternion.AngleAxis(ang, axis) * b.rot;
                }
                else if (b.grounded && !b.isCore)
                {
                    // dinlenirken pulcuk yatay yatar
                    Vector3 fwd = b.rot * Vector3.forward; fwd.y = 0f;
                    if (fwd.sqrMagnitude > 1e-4f) b.rot = Quaternion.Slerp(b.rot, Quaternion.LookRotation(fwd.normalized, Vector3.up), dt * 4f);
                }
            }
            if (b.isCore) { b.tr.SetPositionAndRotation(b.p, b.rot); continue; }
            float grow = b.delivering ? Mathf.Lerp(1f, 0.35f, Mathf.Clamp01((Time.time - b.t0) / Mathf.Max(0.01f, b.dur))) : Mathf.Min(1f, b.age / 0.08f);
            b.tr.SetPositionAndRotation(b.p, b.rot);
            b.tr.localScale = Vector3.one * (b.visScale * Mathf.Max(0.05f, grow));
        }
    }
}

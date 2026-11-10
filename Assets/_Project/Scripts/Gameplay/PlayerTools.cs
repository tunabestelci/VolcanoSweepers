using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Tool { Buckets, Scraper, Breaker }

// Kova / kazma kullanimi ve elleri hareket ettiren kodla yazilmis animasyonlar
public class PlayerTools : MonoBehaviour
{
    class Hand
    {
        public Transform root;
        public Transform rig;
        public Vector3 carryPos, readyPos, carryEuler, readyEuler;
        public float ready;
        public Vector3 animPos;
        public Vector3 animEuler;
        public GameObject item;
        public Transform water;     // kovadaki su gorseli
        public float fill;          // 0..1
        public bool busy;
        public float bobPhase;
        public int ores;            // kovadaki cevher sayisi (en fazla OresPerBucket)
        public bool has = true;     // bu elin kovasi envanterde mi (yere birakilabilir)
    }


    public PlayerController player;
    public Camera cam;
    public Transform leftHandRoot;
    public Transform rightHandRoot;
    public GameObject bucketPrefab;               // kuyu kovasi ve eski ref (kullanilmaz)
    public GameObject pickaxePrefab;
    public GameObject[] vesselPrefabs;            // bardak, surahi, kova
    public GameObject[] toolPrefabs;              // kasik, spatula
    public GameObject[] breakerPrefabs;           // [1..20] kiricilar (0 bos)

    GameObject VesselPrefab() { int t = Mathf.Clamp(Economy.VesselTier, 0, vesselPrefabs.Length - 1); return vesselPrefabs != null && vesselPrefabs.Length > 0 ? vesselPrefabs[t] : bucketPrefab; }
    GameObject ToolPrefab() { int t = Mathf.Clamp(Economy.ToolTier, 0, toolPrefabs.Length - 1); return toolPrefabs != null && toolPrefabs.Length > 0 ? toolPrefabs[t] : pickaxePrefab; }
    float Cap => Economy.VesselCapacity[Mathf.Clamp(Economy.VesselTier, 0, 2)];
    public int OreCap => Economy.VesselOres[Mathf.Clamp(Economy.VesselTier, 0, 2)];
    public string VesselName => Economy.VesselNames[Mathf.Clamp(Economy.VesselTier, 0, 2)];
    public string ToolName => Economy.ToolNames[Mathf.Clamp(Economy.ToolTier, 0, Economy.ToolNames.Length - 1)];
    public string BreakerName => Economy.BreakerNames[Mathf.Clamp(Economy.BreakerTierNow, 0, Economy.BreakerCount)];
    public GameObject coreVisualPrefab;
    public float reach = 6f;
    public float pickupRange = 3.5f;

    public Tool CurrentTool { get; private set; } = Tool.Buckets;
    public int CoreCount => (left != null ? left.ores : 0) + (right != null ? right.ores : 0);
    public int CoreCapacity => OreCap * 2;
    public float FillLeft => left != null && left.has ? left.fill : 0f;
    public float FillRight => right != null && right.has ? right.fill : 0f;
    public string Hint { get; private set; } = "";
    float hintUntil;

    Hand left, right;
    GameObject heldCore;

    void Awake()
    {
        left = MakeHand(leftHandRoot, -1f);
        right = MakeHand(rightHandRoot, 1f);
        Equip(Tool.Buckets);
    }

    void Start()
    {
        RefreshLoadout();
        SetHint("Lavi suyla sogut (1)  >  kirici ile kir (4)  >  kaziyici ile kazi (3)", 7f);
    }       // Economy hazir olduktan sonra dogru kademeyle yeniden kur

    // Eller govdeye bagli: kovalar yanlarda asagida asili durur, bakinca/kullaninca one gelir
    Hand MakeHand(Transform root, float side)
    {
        var h = new Hand { root = root, rig = root.parent };
        SetDefaultPoses(h, side);
        return h;
    }

    static void SetDefaultPoses(Hand h, float side)
    {
        // Kucuk kaplar daha yukarida (elde), kova sarkar
        float cy = Economy.VesselTier == 0 ? -0.46f : (Economy.VesselTier == 1 ? -0.58f : -0.78f);
        float cx = Economy.VesselTier == 2 ? 0.40f : 0.34f;
        h.carryPos = new Vector3(side * cx, cy, Economy.VesselTier == 2 ? 0.22f : 0.3f);
        h.readyPos = new Vector3(side * 0.40f, -0.40f, 0.45f);
        h.carryEuler = Vector3.zero;
        h.readyEuler = Vector3.zero;
    }

    bool hintFromWell;

    void SetHint(string text, float seconds = 2.2f)
    {
        Hint = text;
        hintFromWell = false;
        hintUntil = Time.time + seconds;
    }

    // Test botu ve dis cagrilar icin
    public bool Busy => left.busy || right.busy;
    public void DoBucket(bool leftHand) { UseBucket(leftHand ? left : right); }
    public void DoSwing() { if (!right.busy && Economy.BreakerTierNow > 0) StartCoroutine(SwingHammer(Economy.BreakerTierNow)); }
    public bool BotPourBusy => left.busy || right.busy;
    public void DoPickup() { TryPickup(); }
    public void DoPull() { foreach (var w in WaterWell.All) if (w != null && !w.Busy) w.auto = true; }
    public void DoEquip(Tool t) { Equip(t); }

    // Dukkandan alinan kap/alet elde yenilenir (su ve cevher korunur)
    public void RefreshLoadout()
    {
        if (left == null) return;
        left.busy = false; right.busy = false;
        var t = CurrentTool;
        float lf = left.fill, rf = right.fill; int lo = left.ores, ro = right.ores;
        Equip(t);
        left.fill = lf; right.fill = rf; left.ores = lo; right.ores = ro;
        if (t == Tool.Buckets)
        {
            foreach (var h in new[] { left, right })
                if (h.item != null)
                {
                    UpdateWater(h);
                    var old = h.item.transform.Find("Ores"); if (old != null) Destroy(old.gameObject);
                    BuildOreViews(h);
                }
        }
    }

    public void ShowHint(string text, float seconds = 2.2f) { SetHint(text, seconds); }

    // ---------- Alet degistirme ----------

    void Equip(Tool tool)
    {
        if (left.busy || right.busy) return;
        if (tool == Tool.Breaker && (Economy.BreakerTierNow <= 0 || BreakerPrefab(Economy.BreakerTierNow) == null))
        {
            SetHint(Economy.BreakerTierNow <= 0 ? "Kiricin yok - Lav Adam'dan satin al" : "Kirici modeli bulunamadi");
            return;
        }
        CurrentTool = tool;
        StopDrill();
        if (scrapeOn) EndScrape();
        ClearItem(left);
        ClearItem(right);
        if (heldCore != null) { Destroy(heldCore); heldCore = null; }

        if (tool == Tool.Buckets)
        {
            if (left.has) SpawnBucket(left);
            if (right.has) SpawnBucket(right);
            SetDefaultPoses(left, -1f);
            SetDefaultPoses(right, 1f);
        }
        else if (tool == Tool.Breaker)
        {
            int bt = Economy.BreakerTierNow;
            right.item = Instantiate(BreakerPrefab(bt), right.root);
            right.item.transform.localPosition = Vector3.zero;
            right.item.transform.localRotation = Quaternion.identity;
            switch (Economy.KindOf(bt))
            {
                case Economy.BreakerKind.Hammer:      // sap yukari, bas one egik
                    right.readyPos = new Vector3(0.30f, -0.34f, 0.52f);
                    right.readyEuler = new Vector3(24f, -6f, -6f);
                    break;
                case Economy.BreakerKind.Drill:       // uc one
                    right.readyPos = new Vector3(0.26f, -0.19f, 0.56f);
                    right.readyEuler = new Vector3(3f, -8f, 0f);
                    break;
                default:                              // kirici: uzun, daha one
                    right.readyPos = new Vector3(0.30f, -0.30f, 0.84f);
                    right.readyEuler = new Vector3(4f, -8f, 0f);
                    break;
            }
            right.carryPos = right.readyPos;
            right.carryEuler = right.readyEuler;
            RefreshHeldCore();
        }
        else
        {
            right.item = Instantiate(ToolPrefab(), right.root);
            right.item.transform.localPosition = Vector3.zero;
            // Kazma ucu one baksin (vurus yonunde), sap asagida
            right.item.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            // Kazma sag elde, onde ve hazir
            right.readyPos = new Vector3(0.36f, -0.42f, 0.55f);
            right.readyEuler = new Vector3(25f, 0f, -10f);
            if (IsScraper)
            {
                int st = ScrapeTier;
                if (st <= 2)
                {
                    // Kasik/kepce/spatula kucuk: kameraya yakin ve gorunur durur
                    right.readyPos = new Vector3(0.28f, -0.28f, 0.54f);
                    right.readyEuler = new Vector3(12f, -6f, -8f);
                }
                else
                {
                    // Kurekler: sapin ust kismindan tutulur, bicak onde-asagida (omuzda tasir gibi degil, kazmaya hazir)
                    // sapin ust ucundan tutulur (orijin = model merkezi): bicak onde, sag-alt kosede gorunur
                    float len = st == 3 ? 0.72f : (st == 4 ? 0.95f : 1.05f);
                    right.item.transform.localPosition = new Vector3(0f, 0f, len * 0.32f);
                    right.readyPos = new Vector3(0.30f, -0.24f, 0.30f);
                    right.readyEuler = new Vector3(2f, -12f, -20f);
                }
            }
            right.carryPos = right.readyPos;
            right.carryEuler = right.readyEuler;
            RefreshHeldCore();
        }
    }

    void ClearItem(Hand h)
    {
        if (h.item != null) Destroy(h.item);
        h.item = null;
        h.water = null;
        h.animPos = Vector3.zero;
        h.animEuler = Vector3.zero;
    }

    void SpawnBucket(Hand h)
    {
        h.item = Instantiate(VesselPrefab(), h.root);
        h.item.transform.localPosition = Vector3.zero;
        h.item.transform.localRotation = Quaternion.identity;
        var w = h.item.transform.Find("Water");
        h.water = w;
        UpdateWater(h);
        BuildOreViews(h);
    }

    // Kovanin icindeki cevherleri gorsel olarak diz (tabanda yigin)
    void BuildOreViews(Hand h)
    {
        if (h.item == null || coreVisualPrefab == null || h.ores <= 0) return;
        var bw = h.water != null ? h.water.GetComponent<BucketWater>() : null;
        float floorY = bw != null ? bw.emptyY : 0.02f;
        var holder = new GameObject("Ores").transform;
        holder.SetParent(h.item.transform, false);
        for (int i = 0; i < h.ores; i++)
        {
            var o = Instantiate(coreVisualPrefab, holder);
            foreach (var c in o.GetComponentsInChildren<Collider>()) Destroy(c);
            var rends = o.GetComponentsInChildren<Renderer>();
            float size = 0.0001f;
            if (rends.Length > 0)
            {
                var b = rends[0].bounds;
                foreach (var r in rends) b.Encapsulate(r.bounds);
                size = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.0001f);
            }
            float vf = bw != null ? Mathf.Clamp(bw.rimRadius / 0.15f, 0.3f, 1f) : 1f;      // kap boyutuna gore
            o.transform.localScale = Vector3.one * (0.075f * vf / size) * o.transform.localScale.x;
            float a = i * 2.4f;
            float rad = i == 0 ? 0f : 0.045f * vf;
            o.transform.localPosition = new Vector3(Mathf.Cos(a) * rad, floorY + 0.03f * vf + (i == 0 ? 0.035f * vf : 0f), Mathf.Sin(a) * rad);
            o.transform.localRotation = Quaternion.Euler(i * 57f, i * 91f, i * 33f);
        }
    }

    void RefreshHeldCore()
    {
        if (heldCore != null) { Destroy(heldCore); heldCore = null; }
    }

    static void UpdateWater(Hand h)
    {
        if (h.water == null) return;
        h.water.gameObject.SetActive(h.fill > 0.02f);
        var bw = h.water.GetComponent<BucketWater>();
        if (bw != null) bw.SetFill(h.fill);
    }

    // ---------- Girdi ----------

    void Update()
    {
        if (Time.time > hintUntil) Hint = "";
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null || Cursor.lockState != CursorLockMode.Locked)
        {
            if (scrapeOn) EndScrape();
            if (CurrentTool == Tool.Breaker) UpdateBreaker(false);
            Apply(left); Apply(right);
            return;
        }

        // Tus atamasi: 1 = kovalar (kovalar elindeyken 1: sol kovayi birak, 2: sag kovayi birak), 3 = kazma, Q = kazmayi birak
        if (kb.digit1Key.wasPressedThisFrame)
        {
            if (CurrentTool == Tool.Buckets) DropBucket(left); else Equip(Tool.Buckets);
        }
        if (kb.digit2Key.wasPressedThisFrame)
        {
            if (CurrentTool == Tool.Buckets) DropBucket(right); else Equip(Tool.Buckets);
        }
        if (kb.digit3Key.wasPressedThisFrame) Equip(Tool.Scraper);
        if (kb.digit4Key.wasPressedThisFrame) Equip(Tool.Breaker);
        if (kb.eKey.wasPressedThisFrame) TryPickup();

        // Kuyu: E basili tutarak kova indirilir, doluyken E ile cekilir
        {
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            foreach (var well in WaterWell.All)
            {
                if (well == null) continue;
                bool looking = well.LooksAt(ray.origin, ray.direction, reach + 1f);
                well.SetInput(kb.eKey.wasPressedThisFrame, kb.eKey.isPressed, looking);
                // Kuyunun ipucu, oyuncunun az once aldigi geri bildirimi (ornegin "bardak doluyor") ezmesin
                if (looking && !string.IsNullOrEmpty(well.Hint) && !(OreBin.I != null && OreBin.I.LooksAt(ray, reach + 1f)) && (Hint == "" || hintFromWell))
                {
                    SetHint(well.Hint, 0.3f);
                    hintFromWell = true;
                }
            }
        }

        if (CurrentTool == Tool.Buckets)
        {
            if (mouse.leftButton.wasPressedThisFrame && right.has) UseBucket(right);
            if (mouse.rightButton.wasPressedThisFrame && left.has) UseBucket(left);
        }
        else if (CurrentTool == Tool.Scraper)
        {
            // kasik/spatula: sol tik basili tutulunca kamera calisma noktasina kilitlenir, fare fircayi surer
            UpdateScrape(mouse.leftButton.isPressed || botHoldScrape, mouse.delta.ReadValue());
        }
        else
        {
            UpdateBreaker(mouse.leftButton.isPressed || botHoldBreak);
        }

        Apply(left);
        Apply(right);
    }

    // Eller: durus pozu + yurume sallantisi + animasyon ofseti
    void Apply(Hand h)
    {
        // Kazima kilidinde kamera elin disina cikar: eller gizlenir
        bool hideHands = player != null && player.LockBlend > 0.05f;
        if (h.rig.gameObject.activeSelf == hideHands) h.rig.gameObject.SetActive(!hideHands);
        float speed = player != null ? player.Speed01 : 0f;
        h.bobPhase += Time.deltaTime * (4f + speed * 6f);
        float amp = speed;
        float heavy = (CurrentTool == Tool.Buckets) ? Mathf.Lerp(1f, 1.6f, h.fill) : 0.6f;
        float side = (h == left) ? 1f : -1f;
        Vector3 sway = new Vector3(
            Mathf.Sin(h.bobPhase * 0.5f + side) * 0.012f * amp,
            Mathf.Abs(Mathf.Sin(h.bobPhase * 0.5f)) * 0.03f * amp * -1f + Mathf.Sin(Time.time * 1.3f + side) * 0.004f,
            0f);
        // Dolu kova yurudukce yanlara savrulur
        Vector3 tilt = new Vector3(
            Mathf.Sin(h.bobPhase * 0.5f) * 2f * amp * heavy,
            0f,
            Mathf.Sin(h.bobPhase * 0.5f + 1f) * 4f * amp * heavy * (CurrentTool == Tool.Buckets ? h.fill + 0.2f : 0.3f));
        bool wantReady = h.busy
            || (CurrentTool != Tool.Buckets && (h == right || heldCore != null));
        h.ready = Mathf.MoveTowards(h.ready, wantReady ? 1f : 0f, Time.deltaTime * 4f);
        float e = h.ready * h.ready * (3f - 2f * h.ready);
        h.rig.localRotation = Quaternion.Euler((player != null ? player.Pitch : 0f) * e, 0f, 0f);
        h.root.localPosition = Vector3.Lerp(h.carryPos, h.readyPos, e) + sway + h.animPos;
        h.root.localRotation = Quaternion.Euler(Vector3.Lerp(h.carryEuler, h.readyEuler, e) + tilt + h.animEuler);
    }

    // ---------- Kova ----------

    bool Raycast(out RaycastHit hit)
    {
        var hits = Physics.RaycastAll(cam.transform.position, cam.transform.forward, reach, ~0, QueryTriggerInteraction.Collide);
        hit = default;
        float best = float.MaxValue;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue; // kendi govdemizi atla
            if (h.distance < best) { best = h.distance; hit = h; found = true; }
        }
        return found;
    }

    // Nisangahin gosterdigi nokta: once su kaynagi (buyuk kova), sonra lav yuzeyi
    bool AimPoint(out Vector3 point, out bool isWater, out WaterSource source)
    {
        isWater = false;
        source = null;
        point = Vector3.zero;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        // Su kaynagi (kuyu kovasi): isin-kure testi
        WaterSource bestWs = null; float bestT = float.MaxValue;
        foreach (var ws in WaterSource.All)
            if (ws != null && ws.Hit(ray.origin, ray.direction, reach, out float wt) && wt < bestT) { bestT = wt; bestWs = ws; }
        if (bestWs != null)
        {
            isWater = true;
            source = bestWs;
            point = ray.origin + ray.direction * Mathf.Max(0f, bestT - bestWs.PickRadius * 0.4f);
            return true;
        }
        // Kuyuya bakmak yeterli: kuyu kovasi yukaridaysa nisan tam kovada olmasa da ondan su alinir (kucuk kaplar icin)
        foreach (var well in WaterWell.All)
        {
            if (well == null || well.bucketFill == null || well.bucketFill.locked) continue;
            if (!well.LooksAt(ray.origin, ray.direction, reach)) continue;
            var ws = well.bucketFill.GetComponent<WaterSource>();
            if (ws == null) continue;
            isWater = true;
            source = ws;
            point = ws.transform.position;
            return true;
        }
        bool physHit = Raycast(out var hit);
        if (physHit)
        {
            source = hit.collider.GetComponentInParent<WaterSource>();
            if (source != null)
            {
                isWater = true;
                point = hit.point;
                return true;
            }
        }
        var field = LavaField.I;
        if (field != null && field.Raycast(ray, reach, out var fp))
        {
            point = fp;
            return true;
        }
        if (physHit) { point = hit.point; return true; }
        return false;
    }

    void UseBucket(Hand h)
    {
        if (h.busy) return;
        if (!AimPoint(out var point, out bool isWater, out var source)) { SetHint("Hedef cok uzak"); return; }

        if (isWater)
        {
            if (h.ores > 0) { SetHint("Kovada cevher var - su alinamaz"); return; }
            if (h.fill > 0.95f) { SetHint($"{VesselName} zaten dolu"); return; }
            var big = source != null ? source.GetComponent<BigBucket>() : null;
            if (big != null && !big.TryTake(Cap)) { SetHint(big.locked ? "Kuyu kovasi henuz yukari cikmadi" : "Kuyu kovasi bos - kuyuya bakip E ile su cek"); return; }
            SetHint($"{VesselName} doluyor...", 1.2f);
            StartCoroutine(FillRoutine(h, point));
            return;
        }
        if (h.fill < 0.05f) { SetHint($"{VesselName} bos - kuyu kovasindan doldur"); return; }
        StartCoroutine(PourRoutine(h));
    }

    IEnumerator FillRoutine(Hand h, Vector3 waterPoint)
    {
        h.busy = true;
        Vector3 pos0 = h.animPos, eul0 = h.animEuler;
        Vector3 dipPos = new Vector3(h == left ? 0.1f : -0.1f, -0.22f, 0.32f);
        Vector3 dipEul = new Vector3(40f, 0f, 0f);

        yield return Tween(h, pos0, dipPos, eul0, dipEul, 0.35f);
        // Kuyu kovasindan el kovasina su aktarma: "water pouring out of bucket" kaydinin surekli akan kismi (2.6-4.5 sn)
        if (SfxPlayer.I != null) SfxPlayer.PlaySnippet(SfxPlayer.I.bucketPour, waterPoint, 1.9f, 0.9f, 2.6f);
        const float fillTime = 1.8f;
        float f0 = h.fill;
        for (float e = 0f; e < fillTime; e += Time.deltaTime)
        {
            h.fill = Mathf.Lerp(f0, 1f, e / fillTime);
            UpdateWater(h);
            h.animPos = dipPos + new Vector3(0f, Mathf.Sin(e * 14f) * 0.012f, 0f);
            yield return null;
        }
        h.fill = 1f;
        UpdateWater(h);
        yield return Tween(h, dipPos, Vector3.zero, dipEul, Vector3.zero, 0.4f);
        h.busy = false;
    }

    public bool botHoldPour;           // test botu icin
    float nextSwing;                   // basili tutarken vuruslar arasi kisa bekleme
    const float SwingPause = 0.12f;
    const float ParticlesPerBucket = 420f;   // dolu bir kovanin temsil ettigi parcacik sayisi

    // Kovanin egimine gore fiziksel dokulme: su seviyesi (dunyada yatay) kovanin dudagindan asinca
    // Torricelli akisi ile tasar; tasan su parcacik simulasyonuna girer.
    IEnumerator PourRoutine(Hand h)
    {
        h.busy = true;
        Vector3 liftPos = new Vector3(h == left ? 0.12f : -0.12f, 0.2f, 0.28f);
        Vector3 startEul = new Vector3(25f, 0f, 0f);

        yield return Tween(h, Vector3.zero, liftPos, Vector3.zero, startEul, 0.12f);

        var bw = h.item != null ? h.item.GetComponentInChildren<BucketWater>(true) : null;
        var body = h.item != null ? h.item.transform.Find("WaterBody") : null;
        float tilt = 25f;
        const float maxTilt = 118f;
        float hissTimer = 0f;
        float emitCarry = 0f;
        float elapsed = 0f;
        while (h.fill > 0.01f && elapsed < 8f && bw != null && body != null)
        {
            elapsed += Time.deltaTime;
            var mouse = Mouse.current;
            bool held = mouse != null && (h == right ? mouse.leftButton.isPressed : mouse.rightButton.isPressed);
            if (!held && !botHoldPour) break;

            tilt = Mathf.MoveTowards(tilt, maxTilt, 420f * Time.deltaTime);
            h.animPos = liftPos;
            h.animEuler = new Vector3(tilt, 0f, 0f);

            // --- kova geometrisi (dunya uzayi) ---
            Transform bt = h.item.transform;
            float sc = bt.lossyScale.y;
            float R = bw.rimRadius * sc;                                   // agizdaki ic yaricap
            float vol = bw.totalVolume * sc * sc * sc;                     // ic hacim
            Vector3 u = bt.up;
            float levelLocal = bw.LevelY(h.fill);
            float yPlane = bt.TransformPoint(new Vector3(0f, levelLocal, 0f)).y;
            Vector3 rimC = bt.TransformPoint(new Vector3(0f, bw.fullY, 0f));
            Vector3 dl = Vector3.down - u * Vector3.Dot(Vector3.down, u);
            float sinT = dl.magnitude;
            Vector3 lipDir = sinT > 1e-3f ? dl / sinT : Vector3.forward;
            Vector3 lip = rimC + lipDir * R;
            float head = yPlane - lip.y;

            float dFill = 0f;
            if (head > 0f)
            {
                float hh = Mathf.Min(head, R);
                float w = 2f * Mathf.Sqrt(Mathf.Max(0f, 2f * R * hh - hh * hh));
                float q = 0.4f * w * Mathf.Sqrt(2f * 9.81f) * Mathf.Pow(hh, 1.5f);     // m^3/s (taslama savagi)
                dFill = q * Time.deltaTime / Mathf.Max(vol, 1e-5f);
            }
            // Kova cok egilince kalan su da (taban yukselince) boslasin
            if (tilt > 85f) dFill += Time.deltaTime * 2.2f * Mathf.InverseLerp(85f, 118f, tilt);
            dFill = Mathf.Min(dFill, h.fill);

            if (dFill > 0f)
            {
                h.fill -= dFill;
                emitCarry += dFill * ParticlesPerBucket * Cap;
                int n = Mathf.FloorToInt(emitCarry);
                emitCarry -= n;
                Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                Vector3 lat = Vector3.Cross(Vector3.up, fwd);
                float vOut = Mathf.Sqrt(2f * 9.81f * Mathf.Max(head, 0.02f)) * 1.7f + 1.4f;
                float width = Mathf.Max(0.07f, R * 1.1f);
                for (int i = 0; i < n; i++)
                {
                    // Cok genis, yuvarlak yayilim: her parcacik halka/disk uzerinde farkli menzil ve yana aciyla firlar
                    Vector2 d = Random.insideUnitCircle;
                    Vector3 off = lat * d.x + Vector3.up * d.y;
                    Vector3 spawn = lip + off * width * 0.9f;
                    float ang = Random.Range(-1f, 1f) * 0.75f;                     // ~43 derece yana acilim
                    Vector3 dir = Quaternion.AngleAxis(ang * Mathf.Rad2Deg, Vector3.up) * fwd;
                    float reach = Mathf.Sqrt(Random.value);                         // diski dolduran menzil dagilimi
                    Vector3 v = dir * (vOut * Mathf.Lerp(0.35f, 1.9f, reach)) + Vector3.up * Random.Range(0.2f, 1.6f) + off * 2.4f + Random.insideUnitSphere * 0.4f;
                    WaterSim.Emit(spawn + v * (Time.deltaTime * Random.value), v);
                }
                hissTimer -= Time.deltaTime;
                if (hissTimer <= 0f)
                {
                    hissTimer = 1.1f;
                    if (SfxPlayer.I != null)
                        SfxPlayer.PlaySnippet(SfxPlayer.I.waterPour, lip + fwd * 1.5f, 1.8f, 0.85f);
                }
            }
            UpdateWater(h);
            yield return null;
        }
        h.fill = Mathf.Max(0f, h.fill);
        UpdateWater(h);
        yield return Tween(h, liftPos, Vector3.zero, new Vector3(tilt, 0f, 0f), Vector3.zero, 0.45f);
        h.busy = false;
    }

    // ---------- Kasik / spatula: kameraya kilitli, fareyle surulen kazima ----------
    // Alet ust tabakayi ince bir serit halinde soyar (ScrapeMarks), soyulan kabuk koseli obsidyen pulcuklarina doner
    // (ScrapeDebris): pulcuklar kepcenin ucuna tirmanip orada birikir, tasanlar onunde yigilip itilir.
    // Sol tik birakilinca alet yiginin altina girer, kepceyi doldurup kaldirir ve kaba doker.

    public bool botHoldScrape;                                  // test botu icin
    public Vector2 botScrapeDelta;                              // test botu: fare hareketi (piksel/kare)
    bool IsScraper => CurrentTool == Tool.Scraper;
    const float ScrapeMouseScale = 0.0026f;                     // fare pikseli -> metre
    // Kademeler: 0 kasik, 1 kepce, 2 spatula, 3 kucuk kurek, 4 orta kurek, 5 buyuk kurek
    static readonly float[] ScrapeToolScale = { 1.8f, 1.6f, 1.6f, 1.35f, 1.35f, 1.15f };   // kazima sirasinda alet boyu
    static float[] ScoopCapacity => Economy.ToolScoopGrams;                                // kepceye bir seferde sigan kirinti (gram)
    static readonly float[] ChipR = { 0.0088f, 0.0094f, 0.0107f, 0.0118f, 0.0128f, 0.0140f };
    static readonly float[] ChipRMin = { 0.0060f, 0.0064f, 0.0075f, 0.0082f, 0.0088f, 0.0095f };
    static readonly float[] ChipRMax = { 0.0110f, 0.0118f, 0.0135f, 0.0150f, 0.0162f, 0.0178f };
    static readonly float[] CamScale = { 1f, 1.05f, 1.1f, 1.3f, 1.45f, 1.6f };            // buyuk alet: kamera daha yukarida
    static readonly float[] TiltMin = { 18f, 18f, 18f, 20f, 22f, 30f };
    static readonly float[] TiltMax = { 68f, 68f, 68f, 55f, 52f, 50f };
    static readonly float[] ToolWeight = { 0f, 0.08f, 0.12f, 0.3f, 0.42f, 0.55f };        // agir alet fareyi gec takip eder
    public Material dustMaterial;
    // market icin: bicak genisligi (cm) ve agirlik (0..1)
    public static float ScraperWidthCm(int tier) { int t = Mathf.Clamp(tier, 0, ScrapeToolScale.Length - 1); return ScrapeToolShape.ForTier(t).halfWidth * 2f * ScrapeToolScale[t] * 100f; }
    public static float ScraperWeight(int tier) => ToolWeight[Mathf.Clamp(tier, 0, ToolWeight.Length - 1)];                                                          // kazima tozu (TorchSmoke)
    const float GramsPerM2 = 420f;                              // tamamen kazinan 1 m2 kabuktan cikan kirinti
    const float ChipsPerM2 = 380f;                              // gorunen pulcuk sayisi
    const float MoneyPerGram = 0.03f;

    public bool ScrapeActive => scrapeOn;
    public bool Scooping => scoopT >= 0f;
    public float TipLoad => debris != null ? debris.TipLoadGrams : 0f;
    public float TipCapacity => ScoopCapacity[ScrapeTier];
    public float LastScoopGrams { get; private set; }
    public int LastScoopMoney { get; private set; }
    int ScrapeTier => Mathf.Clamp(Economy.ToolTier, 0, ScoopCapacity.Length - 1);

    bool scrapeOn;
    Vector3 scrapeFocus, scrapeCamFocus, scrapeFwd, scrapeRight, scrapeToolDir = Vector3.forward;
    float scrapeSpeed, scrapeClinkT, scrapeRetry, scrapeVol, scrapeFxT, noScrapeT;
    GameObject scrapeTool;
    Light scrapeLight;
    AudioSource scrapeAudio;
    ScrapeDebris debris;
    ScrapeToolShape toolShape;
    float toolScale = 1.5f;
    float scrapeTilt = 38f;                     // fare tekerlegi: alet acisi (yatik = genis ve hafif, dik = dar ve derin)
    float toolTiltNow, pressDip, backLift, scrapeRoll, coreGrams;
    float chipBudget, gramsBudget, moneyAcc, tickT;
    Vector3 lastStamp, scrapeVel; bool hasStamp;
    Vector3 lockCamPos; Quaternion lockCamRot;
    Vector3 scrapeAim;                          // farenin gosterdigi nokta; alet agirligina gore onu takip eder
    float introT = 1f;                          // basta alet yukaridan inip yuzeye konar
    ParticleSystem dustPs;
    // toplama (kepceye alma)
    float scoopT = -1f;
    Vector3 scoopContact, scoopHead, scoopTarget;
    float scoopTilt0;
    int scoopChips, scoopCores;
    bool scoopPoured;
    float scoopGramsDone; int scoopMoneyDone, scoopCoresDone;

    void UpdateScrape(bool held, Vector2 md)
    {
        var field = LavaField.I;
        if (scoopT >= 0f)
        {
            StepScoop();
            UpdateScrapeAudio();
            return;
        }
        if (!scrapeOn)
        {
            if (held && field != null && !right.busy && !ShopKeeper.Open && Time.time >= scrapeRetry) BeginScrape(field);
        }
        else if (field == null || ShopKeeper.Open || Cursor.lockState != CursorLockMode.Locked)
        {
            EndScrape();
        }
        else if (!held)
        {
            BeginScoop();
            UpdateScrapeAudio();
            return;
        }
        if (scrapeOn) StepScrape(field, md);
        UpdateScrapeAudio();
    }

    void BeginScrape(LavaField field)
    {
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!field.Raycast(ray, reach, out var p) || !field.IsLava(p, 1.5f))
        {
            SetHint("Burada kazacak bir sey yok", 0.8f);
            scrapeRetry = Time.time + 0.4f;
            return;
        }
        scrapeOn = true;
        scrapeFocus = p;
        scrapeCamFocus = p;
        Vector3 f = player.transform.forward; f.y = 0f;
        scrapeFwd = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
        scrapeRight = Vector3.Cross(Vector3.up, scrapeFwd);
        scrapeSpeed = 0f; scrapeToolDir = scrapeFwd; pressDip = 0f; hasStamp = false; noScrapeT = 0f;
        toolTiltNow = scrapeTilt;
        int tier = ScrapeTier;
        toolShape = ScrapeToolShape.ForTier(tier);
        toolScale = ScrapeToolScale[tier];
        scrapeTilt = Mathf.Clamp(scrapeTilt, TiltMin[tier], TiltMax[tier]);
        toolTiltNow = scrapeTilt;
        scrapeAim = p;
        introT = 0f;
        scrapeTool = Instantiate(ToolPrefab());
        scrapeTool.transform.localScale = Vector3.one * toolScale;
        foreach (var c in scrapeTool.GetComponentsInChildren<Collider>()) Destroy(c);
        var lg = new GameObject("ScrapeLight");
        scrapeLight = lg.AddComponent<Light>();
        scrapeLight.type = LightType.Point; scrapeLight.range = 2.6f; scrapeLight.intensity = 1.25f;
        scrapeLight.color = new Color(1f, 0.84f, 0.6f); scrapeLight.shadows = LightShadows.None;
        ScrapeMarks.Ensure().Focus(p);
        debris = ScrapeDebris.Ensure();
        debris.onGramsDelivered = OnChipsDelivered;
        debris.onCoreDelivered = OnCoreDelivered;
        PlaceScrapeTool(scrapeFocus, scrapeToolDir, toolTiltNow, 0.01f);
        if (field.CrustAt(p) >= 0.3f && field.BrokenAt(p) < 0.06f)
            SetHint("Burasi kirilmamis - once kirici (4) ile kir, sonra kazi", 2.4f);
        else
            SetHint($"Fareyle sur (her yone): kirintilar {ToolName} ucunda birikir  |  Birak: topla  |  Tekerlek: aci", 3.2f);
    }

    // Kilidi ve aleti kaldir; tasinan kirintilar varsa aninda teslim edilir
    void EndScrape()
    {
        if (debris != null)
        {
            debris.DeliverNow();
            debris.SetTool(false, Vector3.zero, Quaternion.identity, 1f, toolShape);
        }
        scoopT = -1f;
        scrapeOn = false;
        if (scrapeTool != null) Destroy(scrapeTool);
        if (scrapeLight != null) Destroy(scrapeLight.gameObject);
        scrapeTool = null; scrapeLight = null;
        if (player != null) player.SetCameraLock(false, Vector3.zero, Quaternion.identity);
        scrapeSpeed = 0f; scrapeVol = 0f;
        if (scrapeAudio != null) scrapeAudio.Stop();
    }

    // Aleti, kepce ucunun en alt noktasi 'contact'a degecek sekilde yerlestir.
    // head: aletin baktigi yatay yon; tilt: aletin yataydan asagi acisi (derece). Kepcenin acik yuzu one-yukari bakar.
    void PlaceScrapeTool(Vector3 contact, Vector3 head, float tilt, float lift)
    {
        if (scrapeTool == null) return;
        float a = tilt * Mathf.Deg2Rad;
        Vector3 tdir = head * Mathf.Cos(a) + Vector3.down * Mathf.Sin(a);
        Quaternion rot = Quaternion.LookRotation(tdir, Vector3.up) * Quaternion.Euler(0f, 0f, scoopT < 0f ? scrapeRoll : 0f);
        Vector3 pos = contact + Vector3.up * lift - rot * (toolShape.contact * toolScale);
        scrapeTool.transform.SetPositionAndRotation(pos, rot);
        if (debris != null) debris.SetTool(scoopT < 0f, pos, rot, toolScale, toolShape, TipCapacity, ChipR[ScrapeTier], CoreRoom());
    }

    void StepScrape(LavaField field, Vector2 md)
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        md += botScrapeDelta * (dt * 60f);                         // bot: 60 fps'teki piksel/kare
        var mouse = Mouse.current;
        if (mouse != null)
        {
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f) scrapeTilt = Mathf.Clamp(scrapeTilt + Mathf.Sign(wheel) * 5f, TiltMin[ScrapeTier], TiltMax[ScrapeTier]);
        }
        int tierNow = ScrapeTier;
        float tiltT = Mathf.InverseLerp(TiltMin[tierNow], TiltMax[tierNow], scrapeTilt);       // 0 = yatik, 1 = dik
        float cs = CamScale[tierNow];

        Vector3 prev = scrapeFocus;
        Vector3 np = scrapeAim + (scrapeRight * md.x + scrapeFwd * md.y) * ScrapeMouseScale;
        // Calisma alani: oyuncuya yakin bir halka, yalnizca lav golunun icinde
        Vector3 pp = player.transform.position;
        Vector3 d = new Vector3(np.x - pp.x, 0f, np.z - pp.z);
        float maxR = reach - 0.3f, minR = 1.0f;
        if (d.magnitude > maxR) d = d.normalized * maxR;
        if (d.magnitude < minR) d = (d.sqrMagnitude > 1e-6f ? d.normalized : scrapeFwd) * minR;
        np = new Vector3(pp.x + d.x, 0f, pp.z + d.z);
        np.y = field.HeightAt(np);
        if (!field.IsLava(np, 0.2f)) np = scrapeAim;
        scrapeAim = np;
        // Agir alet (kurek) fareyi biraz geriden, yumusakca izler: agirlik hissi
        float follow = Mathf.Lerp(60f, 9f, ToolWeight[tierNow]);
        np = Vector3.Lerp(prev, scrapeAim, 1f - Mathf.Exp(-follow * dt));
        np.y = field.HeightAt(np);
        scrapeFocus = np;

        Vector3 mv = np - prev; mv.y = 0f;
        scrapeVel = Vector3.Lerp(scrapeVel, mv / dt, 1f - Mathf.Exp(-20f * dt));
        float spd = mv.magnitude / dt;
        scrapeSpeed = Mathf.Lerp(scrapeSpeed, spd, 1f - Mathf.Exp(-12f * dt));
        float sf = Mathf.Clamp01(scrapeSpeed / 1.0f);
        // Alet surme yonune doner (360 derece): kasik nereye surulurse ucu o yone bakar, sap arkasinda kalir.
        // Yon, yumusatilmis hizdan alinir (fare titremesi aleti oynatmaz); agir aletler daha yavas doner.
        float align = 0f;
        Vector3 vdir = new Vector3(scrapeVel.x, 0f, scrapeVel.z);
        if (vdir.magnitude > 0.025f && mv.sqrMagnitude > 1e-8f)
        {
            vdir.Normalize();
            float cur = Mathf.Atan2(scrapeToolDir.x, scrapeToolDir.z) * Mathf.Rad2Deg;
            float want = Mathf.Atan2(vdir.x, vdir.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(cur, want);
            float maxRate = Mathf.Lerp(900f, 320f, ToolWeight[tierNow]) * Mathf.Clamp01(spd * 2.5f);
            float stepA = Mathf.Clamp(delta * (1f - Mathf.Exp(-9f * dt)), -maxRate * dt, maxRate * dt);
            cur += stepA;
            scrapeToolDir = new Vector3(Mathf.Sin(cur * Mathf.Deg2Rad), 0f, Mathf.Cos(cur * Mathf.Deg2Rad));
            align = Mathf.Clamp01(Vector3.Dot(mv.normalized, scrapeToolDir));
            // donerken alet hafifce yana yatar (bilek hareketi)
            scrapeRoll = Mathf.Lerp(scrapeRoll, Mathf.Clamp(stepA / Mathf.Max(dt, 1e-4f) * 0.035f, -14f, 14f), 1f - Mathf.Exp(-8f * dt));
        }
        else scrapeRoll = Mathf.Lerp(scrapeRoll, 0f, 1f - Mathf.Exp(-6f * dt));
        float drive = sf * align * align;

        // Kamera: noktanin hemen ustunde, neredeyse tepeden bakar; yumusakca takip eder
        scrapeCamFocus = Vector3.Lerp(scrapeCamFocus, scrapeFocus, 1f - Mathf.Exp(-8f * dt));
        Vector3 camPos = scrapeCamFocus - scrapeFwd * (0.42f * cs) + Vector3.up * (1.08f * cs);
        // sakin nefes salinimi + hizli surerken hafifce yaklasma
        float tb = Time.time;
        camPos += scrapeRight * (Mathf.Sin(tb * 0.7f) * 0.006f * cs) + Vector3.up * (Mathf.Sin(tb * 0.93f + 1f) * 0.004f * cs);
        camPos += (scrapeCamFocus - camPos) * (0.05f * sf);
        var terr = Terrain.activeTerrain;
        if (terr != null) camPos.y = Mathf.Max(camPos.y, terr.SampleHeight(camPos) + terr.transform.position.y + 0.6f);
        lockCamPos = camPos;
        lockCamRot = Quaternion.LookRotation((scrapeCamFocus + scrapeFwd * (0.06f * cs) - camPos).normalized, Vector3.up)
                   * Quaternion.Euler(0f, 0f, Mathf.Sin(tb * 0.6f) * 0.35f);
        player.SetCameraLock(true, lockCamPos, lockCamRot);
        ScrapeMarks.Ensure().Focus(scrapeFocus);

        // Alet: ucu yuzeye bastirilir; surerken hafifce egilir ve titrer, dururken birkac mm kalkar
        pressDip = Mathf.Lerp(pressDip, drive, 1f - Mathf.Exp(-10f * dt));
        float chatter = Mathf.Sin(Time.time * 37f) * 0.9f + Mathf.Sin(Time.time * 23f) * 0.6f;
        toolTiltNow = Mathf.Lerp(toolTiltNow, scrapeTilt + pressDip * 4f, 1f - Mathf.Exp(-12f * dt)) + chatter * pressDip * 0.5f;
        float back = mv.sqrMagnitude > 1e-8f ? Mathf.Clamp01(-Vector3.Dot(mv.normalized, scrapeToolDir)) * sf : 0f;
        backLift = Mathf.Lerp(backLift, back, 1f - Mathf.Exp(-10f * dt));
        float lift = (1f - Mathf.Clamp01(pressDip * 4f)) * 0.006f + backLift * 0.018f;
        // giris: alet yukaridan suzulup yuzeye konar, degdiginde hafif bir tik
        if (introT < 1f)
        {
            introT = Mathf.Min(1f, introT + dt / 0.3f);
            lift += (1f - Smooth(introT)) * 0.16f * cs;
            if (introT >= 1f) SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, scrapeFocus, 0.08f, Random.Range(1.5f, 1.8f), 0.05f);
        }
        PlaceScrapeTool(scrapeFocus, scrapeToolDir, toolTiltNow, lift);
        scrapeLight.transform.position = scrapeFocus + Vector3.up * 0.9f - scrapeFwd * 0.15f;

        ScrapeWork(field, drive, tiltT, dt);

        // Ince dokulu tikirdama (ASMR)
        scrapeClinkT -= dt;
        if (drive > 0.15f && scrapeClinkT <= 0f)
        {
            scrapeClinkT = Random.Range(0.09f, 0.24f) / (0.4f + drive);
            SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, scrapeFocus, 0.04f + 0.06f * drive, Random.Range(1.8f, 2.6f), 0.1f);
        }
        EmitDust(scrapeFocus, scrapeToolDir, drive, toolShape.halfWidth * toolScale);
        if (debris != null && debris.TipLoadGrams >= TipCapacity - 4f && Hint == "")
            SetHint($"{ToolName} dolu - birak ve topla", 1.2f);
    }

    // Bicagin gectigi yerde ust tabaka soyulur; soyulan alan kadar pulcuk bicagin onunden kepceye dogru kalkar
    void ScrapeWork(LavaField field, float drive, float tiltT, float dt)
    {
        int tier = ScrapeTier;
        Vector3 contact = scrapeFocus;
        Vector3 head = scrapeToolDir;
        Vector3 lateral = Vector3.Cross(Vector3.up, head);
        float hw = toolShape.halfWidth * toolScale * (toolShape.ellipse ? Mathf.Lerp(0.74f, 0.58f, tiltT) : Mathf.Lerp(0.95f, 0.85f, tiltT));
        float hl = Mathf.Min(hw * 0.8f, 0.012f + 0.012f * tiltT);
        Vector3 from = hasStamp ? lastStamp : contact;
        lastStamp = contact; hasStamp = true;
        float strength = drive * Mathf.Lerp(0.38f, 0.8f, tiltT);
        if (strength < 0.02f) return;
        float area = ScrapeMarks.Ensure().Stamp(from, contact, lateral, head, hw, hl, strength, field);
        if (area <= 1e-7f)
        {
            if (drive > 0.25f)
            {
                noScrapeT += dt;
                if (noScrapeT > 0.7f && Hint == "")
                    SetHint(field.CrustAt(contact) < 0.3f ? "Burada kabuk yok - once lavi suyla (1) sogut"
                          : field.BrokenAt(contact) < 0.06f ? "Burasi kirilmamis - once kirici (4) ile kir, sonra kazi"
                          : "Burasi kazindi - kirici (4) ile tekrar kir", 1.4f);
            }
            return;
        }
        noScrapeT = 0f;
        // ufalanmis tabaka alinir: kabuk incelir; tamamen incelince lav acilir ve golun seviyesi duser
        if (field.ConsumeRubble(from, contact, hw, area))
            SetHint("Kabuk bitti, lav acildi - suyla (1) tekrar sogut", 1.6f);
        coreGrams += area * GramsPerM2;
        float coreEvery = 220f / Mathf.Max(1, Economy.BreakerCores(Mathf.Max(1, Economy.BreakerTierNow)));
        if (coreGrams >= coreEvery) { coreGrams -= coreEvery; RevealCore(contact, head, hl); }
        gramsBudget += area * GramsPerM2;
        chipBudget += area * ChipsPerM2;
        int n = Mathf.Min(14, Mathf.FloorToInt(chipBudget));
        if (n <= 0) return;
        chipBudget -= n;
        float each = gramsBudget / n;
        gramsBudget = 0f;
        float rMin = ChipRMin[tier], rMax = ChipRMax[tier];
        float big = Mathf.Lerp(0.9f, 1.2f, tiltT);
        Vector3 toolV = new Vector3(scrapeVel.x, 0f, scrapeVel.z);
        var tt = scrapeTool.transform;
        Vector3 upBowl = tt.rotation * Vector3.back;            // kepcenin icine dogru (saba dogru) kayma yonu
        for (int i = 0; i < n; i++)
        {
            // Soyulan kabuk ucun kenarinda kalkar ve kepcenin icine kivrilir (kasigin ucunda birikir)
            float r = Random.Range(rMin, rMax) * big;
            float uz = Random.Range(0.45f, 0.85f);
            float z = toolShape.zCenter + toolShape.halfLen * uz;
            float xw = toolShape.ellipse ? toolShape.halfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - uz * uz)) : toolShape.halfWidth;
            float x = Random.Range(-0.75f, 0.75f) * xw;
            float ys = toolShape.Surface(x, z, 0f, out _, out _);
            Vector3 pos = tt.TransformPoint(new Vector3(x, ys + r / toolScale * 1.05f, z));
            Vector3 vel = toolV * Random.Range(0.95f, 1.05f) + upBowl * (Random.Range(0.08f, 0.3f) * drive)
                        + Vector3.up * (Random.Range(0.03f, 0.18f) * drive);
            debris.SpawnChip(pos, vel, r, each);
        }
        scrapeFxT -= dt;
        if (scrapeFxT <= 0f && drive > 0.2f) { scrapeFxT = Mathf.Lerp(0.25f, 0.1f, drive); Fx.Chips(contact + head * 0.02f + Vector3.up * 0.02f, 2); }
    }

    bool EnsureDust()
    {
        if (dustMaterial == null) return false;
        if (dustPs != null) return true;
        var go = new GameObject("ScrapeDust");
        dustPs = go.AddComponent<ParticleSystem>();
        dustPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = dustPs.main;
        m.loop = false; m.playOnAwake = false; m.maxParticles = 220;
        m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.12f);
        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new Color(0.32f, 0.27f, 0.3f, 0.32f);
        m.gravityModifier = -0.02f;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = dustPs.emission; em.enabled = false;
        var sh = dustPs.shape; sh.enabled = false;
        var sz = dustPs.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f)));
        var col = dustPs.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        go.GetComponent<ParticleSystemRenderer>().sharedMaterial = dustMaterial;
        dustPs.Play();
        return true;
    }

    // Bicagin arkasinda ince obsidyen tozu kalkar (hiz ve bicak genisligiyle)
    float dustAcc;
    void EmitDust(Vector3 contact, Vector3 head, float drive, float halfWidth)
    {
        if (drive < 0.12f || !EnsureDust()) return;
        dustAcc += Time.deltaTime * drive * (18f + halfWidth * 160f);
        Vector3 lat = Vector3.Cross(Vector3.up, head);
        while (dustAcc >= 1f)
        {
            dustAcc -= 1f;
            var ep = new ParticleSystem.EmitParams
            {
                position = contact - head * Random.Range(0.01f, 0.05f) + lat * (Random.Range(-1f, 1f) * halfWidth) + Vector3.up * 0.01f,
                velocity = -head * Random.Range(0.02f, 0.1f) + Vector3.up * Random.Range(0.03f, 0.12f) + lat * Random.Range(-0.05f, 0.05f),
                applyShapeToPosition = false
            };
            dustPs.Emit(ep, 1);
        }
    }

    // Kaplarda kac cevherlik yer var (CollectCore ile ayni kural)
    int CoreRoom()
    {
        int room = 0;
        foreach (var h in new[] { left, right })
        {
            if (!h.has) continue;
            if (h.ores > 0) room += Mathf.Max(0, OreCap - h.ores);
            else if (h.fill < 0.05f) room += OreCap;
        }
        return room;
    }

    // ---------- Toplama: kepceyi yiginin altina sok, kaldir, kaba dok ----------

    const float ScoopSlide = 0.16f, ScoopLift = 0.42f, ScoopPour = 0.68f, ScoopEnd = 1.05f;

    void BeginScoop()
    {
        if (scrapeTool == null || debris == null) { EndScrape(); return; }
        scoopT = 0f;
        scoopContact = scrapeFocus;
        scoopHead = scrapeToolDir;
        scoopTilt0 = toolTiltNow;
        scoopPoured = false;
        scoopGramsDone = 0f; scoopMoneyDone = 0; scoopCoresDone = 0;
        // kepce one kayarken yigin kaseye dolar
        PlaceScrapeTool(scoopContact + scoopHead * 0.03f, scoopHead, 14f, 0f);
        debris.Capture(scrapeTool.transform, TipCapacity, CoreRoom(), out scoopCores, out scoopChips);
        debris.SetTool(false, Vector3.zero, Quaternion.identity, 1f, toolShape);
        PlaceScrapeTool(scoopContact, scoopHead, scoopTilt0, 0f);
        scrapeSpeed = 0f;
        // kaplarin agzi: ekranin alt-sag kosesinin hemen disi (oyuncunun elindeki kap)
        scoopTarget = lockCamPos + lockCamRot * new Vector3(0.22f, -0.42f, 0.38f);
        if (scoopChips + scoopCores > 0)
            SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, scoopContact, 0.12f, Random.Range(1.4f, 1.7f), 0.05f);
    }

    void StepScoop()
    {
        float dt = Time.deltaTime;
        scoopT += dt;
        bool empty = scoopChips + scoopCores == 0;
        float tEnd = empty ? 0.28f : ScoopEnd;
        player.SetCameraLock(true, lockCamPos, lockCamRot);

        Vector3 contact; float tilt;
        if (empty)
        {
            float u = Mathf.Clamp01(scoopT / tEnd);
            contact = scoopContact + Vector3.up * (0.12f * u * u);
            tilt = Mathf.Lerp(scoopTilt0, scoopTilt0 + 12f, u);
        }
        else if (scoopT < ScoopSlide)
        {
            // kepce yatar ve yiginin altina kayar
            float u = Smooth(scoopT / ScoopSlide);
            contact = scoopContact + scoopHead * (0.03f * u);
            tilt = Mathf.Lerp(scoopTilt0, 14f, u);
        }
        else if (scoopT < ScoopLift)
        {
            // kaldir: kase duzlesir, dolu kepce havaya kalkar
            float u = Smooth((scoopT - ScoopSlide) / (ScoopLift - ScoopSlide));
            contact = scoopContact + scoopHead * 0.03f + Vector3.up * (0.2f * CamScale[ScrapeTier] * u);
            tilt = Mathf.Lerp(14f, -4f, u);
        }
        else
        {
            // kaba dogru gotur ve dok
            float u = Smooth(Mathf.Clamp01((scoopT - ScoopLift) / (ScoopPour - ScoopLift)));
            Vector3 up0 = scoopContact + scoopHead * 0.03f + Vector3.up * (0.2f * CamScale[ScrapeTier]);
            contact = Vector3.Lerp(up0, Vector3.Lerp(up0, scoopTarget, 0.55f), u);
            tilt = Mathf.Lerp(-4f, 35f, u);
            if (!scoopPoured && scoopT >= ScoopLift + (ScoopPour - ScoopLift) * 0.45f)
            {
                scoopPoured = true;
                debris.Deliver(scoopTarget, 0.28f);
            }
        }
        PlaceScrapeTool(contact, scoopHead, tilt, 0f);
        if (scrapeLight != null) scrapeLight.transform.position = contact + Vector3.up * 0.8f;

        if (scoopT >= tEnd)
        {
            debris.DeliverNow();                 // gecikenler de sayilsin
            LastScoopGrams = scoopGramsDone; LastScoopMoney = scoopMoneyDone;
            if (!empty)
            {
                string cores = scoopCoresDone > 0 ? $" + {scoopCoresDone} cevher kaba" : "";
                SetHint($"+{scoopMoneyDone} para  ({scoopGramsDone:F0} g obsidyen kirintisi{cores})", 1.8f);
            }
            EndScrape();
        }
    }

    static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }

    float tinkT;
    void OnChipsDelivered(float grams, Vector3 p)
    {
        scoopGramsDone += grams;
        moneyAcc += grams * MoneyPerGram;
        while (moneyAcc >= 1f)
        {
            moneyAcc -= 1f;
            scoopMoneyDone++;
            if (Economy.I != null) Economy.I.Earn(1);
        }
        if (Time.time >= tinkT)
        {
            tinkT = Time.time + 0.035f;
            SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 0.05f, Random.Range(2.6f, 3.4f), 0.05f);
        }
    }

    void OnCoreDelivered(LithicCoreItem core)
    {
        if (core == null) return;
        if (CollectCore(core)) { scoopCoresDone++; return; }
        // kaplar doldu: cevher oyuncunun onune duser
        Vector3 p = player.transform.position + player.transform.forward * 1.2f + Vector3.up * 0.6f;
        core.transform.position = p;
        core.Launch(new Vector3(Random.Range(-0.4f, 0.4f), 1.2f, Random.Range(-0.4f, 0.4f)));
        SetHint("Kaplar dolu - cevherleri depoya at", 1.2f);
    }

    // Kirinti/cevher parcasini bir kaba koyar (once yarim dolu kap, sonra bos kap); sigmazsa false
    bool CollectCore(LithicCoreItem core)
    {
        Hand target = null;
        foreach (var h in new[] { left, right })
            if (h.has && h.ores > 0 && h.ores < OreCap) { target = h; break; }
        if (target == null)
            foreach (var h in new[] { left, right })
                if (h.has && h.ores == 0 && h.fill < 0.05f) { target = h; break; }
        if (target == null) return false;
        Destroy(core.gameObject);
        target.ores++;
        if (target.item != null)
        {
            var old = target.item.transform.Find("Ores");
            if (old != null) Destroy(old.gameObject);
            BuildOreViews(target);
        }
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, scrapeFocus, 0.12f, Random.Range(2.0f, 2.8f), 0.1f);
        SetHint($"Toplandi ({CoreCount}/{CoreCapacity})", 0.8f);
        return true;
    }

    // Kum/cakil kazima sesi: surekli, yumusak bantli gurultu; fare hizina gore ses ve ton degisir
    void UpdateScrapeAudio()
    {
        if (!scrapeOn) return;
        if (scrapeAudio == null)
        {
            scrapeAudio = gameObject.AddComponent<AudioSource>();
            scrapeAudio.clip = MakeScrapeClip();
            scrapeAudio.loop = true; scrapeAudio.playOnAwake = false; scrapeAudio.spatialBlend = 0f; scrapeAudio.volume = 0f;
        }
        float sf = Mathf.Clamp01(scrapeSpeed / 1.4f);
        float target = 0.34f * Mathf.Pow(sf, 0.8f);
        scrapeVol = Mathf.MoveTowards(scrapeVol, target, Time.deltaTime * 1.6f);
        scrapeAudio.volume = scrapeVol;
        scrapeAudio.pitch = (0.88f + 0.5f * sf) * Mathf.Lerp(1.08f, 0.74f, ScrapeTier / 5f);
        if (scrapeVol > 0.004f && !scrapeAudio.isPlaying) scrapeAudio.Play();
        else if (scrapeVol <= 0.004f && scrapeAudio.isPlaying) scrapeAudio.Pause();
    }

    static AudioClip MakeScrapeClip()
    {
        const int rate = 24000;
        int n = rate * 2;
        var data = new float[n];
        var rng = new System.Random(7);
        float lp1 = 0f, lp2 = 0f, grain = 0f, gTarget = 0.5f, peak = 0.0001f;
        int cnt = 0;
        for (int i = 0; i < n; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            lp1 += 0.55f * (x - lp1);
            lp2 += 0.10f * (x - lp2);
            if (--cnt <= 0) { gTarget = (float)rng.NextDouble(); cnt = rng.Next(40, 400); }
            grain += 0.01f * (gTarget - grain);
            float s = (lp1 - lp2) * (0.35f + 0.9f * grain);
            data[i] = s;
            peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        for (int i = 0; i < n; i++) data[i] *= 0.8f / peak;
        var clip = AudioClip.Create("ScrapeNoise", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }


    // Kirici ust tabakayi tamamen ufaladi: parcalara ayrilip sacilmaz, yerinde kazinmaya hazir yumusak bir
    // kirinti tabakasina doner (toz kalkar, catlaklar acilir).
    void OnObsidianBroken(Vector3 p)
    {
        Fx.Chips(p + Vector3.up * 0.04f, 5);
        CrumbleDust(p, 1f);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 0.75f, 0.85f);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.obsidianBreak : null, p, 0.4f, 0.7f);
        player.Shake(0.02f, 0.12f);
        int earn = Economy.I != null ? Economy.BreakerMoney(Economy.BreakerTierNow) : 0;
        if (earn > 0) Economy.I.Earn(earn);
        SetHint(earn > 0 ? $"+{earn} para  |  Ufalandi - kaziyici (3) ile kazi" : "Ufalandi - kaziyici (3) ile kazi", 1.4f);
    }

    // Kirilan yerde alcak, yayilan toz bulutu (parca sacilmaz)
    void CrumbleDust(Vector3 p, float amount)
    {
        if (!EnsureDust()) return;
        int n = Mathf.Max(1, Mathf.RoundToInt(16f * amount));
        for (int i = 0; i < n; i++)
        {
            Vector2 o = Random.insideUnitCircle * 0.35f;
            var ep = new ParticleSystem.EmitParams
            {
                position = p + new Vector3(o.x, 0.02f, o.y),
                velocity = new Vector3(o.x * 0.6f, Random.Range(0.05f, 0.25f), o.y * 0.6f),
                startSize = Random.Range(0.08f, 0.2f),
                startLifetime = Random.Range(0.8f, 1.6f),
                applyShapeToPosition = false
            };
            dustPs.Emit(ep, 1);
        }
    }

    // Kazinan kirintinin arasindan cevher cikar: bicagin onunde belirir, kaziyici onu da kepceye alir
    void RevealCore(Vector3 contact, Vector3 head, float hl)
    {
        if (coreItemPrefab == null) return;
        Vector3 pos = contact + head * (hl + 0.06f);
        var field = LavaField.I;
        if (field != null) pos.y = field.HeightAt(pos) + 0.03f;
        var go = Instantiate(coreItemPrefab, pos, Random.rotation);
        go.transform.localScale *= Random.Range(0.42f, 0.55f);
        var item = go.GetComponent<LithicCoreItem>();
        if (item != null) item.Launch(head * 0.25f + Vector3.up * 0.55f);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, pos, 0.14f, Random.Range(1.2f, 1.4f), 0.05f);
        if (Hint == "") SetHint("Cevher cikti! Kepceye al, kaba koy", 1.2f);
    }

    // ---------- Kiricilar: cekic/balyoz (vur), matkap/kirici (sol tik basili tut) ----------

    public bool botHoldBreak;                                   // test botu icin
    GameObject BreakerPrefab(int t) { return breakerPrefabs != null && t >= 0 && t < breakerPrefabs.Length ? breakerPrefabs[t] : null; }

    float drillTickT, drillVibe, drillVol, drillFxT;
    AudioSource drillAudio;
    AudioClip drillClip, jackClip;

    void UpdateBreaker(bool held)
    {
        int t = Economy.BreakerTierNow;
        if (t <= 0 || CurrentTool != Tool.Breaker) { StopDrill(); return; }
        var kind = Economy.KindOf(t);
        if (kind == Economy.BreakerKind.Hammer)
        {
            if (held && !right.busy && Time.time >= nextSwing) StartCoroutine(SwingHammer(t));
            return;
        }
        UpdateDrill(held && !right.busy && !ShopKeeper.Open, t, kind);
    }

    void StopDrill()
    {
        drillVibe = 0f; drillVol = 0f; drillTickT = 0f;
        if (drillAudio != null) drillAudio.Stop();
    }

    // Matkap / kirici: tus basili oldukca nisan noktasini surekli deler; el titrer, kamera sarsilir
    void UpdateDrill(bool held, int t, Economy.BreakerKind kind)
    {
        float dt = Time.deltaTime;
        bool jack = kind == Economy.BreakerKind.Jackhammer;
        drillVibe = Mathf.MoveTowards(drillVibe, held ? 1f : 0f, dt * (held ? 6f : 8f));
        if (right.item != null && !right.busy)
        {
            float amp = (jack ? 0.014f : 0.007f) * drillVibe;
            float ph = Time.time;
            right.animPos = new Vector3(Mathf.Sin(ph * 83f), Mathf.Sin(ph * 97f + 1f), Mathf.Sin(ph * 71f + 2f) * 0.6f) * amp
                            + new Vector3(0f, 0f, 0.035f * drillVibe);
            right.animEuler = new Vector3(Mathf.Sin(ph * 67f) * (jack ? 1.6f : 0.8f), Mathf.Sin(ph * 59f + 1f) * (jack ? 1.2f : 0.6f), 0f) * drillVibe;
        }
        if (held)
        {
            float interval = Economy.BreakerTempo(t);
            drillTickT += dt;
            int guard = 0;
            while (drillTickT >= interval && guard++ < 4) { drillTickT -= interval; DrillHit(t); }
            player.Shake(jack ? 0.012f : 0.005f, 0.06f);
        }
        else drillTickT = 0f;
        UpdateDrillAudio(held, t, jack);
    }

    void DrillHit(int t)
    {
        var field = LavaField.I;
        if (field == null) return;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!field.Raycast(ray, reach, out var p)) return;
        Vector2 j = Random.insideUnitCircle * (Economy.BreakerRadius(t) * 0.25f);
        p += new Vector3(j.x, 0f, j.y);
        p.y = field.HeightAt(p);
        if (!field.IsLava(p, 1.0f)) { if (Hint == "") SetHint("Burada kazacak bir sey yok", 0.8f); return; }
        switch (field.Mine(p, Economy.BreakerRadius(t), Economy.BreakerPower(t)))
        {
            case MineResult.None:
                if (Hint == "") SetHint(field.DigAt(p) > 0.97f ? "Ana kayaya ulastin" : "Once lavi suyla (1) sogut", 0.8f);
                break;
            case MineResult.Chipped:
                drillFxT -= Economy.BreakerTempo(t);
                if (drillFxT <= 0f) { drillFxT = 0.09f; Fx.Chips(p + Vector3.up * 0.03f, 1); CrumbleDust(p, 0.12f); }
                break;
            case MineResult.AlreadySoft:
                drillFxT -= Economy.BreakerTempo(t);
                if (drillFxT <= 0f) { drillFxT = 0.2f; CrumbleDust(p, 0.08f); }
                if (Hint == "") SetHint("Burasi ufalandi - kaziyici (3) ile kazi", 1f);
                break;
            case MineResult.Broken:
                OnObsidianBroken(p);
                break;
        }
    }

    void UpdateDrillAudio(bool held, int t, bool jack)
    {
        if (drillAudio == null)
        {
            drillAudio = gameObject.AddComponent<AudioSource>();
            drillAudio.loop = true; drillAudio.playOnAwake = false; drillAudio.spatialBlend = 0f; drillAudio.volume = 0f;
        }
        if (drillClip == null) drillClip = MakeDrillClip();
        if (jackClip == null) jackClip = MakeJackClip();
        var clip = jack ? jackClip : drillClip;
        if (drillAudio.clip != clip) { drillAudio.Stop(); drillAudio.clip = clip; }
        drillVol = Mathf.MoveTowards(drillVol, held ? (jack ? 0.55f : 0.36f) : 0f, Time.deltaTime * 3.5f);
        drillAudio.volume = drillVol;
        drillAudio.pitch = jack ? 1f : Mathf.Clamp(0.75f + (t - 9) * 0.05f, 0.7f, 1.2f);
        if (drillVol > 0.004f && !drillAudio.isPlaying) drillAudio.Play();
        else if (drillVol <= 0.004f && drillAudio.isPlaying) drillAudio.Pause();
    }

    // Matkap vizildisi: 150 Hz testere dalgasi + hafif gurultu, 30 Hz titresim (1 sn'de tam dongu = dikissiz)
    static AudioClip MakeDrillClip()
    {
        const int rate = 22050;
        var data = new float[rate];
        var rng = new System.Random(11);
        float peak = 0.0001f;
        for (int i = 0; i < rate; i++)
        {
            float t = i / (float)rate;
            float saw = 0f;
            for (int h = 1; h <= 8; h++) saw += Mathf.Sin(2f * Mathf.PI * 150f * h * t) / h;
            float am = 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * 30f * t);
            float s = saw * 0.4f * am + ((float)rng.NextDouble() - 0.5f) * 0.35f;
            data[i] = s; peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        for (int i = 0; i < rate; i++) data[i] *= 0.8f / peak;
        var clip = AudioClip.Create("DrillLoop", rate, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Kirici: saniyede 20 agir darbe (gurultu patlamasi + 60 Hz gumbur)
    static AudioClip MakeJackClip()
    {
        const int rate = 22050;
        var data = new float[rate];
        var rng = new System.Random(5);
        float peak = 0.0001f;
        for (int i = 0; i < rate; i++)
        {
            float t = i / (float)rate;
            float p = (t * 20f) % 1f;
            float env = Mathf.Exp(-p * 16f);
            float s = (Mathf.Sin(2f * Mathf.PI * 60f * t) * 0.9f + ((float)rng.NextDouble() * 2f - 1f) * 0.7f) * env;
            data[i] = s; peak = Mathf.Max(peak, Mathf.Abs(s));
        }
        for (int i = 0; i < rate; i++) data[i] *= 0.85f / peak;
        var clip = AudioClip.Create("JackLoop", rate, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // Cekic / balyoz: kaldirip indirir; agir olan daha yavas ve daha sert vurur
    IEnumerator SwingHammer(int t)
    {
        right.busy = true;
        float sw = Economy.BreakerTempo(t);
        float heavy = Mathf.Clamp01((t - 1) / 7f);
        Vector3 windPos = new Vector3(0.05f, 0.18f + 0.05f * heavy, -0.15f);
        Vector3 windEul = new Vector3(-55f - 15f * heavy, 0f, -10f);
        Vector3 hitPos = new Vector3(-0.05f, -0.12f, 0.28f);
        Vector3 hitEul = new Vector3(40f + 8f * heavy, 0f, 0f);

        yield return Tween(right, Vector3.zero, windPos, Vector3.zero, windEul, 0.22f * sw);
        yield return Tween(right, windPos, hitPos, windEul, hitEul, 0.11f * sw);

        // Vurus aninda
        var field = LavaField.I;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (field != null && field.Raycast(ray, reach, out var p))
        {
            if (!field.IsLava(p, 1.5f)) { SetHint("Burada kazacak bir sey yok"); }
            else
            {
                switch (field.Mine(p, Economy.BreakerRadius(t), Economy.BreakerPower(t)))
                {
                    case MineResult.None:
                        SetHint(field.DigAt(p) > 0.97f ? "Ana kayaya ulastin" : "Once lavi suyla (1) sogut");
                        break;
                    case MineResult.Chipped:
                        Fx.Chips(p + Vector3.up * 0.04f, 3);
                        CrumbleDust(p, 0.45f);
                        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 1f, 1f - 0.12f * heavy);
                        player.Shake(0.02f + 0.02f * heavy, 0.1f);
                        break;
                    case MineResult.AlreadySoft:
                        CrumbleDust(p, 0.3f);
                        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 0.5f, 0.7f);
                        SetHint("Burasi zaten ufalandi - kaziyici (3) ile kazi", 1.4f);
                        break;
                    case MineResult.Broken:
                        OnObsidianBroken(p);
                        break;
                }
            }
        }
        yield return Tween(right, hitPos, Vector3.zero, hitEul, Vector3.zero, 0.3f * sw);
        nextSwing = Time.time + SwingPause;
        right.busy = false;
    }

    public GameObject coreItemPrefab;

    void SpawnCores(Vector3 p)
    {
        if (coreItemPrefab == null) return;
        // kirici obsidyeni parcalara boler: kucuk kirintilar etrafa saçilir, sonra kaziyiciyla toplanir
        int n = Random.Range(2, 5) + (CurrentTool == Tool.Breaker ? Economy.BreakerCores(Economy.BreakerTierNow) - 1 : 0);
        for (int i = 0; i < n; i++)
        {
            var go = Instantiate(coreItemPrefab, p + Vector3.up * 0.3f, Random.rotation);
            go.transform.localScale *= Random.Range(0.45f, 0.7f);
            var item = go.GetComponent<LithicCoreItem>();
            if (item != null)
                item.Launch(new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(2f, 3.2f), Random.Range(-1.2f, 1.2f)));
        }
    }

    bool depositing;

    // Cevherleri kovalardan tek tek al, hazneye at (sirayla: sol kova, sag kova)
    IEnumerator DepositRoutine()
    {
        depositing = true;
        var bin = OreBin.I;
        if (bin.orePrefab == null) bin.orePrefab = coreVisualPrefab;
        int moved = 0;
        foreach (var h in new[] { left, right })
        {
            while (h.ores > 0 && !bin.Full)
            {
                Vector3 side = cam.transform.right * (h == left ? -0.28f : 0.28f);
                Vector3 from = cam.transform.position + cam.transform.forward * 0.55f + side + Vector3.down * 0.3f;
                h.ores--; moved++;
                bin.Toss(from);
                if (Economy.I != null) Economy.I.Earn(Economy.OreValue);
                if (h.item != null)
                {
                    var old = h.item.transform.Find("Ores");
                    if (old != null) Destroy(old.gameObject);
                    BuildOreViews(h);
                }
                SetHint($"Depoya atiliyor... ({bin.Count}/{bin.capacity})");
                yield return new WaitForSeconds(0.14f);
            }
        }
        yield return new WaitForSeconds(0.5f);
        SetHint($"{moved} cevher depoya dizildi (+{moved * Economy.OreValue} para)");
        depositing = false;
    }

    // ---------- Litik cekirdek toplama ----------

    void TryPickup()
    {
        var core = LithicCoreItem.Nearest(transform.position, pickupRange);
        if (core == null)
        {
            var eRay = new Ray(cam.transform.position, cam.transform.forward);
            bool lookWell = false;
            foreach (var well in WaterWell.All)
                if (well != null && well.LooksAt(eRay.origin, eRay.direction, reach + 1f)) { lookWell = true; break; }
            var dropped = lookWell ? null : DroppedItem.Nearest(transform.position, 2.6f);
            if (dropped != null) { PickUpDropped(dropped); return; }
            if (OreBin.I != null && OreBin.I.LooksAt(eRay, reach + 1f))
            {
                int have = CoreCount;
                if (depositing) return;
                if (have == 0) { SetHint($"Depo: {OreBin.I.Count}/{OreBin.I.capacity} (kovalarda cevher yok)"); return; }
                if (OreBin.I.Full) { SetHint("Depo dolu (500/500)"); return; }
                StartCoroutine(DepositRoutine());
                return;
            }
            if (lookWell) return;      // kuyu E tusunu kendi yonetir (basili tut: indir, doluyken bas: cek)
            return;
        }
        // Cevher kovaya girer: once yarisi dolu kova, sonra bos kova (kovada su varsa girmez)
        Hand target = null;
        foreach (var h in new[] { left, right })
            if (h.has && h.ores > 0 && h.ores < OreCap) { target = h; break; }
        if (target == null)
            foreach (var h in new[] { left, right })
                if (h.has && h.ores == 0 && h.fill < 0.05f) { target = h; break; }
        if (target == null)
        {
            SetHint(!left.has && !right.has ? "Elinde kap yok" : CoreCount >= CoreCapacity ? $"Kaplar dolu ({VesselName}: {OreCap} cevher)" : "Kapta su var - once bosalt");
            return;
        }
        Destroy(core.gameObject);
        target.ores++;
        if (target.item != null)
        {
            var old = target.item.transform.Find("Ores");
            if (old != null) Destroy(old.gameObject);
            BuildOreViews(target);
        }
        SetHint($"Cevher kovaya girdi ({CoreCount}/{CoreCapacity})");
        StartCoroutine(ReachLeft());
    }

    // ---------- Yere birakma / alma ----------

    Vector3 DropPoint() { return cam.transform.position + cam.transform.forward * 0.9f + Vector3.down * 0.45f; }

    void DropBucket(Hand h)
    {
        if (!h.has || h.busy || CurrentTool != Tool.Buckets || h.item == null) return;
        var item = h.item;
        h.item = null; h.water = null;
        float fill = h.fill; int ores = h.ores;
        h.fill = 0f; h.ores = 0; h.has = false;
        h.animPos = Vector3.zero; h.animEuler = Vector3.zero;
        DroppedItem.Create(item, DroppedItem.Kind.Bucket, DropPoint(), cam.transform.forward * 1.5f + Vector3.up * 0.8f, fill, ores);
        SetHint(h == left ? "Sol kova birakildi (E: al)" : "Sag kova birakildi (E: al)");
    }

    void PickUpDropped(DroppedItem d)
    {
        // Kovalar sirayla alinir: once sol, sonra sag
        Hand h = !left.has ? left : (!right.has ? right : null);
        if (h == null) { SetHint("Iki kovan da elinde"); return; }
        h.has = true; h.fill = d.fill; h.ores = d.ores;
        Destroy(d.gameObject);
        if (CurrentTool == Tool.Buckets) SpawnBucket(h);
        SetHint(h == left ? "Sol kova alindi" : "Sag kova alindi");
    }

    IEnumerator ReachLeft()
    {
        if (left.busy) yield break;
        left.busy = true;
        Vector3 reachPos = new Vector3(0.05f, -0.1f, 0.3f);
        yield return Tween(left, Vector3.zero, reachPos, Vector3.zero, new Vector3(30f, 0f, 0f), 0.2f);
        yield return Tween(left, reachPos, Vector3.zero, new Vector3(30f, 0f, 0f), Vector3.zero, 0.25f);
        left.busy = false;
    }

    // ---------- Yardimci ----------

    IEnumerator Tween(Hand h, Vector3 p0, Vector3 p1, Vector3 e0, Vector3 e1, float dur)
    {
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float k = t / dur;
            k = k * k * (3f - 2f * k);
            h.animPos = Vector3.Lerp(p0, p1, k);
            h.animEuler = Vector3.Lerp(e0, e1, k);
            yield return null;
        }
        h.animPos = p1;
        h.animEuler = e1;
    }
}

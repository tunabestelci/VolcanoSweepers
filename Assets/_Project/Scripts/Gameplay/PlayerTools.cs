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

    void Start() { RefreshLoadout(); }       // Economy hazir olduktan sonra dogru kademeyle yeniden kur

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
                // Kasik/spatula daha kucuk: kameraya yakin ve gorunur durur, kazirken ucu nisana iner
                right.readyPos = new Vector3(0.28f, -0.28f, 0.54f);
                right.readyEuler = new Vector3(12f, -6f, -8f);
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

    // ---------- Kasik / spatula: kameraya kilitli, fareyle surulen kazima (Moon Sweepers tarzi) ----------

    public bool botHoldScrape;                                  // test botu icin
    public Vector2 botScrapeDelta;                              // test botu: fare hareketi (piksel/kare)
    bool IsScraper => CurrentTool == Tool.Scraper;
    const float ScrapeTick = 0.08f;                             // kazima darbesi araligi
    const float ScrapePowerFactor = 0.5f;                       // darbe basina guc (kademenin gucunun orani)
    const float ScrapeMouseScale = 0.0032f;                     // fare pikseli -> metre
    bool scrapeOn;
    Vector3 scrapeFocus, scrapeCamFocus, scrapeFwd, scrapeRight, scrapeToolDir = Vector3.forward;
    float scrapeSpeed, scrapeTickT, scrapeFxT, scrapeClinkT, scrapeRetry, scrapeVol;
    GameObject scrapeTool;
    Light scrapeLight;
    AudioSource scrapeAudio;

    void UpdateScrape(bool held, Vector2 md)
    {
        var field = LavaField.I;
        if (!scrapeOn)
        {
            if (held && field != null && !right.busy && !ShopKeeper.Open && Time.time >= scrapeRetry) BeginScrape(field);
        }
        else if (!held || field == null || ShopKeeper.Open || Cursor.lockState != CursorLockMode.Locked)
        {
            EndScrape();
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
        scrapeSpeed = 0f; scrapeTickT = 0f; scrapeToolDir = scrapeFwd;
        scrapeTool = Instantiate(ToolPrefab());
        scrapeTool.transform.localScale = Vector3.one * 1.5f;
        foreach (var c in scrapeTool.GetComponentsInChildren<Collider>()) Destroy(c);
        var lg = new GameObject("ScrapeLight");
        scrapeLight = lg.AddComponent<Light>();
        scrapeLight.type = LightType.Point; scrapeLight.range = 3.2f; scrapeLight.intensity = 1.4f;
        scrapeLight.color = new Color(1f, 0.82f, 0.55f); scrapeLight.shadows = LightShadows.None;
        SetHint("Fareyi surerek kaz", 1.6f);
    }

    void EndScrape()
    {
        scrapeOn = false;
        if (scrapeTool != null) Destroy(scrapeTool);
        if (scrapeLight != null) Destroy(scrapeLight.gameObject);
        scrapeTool = null; scrapeLight = null;
        if (player != null) player.SetCameraLock(false, Vector3.zero, Quaternion.identity);
        scrapeSpeed = 0f; scrapeVol = 0f;
        if (scrapeAudio != null) scrapeAudio.Stop();
    }

    void StepScrape(LavaField field, Vector2 md)
    {
        int tier = Mathf.Clamp(Economy.ToolTier, 0, Economy.ToolNames.Length - 1);
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);
        md += botScrapeDelta;
        Vector3 prev = scrapeFocus;
        Vector3 np = prev + (scrapeRight * md.x + scrapeFwd * md.y) * ScrapeMouseScale;
        // Calisma alani: oyuncuya yakin bir halka, yalnizca lav golunun icinde
        Vector3 pp = player.transform.position;
        Vector3 d = new Vector3(np.x - pp.x, 0f, np.z - pp.z);
        float maxR = reach - 0.3f, minR = 1.0f;
        if (d.magnitude > maxR) d = d.normalized * maxR;
        if (d.magnitude < minR) d = (d.sqrMagnitude > 1e-6f ? d.normalized : scrapeFwd) * minR;
        np = new Vector3(pp.x + d.x, 0f, pp.z + d.z);
        np.y = field.HeightAt(np);
        if (!field.IsLava(np, 0.2f)) np = prev;
        scrapeFocus = np;

        Vector3 mv = np - prev; mv.y = 0f;
        float spd = mv.magnitude / dt;
        scrapeSpeed = Mathf.Lerp(scrapeSpeed, spd, 1f - Mathf.Exp(-12f * dt));
        float sf = Mathf.Clamp01(scrapeSpeed / 1.4f);
        if (mv.sqrMagnitude > 1e-8f) scrapeToolDir = Vector3.Slerp(scrapeToolDir, mv.normalized, 1f - Mathf.Exp(-6f * dt));

        // Kamera: noktanin biraz gerisinde ve yukarisinda, asagi bakar; yumusakca takip eder
        scrapeCamFocus = Vector3.Lerp(scrapeCamFocus, scrapeFocus, 1f - Mathf.Exp(-5f * dt));
        Vector3 camPos = scrapeCamFocus - scrapeFwd * 0.9f + Vector3.up * 1.7f;
        var terr = Terrain.activeTerrain;
        if (terr != null) camPos.y = Mathf.Max(camPos.y, terr.SampleHeight(camPos) + terr.transform.position.y + 0.8f);
        Quaternion camRot = Quaternion.LookRotation((scrapeCamFocus - camPos).normalized, Vector3.up);
        player.SetCameraLock(true, camPos, camRot);

        // Alet: ucu yuzeye degiyor, hafifce one egik; sururken titrer
        Vector3 head = (scrapeFwd * 0.6f + scrapeToolDir * 0.4f).normalized;
        float ang = 28f * Mathf.Deg2Rad;
        Vector3 tdir = head * Mathf.Cos(ang) + Vector3.down * Mathf.Sin(ang);
        float bob = Mathf.Sin(Time.time * 38f) * 0.006f * sf;
        scrapeTool.transform.position = scrapeFocus - tdir * (0.2f * 1.5f) + Vector3.up * (0.015f + bob);
        scrapeTool.transform.rotation = Quaternion.LookRotation(tdir, Vector3.up);
        scrapeLight.transform.position = scrapeFocus + Vector3.up * 1.2f;

        // Kazima darbeleri: hiz arttikca guc artar, durunca yavaslar
        scrapeTickT += dt;
        if (scrapeTickT >= ScrapeTick)
        {
            scrapeTickT = 0f;
            ScrapeMine(field, tier, sf);
        }
        // Ince dokulu tikirdama (ASMR)
        scrapeClinkT -= dt;
        if (sf > 0.15f && scrapeClinkT <= 0f)
        {
            scrapeClinkT = Random.Range(0.09f, 0.24f) / (0.4f + sf);
            SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, scrapeFocus, 0.05f + 0.07f * sf, Random.Range(1.8f, 2.6f), 0.1f);
        }
    }

    void ScrapeMine(LavaField field, int tier, float sf)
    {
        Vector3 p = scrapeFocus;
        float factor = ScrapePowerFactor * (0.35f + 1.05f * sf);
        switch (field.Mine(p, Economy.ToolRadius[tier], Economy.ToolPower[tier] * factor))
        {
            case MineResult.None:
                if (Hint == "") SetHint(field.DigAt(p) > 0.97f ? "Ana kayaya ulastin" : "Once lavi suyla sogut", 0.8f);
                break;
            case MineResult.Chipped:
                scrapeFxT -= ScrapeTick;
                if (scrapeFxT <= 0f && sf > 0.1f) { scrapeFxT = Mathf.Lerp(0.22f, 0.08f, sf); Fx.Chips(p + Vector3.up * 0.03f, 2); }
                break;
            case MineResult.Broken:
                OnObsidianBroken(p);
                break;
        }
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
        scrapeAudio.pitch = 0.88f + 0.5f * sf;
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


    void OnObsidianBroken(Vector3 p)
    {
        Fx.Chips(p + Vector3.up * 0.1f, 26);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 1f);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.obsidianBreak : null, p, 1f);
        player.Shake(0.035f, 0.15f);
        SpawnCores(p);
        if (Economy.I != null)
        {
            int earn = CurrentTool == Tool.Breaker ? Economy.BreakerMoney(Economy.BreakerTierNow)
                     : Economy.ToolBreakMoney[Mathf.Clamp(Economy.ToolTier, 0, Economy.ToolNames.Length - 1)];
            Economy.I.Earn(earn);
            SetHint($"+{earn} para", 0.9f);
        }
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
                if (Hint == "") SetHint(field.DigAt(p) > 0.97f ? "Ana kayaya ulastin" : "Once lavi suyla sogut", 0.8f);
                break;
            case MineResult.Chipped:
                drillFxT -= Economy.BreakerTempo(t);
                if (drillFxT <= 0f) { drillFxT = 0.09f; Fx.Chips(p + Vector3.up * 0.04f, 3); }
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
                        SetHint(field.DigAt(p) > 0.97f ? "Ana kayaya ulastin" : "Once lavi suyla sogut");
                        break;
                    case MineResult.Chipped:
                        Fx.Chips(p + Vector3.up * 0.05f);
                        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 1f, 1f - 0.12f * heavy);
                        player.Shake(0.02f + 0.02f * heavy, 0.1f);
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
        int n = Random.Range(1, 3) + (CurrentTool == Tool.Breaker ? Economy.BreakerCores(Economy.BreakerTierNow) - 1 : 0);
        for (int i = 0; i < n; i++)
        {
            var go = Instantiate(coreItemPrefab, p + Vector3.up * 0.3f, Random.rotation);
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

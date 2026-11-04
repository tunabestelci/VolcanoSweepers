using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Tool { Buckets, Pickaxe }

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
    }

    public PlayerController player;
    public Camera cam;
    public Transform leftHandRoot;
    public Transform rightHandRoot;
    public GameObject bucketPrefab;
    public GameObject pickaxePrefab;
    public GameObject coreVisualPrefab;
    public float reach = 6f;
    public float pickupRange = 3.5f;

    public Tool CurrentTool { get; private set; } = Tool.Buckets;
    public int CoreCount { get; private set; }
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

    // Eller govdeye bagli: kovalar yanlarda asagida asili durur, bakinca/kullaninca one gelir
    Hand MakeHand(Transform root, float side)
    {
        var h = new Hand { root = root, rig = root.parent };
        SetDefaultPoses(h, side);
        return h;
    }

    static void SetDefaultPoses(Hand h, float side)
    {
        h.carryPos = new Vector3(side * 0.42f, -0.78f, 0.14f);
        h.readyPos = new Vector3(side * 0.40f, -0.40f, 0.45f);
        h.carryEuler = Vector3.zero;
        h.readyEuler = Vector3.zero;
    }

    void SetHint(string text, float seconds = 2.2f)
    {
        Hint = text;
        hintUntil = Time.time + seconds;
    }

    // Test botu ve dis cagrilar icin
    public bool Busy => left.busy || right.busy;
    public void DoBucket(bool leftHand) { UseBucket(leftHand ? left : right); }
    public void DoSwing() { if (!right.busy) StartCoroutine(SwingPickaxe()); }
    public bool BotPourBusy => left.busy || right.busy;
    public void DoPickup() { TryPickup(); }
    public void DoEquip(Tool t) { Equip(t); }

    // ---------- Alet degistirme ----------

    void Equip(Tool tool)
    {
        if (left.busy || right.busy) return;
        CurrentTool = tool;
        ClearItem(left);
        ClearItem(right);
        if (heldCore != null) { Destroy(heldCore); heldCore = null; }

        if (tool == Tool.Buckets)
        {
            SpawnBucket(left);
            SpawnBucket(right);
            SetDefaultPoses(left, -1f);
            SetDefaultPoses(right, 1f);
        }
        else
        {
            right.item = Instantiate(pickaxePrefab, right.root);
            right.item.transform.localPosition = Vector3.zero;
            // Kazma ucu one baksin (vurus yonunde), sap asagida
            right.item.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
            // Kazma sag elde, onde ve hazir
            right.readyPos = new Vector3(0.36f, -0.42f, 0.55f);
            right.readyEuler = new Vector3(25f, 0f, -10f);
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
        h.item = Instantiate(bucketPrefab, h.root);
        h.item.transform.localPosition = Vector3.zero;
        h.item.transform.localRotation = Quaternion.identity;
        var w = h.item.transform.Find("Water");
        h.water = w;
        UpdateWater(h);
    }

    void RefreshHeldCore()
    {
        if (heldCore != null) { Destroy(heldCore); heldCore = null; }
        if (CoreCount > 0 && CurrentTool == Tool.Pickaxe && coreVisualPrefab != null)
        {
            heldCore = Instantiate(coreVisualPrefab, left.root);
            heldCore.transform.localPosition = Vector3.zero;
            heldCore.transform.localRotation = Quaternion.identity;
        }
    }

    static void UpdateWater(Hand h)
    {
        if (h.water == null) return;
        h.water.gameObject.SetActive(h.fill > 0.02f);
        var s = h.water.localScale;
        // Su yuzeyi bucket prefabinda 'WaterBaseY' / 'WaterFullY' degerleri ile kurulur
        var info = h.water.GetComponent<BucketWater>();
        if (info != null)
        {
            h.water.localPosition = new Vector3(0f, Mathf.Lerp(info.emptyY, info.fullY, h.fill), 0f);
        }
        h.water.localScale = s;
    }

    // ---------- Girdi ----------

    void Update()
    {
        if (Time.time > hintUntil) Hint = "";
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null || mouse == null || Cursor.lockState != CursorLockMode.Locked) { Apply(left); Apply(right); return; }

        if (kb.digit1Key.wasPressedThisFrame) Equip(Tool.Buckets);
        if (kb.digit2Key.wasPressedThisFrame) Equip(Tool.Pickaxe);
        if (kb.eKey.wasPressedThisFrame) TryPickup();

        if (CurrentTool == Tool.Buckets)
        {
            if (mouse.leftButton.wasPressedThisFrame) UseBucket(right);
            if (mouse.rightButton.wasPressedThisFrame) UseBucket(left);
        }
        else
        {
            if (mouse.leftButton.wasPressedThisFrame && !right.busy) StartCoroutine(SwingPickaxe());
        }

        Apply(left);
        Apply(right);
    }

    // Eller: durus pozu + yurume sallantisi + animasyon ofseti
    void Apply(Hand h)
    {
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
            || (CurrentTool == Tool.Pickaxe && (h == right || heldCore != null));
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

    // Nisangahin gosterdigi nokta: once havuz (su), sonra lav yuzeyi
    bool AimPoint(out Vector3 point, out bool isWater)
    {
        isWater = false;
        point = Vector3.zero;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        bool physHit = Raycast(out var hit);
        if (physHit && hit.collider.GetComponentInParent<WaterSource>() != null)
        {
            isWater = true;
            point = hit.point;
            return true;
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
        if (!AimPoint(out var point, out bool isWater)) { SetHint("Hedef cok uzak"); return; }

        if (isWater)
        {
            if (h.fill > 0.95f) { SetHint("Kova zaten dolu"); return; }
            StartCoroutine(FillRoutine(h, point));
            return;
        }
        if (h.fill < 0.05f) { SetHint("Kova bos - once havuzdan doldur"); return; }
        StartCoroutine(PourRoutine(h));
    }

    IEnumerator FillRoutine(Hand h, Vector3 waterPoint)
    {
        h.busy = true;
        Vector3 pos0 = h.animPos, eul0 = h.animEuler;
        Vector3 dipPos = new Vector3(h == left ? 0.1f : -0.1f, -0.22f, 0.32f);
        Vector3 dipEul = new Vector3(40f, 0f, 0f);

        yield return Tween(h, pos0, dipPos, eul0, dipEul, 0.35f);
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.waterFill : null, waterPoint, 0.9f);
        Fx.Splash(waterPoint);
        const float fillTime = 0.9f;
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

    const float DropsPerBucket = 520f;
    public bool botHoldPour;   // test botu icin

    // Kovanin egimine bagli akis: yeterince egilince su damlalari firlar, fizik kurallarina gore duser
    IEnumerator PourRoutine(Hand h)
    {
        h.busy = true;
        Vector3 liftPos = new Vector3(h == left ? 0.12f : -0.12f, 0.2f, 0.28f);
        Vector3 startEul = new Vector3(25f, 0f, 0f);

        yield return Tween(h, Vector3.zero, liftPos, Vector3.zero, startEul, 0.35f);

        float tilt = 25f;
        float maxTilt = 112f;
        float hissTimer = 0f;
        float emitCarry = 0f;
        float elapsed = 0f;
        while (h.fill > 0.01f && elapsed < 7f)
        {
            elapsed += Time.deltaTime;
            // Mouse sol tusu birakilirsa dokmeyi birak
            var mouse = Mouse.current;
            bool held = mouse != null && (h == right ? mouse.leftButton.isPressed : mouse.rightButton.isPressed);
            if (!held && !botHoldPour) break;

            tilt = Mathf.MoveTowards(tilt, maxTilt, 70f * Time.deltaTime);
            h.animPos = liftPos;
            h.animEuler = new Vector3(tilt, 0f, 0f);

            // Egim 60 dereceyi gecince akmaya baslar
            float flow = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f, maxTilt, tilt));
            if (flow > 0.01f)
            {
                if (!AimPoint(out var target, out _)) target = h.root.position + cam.transform.forward * 3f;
                Vector3 mouth = h.item != null ? h.item.transform.position + h.item.transform.up * 0.16f : h.root.position;
                emitCarry += (40f + 260f * flow) * Time.deltaTime;
                int n = Mathf.FloorToInt(emitCarry);
                emitCarry -= n;
                for (int i = 0; i < n; i++)
                {
                    float t = Mathf.Clamp(0.35f + Vector3.Distance(mouth, target) * 0.09f, 0.35f, 0.9f);
                    Vector3 v = (target - mouth) / t - 0.5f * Physics.gravity * t;
                    v += Random.insideUnitSphere * 0.28f;
                    Vector3 spawn = mouth + Random.insideUnitSphere * 0.03f;
                    WaterDrop.Emit(spawn, v);
                    h.fill = Mathf.Max(0f, h.fill - 1f / DropsPerBucket);
                }
                hissTimer -= Time.deltaTime;
                if (hissTimer <= 0f)
                {
                    hissTimer = 0.9f;
                    SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.waterPour : null, target, 0.7f);
                    SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.steamHiss : null, target, 0.5f, 1.1f);
                }
                UpdateWater(h);
            }
            yield return null;
        }
        UpdateWater(h);
        yield return Tween(h, liftPos, Vector3.zero, new Vector3(tilt, 0f, 0f), Vector3.zero, 0.45f);
        h.busy = false;
    }

    // ---------- Kazma ----------

    IEnumerator SwingPickaxe()
    {
        right.busy = true;
        Vector3 windPos = new Vector3(0.05f, 0.18f, -0.15f);
        Vector3 windEul = new Vector3(-55f, 0f, -10f);
        Vector3 hitPos = new Vector3(-0.05f, -0.12f, 0.28f);
        Vector3 hitEul = new Vector3(40f, 0f, 0f);

        yield return Tween(right, Vector3.zero, windPos, Vector3.zero, windEul, 0.22f);
        yield return Tween(right, windPos, hitPos, windEul, hitEul, 0.11f);

        // Vurus aninda
        var field = LavaField.I;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (field != null && field.Raycast(ray, reach, out var p))
        {
            if (!field.InsidePool(p, 0.9f)) { SetHint("Burada kazacak bir sey yok"); }
            else
            {
                switch (field.Mine(p))
                {
                    case MineResult.None:
                        SetHint(field.DigAt(p) > 0.97f ? "Ana kayaya ulastin" : "Once lavi suyla sogut");
                        break;
                    case MineResult.Chipped:
                        Fx.Chips(p + Vector3.up * 0.05f);
                        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 1f);
                        player.Shake(0.02f, 0.1f);
                        break;
                    case MineResult.Broken:
                        Fx.Chips(p + Vector3.up * 0.1f, 26);
                        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickaxeHit : null, p, 1f);
                        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.obsidianBreak : null, p, 1f);
                        player.Shake(0.035f, 0.15f);
                        SpawnCores(p);
                        break;
                }
            }
        }
        yield return Tween(right, hitPos, Vector3.zero, hitEul, Vector3.zero, 0.3f);
        right.busy = false;
    }

    public GameObject coreItemPrefab;

    void SpawnCores(Vector3 p)
    {
        if (coreItemPrefab == null) return;
        int n = Random.Range(1, 3);
        for (int i = 0; i < n; i++)
        {
            var go = Instantiate(coreItemPrefab, p + Vector3.up * 0.3f, Random.rotation);
            var item = go.GetComponent<LithicCoreItem>();
            if (item != null)
                item.Launch(new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(2f, 3.2f), Random.Range(-1.2f, 1.2f)));
        }
    }

    // ---------- Litik cekirdek toplama ----------

    void TryPickup()
    {
        var core = LithicCoreItem.Nearest(transform.position, pickupRange);
        if (core == null) { SetHint("Yakinda cekirdek yok"); return; }
        SfxPlayer.Play(SfxPlayer.I != null ? SfxPlayer.I.pickup : null, core.transform.position, 0.8f);
        Destroy(core.gameObject);
        CoreCount++;
        if (CurrentTool != Tool.Pickaxe) { SetHint("Cekirdek alindi (2: kazma ile elinde gorursun)"); }
        RefreshHeldCore();
        StartCoroutine(ReachLeft());
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

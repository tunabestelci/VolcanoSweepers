using UnityEngine;

// Su kuyusu: E basili tutulunca kova kuyuya iner (donderme makarasi doner); suya ulasinca dolar.
// Doluyken E'ye tekrar basinca kova yukari cekilir. Kova dik ve sabit kalir (devrilmez);
// elimizdeki kovalar dogrudan ondan doldurulur.
public class WaterWell : MonoBehaviour
{
    public static readonly System.Collections.Generic.List<WaterWell> All = new System.Collections.Generic.List<WaterWell>();
    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // Isin kuyuya bakiyor mu (fizik colliderindan bagimsiz)
    public bool LooksAt(Vector3 origin, Vector3 dir, float maxDist)
    {
        Vector3 c = transform.position + Vector3.up * 1.0f;
        float t = Vector3.Dot(c - origin, dir);
        if (t < 0f || t > maxDist) return false;
        return (origin + dir * t - c).magnitude <= 1.25f;
    }

    public Transform wheelPivot;     // donen makara + cark
    public Transform bucket;         // kuyu kovasi
    public BigBucket bucketFill;     // kuyu kovasinin su durumu (WaterSource ile ayni nesne)
    public Transform rope;           // ip (silindir)
    public Vector3 anchor;           // ipin makaraya baglandigi nokta (dunya)
    public float topY;               // kovanin yukaridaki yuksekligi
    public float bottomY;            // kovanin kuyu icindeki en alt yuksekligi
    public float waterY;             // kuyudaki su seviyesi
    public float logRadius = 0.175f;
    public float rimY;

    // Pompa sesi kaydinda iki pompa darbesi ~4.4 sn; kova inis/cikis suresi buna gore ayarli
    public const float TravelTime = 4.4f;
    const float PumpStart = 2.2f, PumpLoopEnd = 6.6f;
    const float FillTime = 1.2f;

    enum S { Top, Lowering, Hanging, Filling, Down, Raising }
    S state = S.Top;
    float y;
    float fillT, startFill;
    public bool auto;                // test botu: basili tutmadan tam dongu
    public string Hint { get; private set; } = "";
    public bool Busy => state != S.Top;

    bool inPressed, inHeld, inLooking;
    Vector3 bucketXZ;
    float bucketHalfH;
    float baseLen;
    AudioSource pump;
    float pumpVol;

    void Start()
    {
        WaterSim.RegisterWell(transform.position, 0.6f, 0.9f, waterY, rimY);
    }

    void Awake()
    {
        pump = gameObject.AddComponent<AudioSource>();
        pump.spatialBlend = 1f; pump.minDistance = 3f; pump.maxDistance = 40f; pump.loop = false; pump.volume = 0f;
        if (bucket == null) return;
        bucketXZ = bucket.position;
        bucketHalfH = 0.17f * bucket.localScale.y;
        baseLen = anchor.y - (topY + bucketHalfH);
        y = topY;
        SetBucketY(topY);
    }

    // Oyuncu tarafindan her karede verilir
    public void SetInput(bool pressed, bool held, bool looking)
    {
        inPressed |= pressed;
        inHeld = held;
        inLooking = looking;
    }

    void Update()
    {
        Step(inPressed, inHeld, inLooking);
        inPressed = false;
        inLooking = false;
        inHeld = false;
        UpdatePump();
    }

    void Step(bool pressed, bool held, bool looking)
    {
        if (bucket == null || bucketFill == null) return;
        Hint = "";
        if (auto) { held = true; looking = true; }
        switch (state)
        {
            case S.Top:
                if (!looking) break;
                if (bucketFill.Fill > 0.98f && !auto) { Hint = "Kova dolu - elindeki kovalari ondan doldur"; break; }
                Hint = "E basili tut: kovayi kuyuya indir";
                if (pressed || auto) Begin(S.Lowering);
                break;
            case S.Lowering:
                if (!held) { Begin(S.Hanging); break; }
                Hint = "Kova iniyor...";
                y -= (topY - bottomY) / TravelTime * Time.deltaTime;
                if (y <= bottomY) { y = bottomY; StartFill(); }
                SetBucketY(y);
                break;
            case S.Hanging:
                if (looking) Hint = "E basili tut: kovayi indirmeye devam et";
                if (pressed && looking) Begin(S.Lowering);
                break;
            case S.Filling:
                Hint = "Kova doluyor...";
                fillT += Time.deltaTime;
                float k = Mathf.Clamp01(fillT / FillTime);
                bucketFill.SetFill(Mathf.Lerp(startFill, 1f, k));
                SetBucketY(bottomY + Mathf.Sin(fillT * 7f) * 0.03f * (1f - k));
                if (fillT >= FillTime)
                {
                    bucketFill.SetFill(1f);
                    SetBucketY(bottomY);
                    state = S.Down;
                }
                break;
            case S.Down:
                if (looking) Hint = "E: kovayi yukari cek";
                if ((pressed && looking) || auto) Begin(S.Raising);
                break;
            case S.Raising:
                Hint = "Kova cekiliyor...";
                y += (topY - bottomY) / TravelTime * Time.deltaTime;
                if (y >= topY)
                {
                    y = topY;
                    SetBucketY(y);
                    bucketFill.locked = false;
                    state = S.Top;
                    auto = false;
                    StopPump();
                    break;
                }
                SetBucketY(y);
                break;
        }
    }

    void Begin(S s)
    {
        state = s;
        if (s == S.Lowering || s == S.Raising)
        {
            bucketFill.locked = true;
            bucket.rotation = Quaternion.identity;
            StartPump();
        }
        else if (s == S.Hanging) StopPump();
    }

    void StartFill()
    {
        state = S.Filling;
        fillT = 0f;
        startFill = bucketFill.Fill;
        StopPump();
        Vector3 wp = new Vector3(bucketXZ.x, waterY, bucketXZ.z);
        Fx.Splash(wp);
        if (SfxPlayer.I != null) SfxPlayer.PlaySnippet(SfxPlayer.I.waterPour, wp, 1.6f, 0.9f);
    }

    // ---- pompa sesi ----
    void StartPump()
    {
        var clip = SfxPlayer.I != null ? SfxPlayer.I.wellPump : null;
        if (clip == null) return;
        if (!pump.isPlaying || pump.clip != clip)
        {
            pump.clip = clip;
            pump.time = PumpStart;
            pump.Play();
        }
        pumpVol = 1f;
    }

    void StopPump() { pumpVol = 0f; }

    void UpdatePump()
    {
        if (pump == null || pump.clip == null) return;
        pump.volume = Mathf.MoveTowards(pump.volume, pumpVol * 0.9f, Time.deltaTime * 3f);
        if (!pump.isPlaying) return;
        if (pump.time > PumpLoopEnd) pump.time = PumpStart;
        if (pumpVol <= 0f && pump.volume <= 0.01f) pump.Stop();
    }

    void SetBucketY(float yy)
    {
        bucket.position = new Vector3(bucketXZ.x, yy, bucketXZ.z);
        float len = anchor.y - (yy + bucketHalfH);
        if (wheelPivot != null)
            wheelPivot.localRotation = Quaternion.Euler((len - baseLen) / logRadius * Mathf.Rad2Deg, 0f, 0f);
        UpdateRope();
    }

    void UpdateRope()
    {
        if (rope == null) return;
        Vector3 top = anchor;
        Vector3 bottom = bucket.position + Vector3.up * bucketHalfH;
        Vector3 d = bottom - top;
        float len = Mathf.Max(0.02f, d.magnitude);
        rope.position = (top + bottom) * 0.5f;
        rope.rotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
        rope.localScale = new Vector3(rope.localScale.x, len * 0.5f, rope.localScale.z);
    }
}

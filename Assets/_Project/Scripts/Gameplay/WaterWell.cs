using System.Collections;
using UnityEngine;

// Su kuyusu: E ile kova kuyuya iner (donderme makarasi doner), dolar ve yukari cikar.
// Kova dik ve sabit kalir (devrilmez); elimizdeki kovalar dogrudan ondan doldurulur.
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

    bool busy;
    Vector3 bucketXZ;
    float bucketHalfH;
    float baseLen;

    void Start()
    {
        WaterSim.RegisterWell(transform.position, 0.6f, 0.9f, waterY, rimY);
    }

    void Awake()
    {
        if (bucket == null) return;
        bucketXZ = bucket.position;
        bucketHalfH = 0.17f * bucket.localScale.y;
        baseLen = anchor.y - (topY + bucketHalfH);
        SetBucketY(topY);
    }

    public string Pull()
    {
        if (bucket == null || bucketFill == null) return "";
        if (busy) return "Kova kuyuda...";
        if (bucketFill.Fill > 0.98f) return "Kova zaten dolu - elindeki kovalari ondan doldur";
        StartCoroutine(Run());
        return "Kova kuyuya iniyor...";
    }

    void SetBucketY(float y)
    {
        bucket.position = new Vector3(bucketXZ.x, y, bucketXZ.z);
        float len = anchor.y - (y + bucketHalfH);
        if (wheelPivot != null)
            wheelPivot.localRotation = Quaternion.Euler((len - baseLen) / logRadius * Mathf.Rad2Deg, 0f, 0f);
        UpdateRope(y);
    }

    void UpdateRope(float y)
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

    static float Smooth(float k) { return k * k * (3f - 2f * k); }

    IEnumerator Move(float from, float to, float dur)
    {
        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            SetBucketY(Mathf.Lerp(from, to, Smooth(t / dur)));
            yield return null;
        }
        SetBucketY(to);
    }

    IEnumerator Run()
    {
        busy = true;
        bucketFill.locked = true;                 // kova hareket ederken su alinamaz
        bucket.rotation = Quaternion.identity;
        float startFill = bucketFill.Fill;

        // 1) Kuyuya in
        yield return Move(topY, bottomY, 2.4f);

        // 2) Suya degdi: sicrama + dolma
        Vector3 wp = new Vector3(bucketXZ.x, waterY, bucketXZ.z);
        Fx.Splash(wp);
        if (SfxPlayer.I != null) SfxPlayer.Play(SfxPlayer.I.waterFill, wp, 1f, 0.9f);
        const float fillTime = 0.9f;
        for (float t = 0f; t < fillTime; t += Time.deltaTime)
        {
            bucketFill.SetFill(Mathf.Lerp(startFill, 1f, t / fillTime));
            SetBucketY(bottomY + Mathf.Sin(t * 9f) * 0.03f);
            yield return null;
        }
        bucketFill.SetFill(1f);

        // 3) Cek: kova dik ve sabit yukarida kalir
        yield return Move(bottomY, topY, 2.6f);

        bucketFill.locked = false;
        busy = false;
    }
}

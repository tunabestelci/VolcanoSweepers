using System.Collections;
using UnityEngine;

// Su kuyusu: E ile kova kuyuya iner (donderme makarasi doner), dolar, cikar, devrilip buyuk kovaya bosalir.
public class WaterWell : MonoBehaviour
{
    public Transform wheelPivot;     // donen makara + cark
    public Transform bucket;         // kuyu kovasi
    public BigBucket bucketFill;     // kuyu kovasinin su gorseli
    public Transform rope;           // ip (silindir)
    public BigBucket big;            // buyuk kova
    public Transform pourTarget;     // buyuk kovanin agzi
    public Vector3 anchor;           // ipin makaraya baglandigi nokta (dunya)
    public Vector3 tipDir;           // kovanin devrildigi yon (buyuk kovaya dogru)
    public float topY;               // kovanin yukaridaki yuksekligi
    public float bottomY;            // kovanin kuyu icindeki en alt yuksekligi
    public float waterY;             // kuyudaki su seviyesi
    public float logRadius = 0.175f;
    public float haulAmount = 0.5f;  // bir cekis buyuk kovanin ne kadarini doldurur

    public float rimY;
    public const int ParticlesPerHaul = 900;
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
        if (bucket == null || big == null) return "";
        if (busy) return "Kova kuyuda...";
        if (big.Fill > 0.98f) return "Buyuk kova zaten dolu";
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
        bucket.rotation = Quaternion.identity;
        bucketFill.SetFill(0f);

        // 1) Kuyuya in
        yield return Move(topY, bottomY, 2.4f);

        // 2) Suya degdi: sicrama + dolma
        Vector3 wp = new Vector3(bucketXZ.x, waterY, bucketXZ.z);
        Fx.Splash(wp);
        if (SfxPlayer.I != null) SfxPlayer.Play(SfxPlayer.I.waterFill, wp, 1f, 0.9f);
        const float fillTime = 0.9f;
        for (float t = 0f; t < fillTime; t += Time.deltaTime)
        {
            bucketFill.SetFill(t / fillTime);
            SetBucketY(bottomY + Mathf.Sin(t * 9f) * 0.03f);
            yield return null;
        }
        bucketFill.SetFill(1f);

        // 3) Cek
        yield return Move(bottomY, topY, 2.6f);

        // 4) Devir ve buyuk kovaya bosalt
        yield return Tip();
        busy = false;
    }

    IEnumerator Tip()
    {
        Vector3 axis = Vector3.Cross(Vector3.up, tipDir).normalized;
        const float dur = 1.7f;
        const float maxTilt = 108f;
        float haul = bucketFill.Fill * haulAmount;
        float bigStart = big.Fill;
        float poured = 0f;
        float emitCarry = 0f;
        float emittedFrac = 0f;
        float startFill = bucketFill.Fill;
        float mouthDist = 0.2f * bucket.localScale.y;
        Vector3 center = pourTarget.position;

        for (float t = 0f; t < dur; t += Time.deltaTime)
        {
            float e = Smooth(Mathf.Clamp01(t / (dur * 0.55f)));
            float tilt = maxTilt * e;
            bucket.rotation = Quaternion.AngleAxis(tilt, axis);
            // Kova ipin ucunda buyuk kovaya dogru disari sallanir ve hafif kalkar
            bucket.position = new Vector3(bucketXZ.x, topY, bucketXZ.z) + tipDir * (0.42f * e) + Vector3.up * (0.1f * e);
            UpdateRope(topY);

            float flow = Mathf.InverseLerp(50f, maxTilt, tilt);
            if (flow > 0f && bucketFill.Fill > 0.005f)
            {
                float drain = Time.deltaTime / (dur * 0.5f) * flow;
                float f = Mathf.Max(0f, bucketFill.Fill - drain);
                poured += (bucketFill.Fill - f);
                bucketFill.SetFill(f);

                Vector3 mouth = bucket.position + bucket.up * mouthDist;
                emitCarry += (poured - emittedFrac) / Mathf.Max(startFill, 0.01f) * ParticlesPerHaul;
                emittedFrac = poured;
                int n = Mathf.FloorToInt(emitCarry);
                emitCarry -= n;
                for (int i = 0; i < n; i++)
                {
                    Vector2 o = Random.insideUnitCircle * 0.05f;
                    Vector3 land = center + new Vector3(o.x, 0f, o.y);
                    float tt = 0.32f + Random.Range(0f, 0.05f);
                    Vector3 v = (land - mouth) / tt - 0.5f * Physics.gravity * tt;
                    Vector3 p0 = mouth + bucket.right * Random.Range(-0.06f, 0.06f) + Random.insideUnitSphere * 0.04f;
                    WaterSim.Emit(p0 + v * (Time.deltaTime * Random.value), v);
                }
            }
            yield return null;
        }
        // Kalan suyu da ayni hedefe aktar (hicbir sey disari dokulmesin)
        {
            int rest = Mathf.RoundToInt(emitCarry + (1f - poured / Mathf.Max(startFill, 0.01f)) * ParticlesPerHaul);
            Vector3 mouth = bucket.position + bucket.up * mouthDist;
            for (int i = 0; i < rest; i++)
            {
                Vector2 o = Random.insideUnitCircle * 0.05f;
                Vector3 land = center + new Vector3(o.x, 0f, o.y);
                float tt = 0.32f + Random.Range(0f, 0.05f);
                Vector3 v = (land - mouth) / tt - 0.5f * Physics.gravity * tt;
                WaterSim.Emit(mouth + bucket.right * Random.Range(-0.06f, 0.06f), v);
            }
        }
        bucketFill.SetFill(0f);
        if (SfxPlayer.I != null) SfxPlayer.Play(SfxPlayer.I.waterPour, center, 0.8f);

        // Duzel ve geri sallan
        Quaternion from = bucket.rotation;
        Vector3 fromPos = bucket.position;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            float k = Smooth(t / 0.6f);
            bucket.rotation = Quaternion.Slerp(from, Quaternion.identity, k);
            bucket.position = Vector3.Lerp(fromPos, new Vector3(bucketXZ.x, topY, bucketXZ.z), k);
            UpdateRope(topY);
            yield return null;
        }
        bucket.rotation = Quaternion.identity;
        SetBucketY(topY);
    }
}

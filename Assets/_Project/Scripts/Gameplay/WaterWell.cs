using System.Collections;
using UnityEngine;

// Su kuyusu: E ile buyuk kovayi doldurur (kuyudan su cekme).
public class WaterWell : MonoBehaviour
{
    public BigBucket bucket;
    public float pullTime = 2.4f;

    bool busy;

    public string Pull()
    {
        if (bucket == null) return "";
        if (busy) return "Kuyudan su cekiliyor...";
        if (bucket.Fill > 0.98f) return "Buyuk kova zaten dolu";
        StartCoroutine(PullRoutine());
        return "Kuyudan su cekiliyor...";
    }

    IEnumerator PullRoutine()
    {
        busy = true;
        Vector3 top = bucket.transform.position + Vector3.up * 0.6f;
        if (SfxPlayer.I != null) SfxPlayer.Play(SfxPlayer.I.waterFill, transform.position + Vector3.up, 1f, 0.8f);
        float f0 = bucket.Fill;
        for (float t = 0f; t < pullTime; t += Time.deltaTime)
        {
            bucket.SetFill(Mathf.Lerp(f0, 1f, t / pullTime));
            if (Random.value < 0.12f) Fx.Splash(top + Random.insideUnitSphere * 0.15f);
            yield return null;
        }
        bucket.SetFill(1f);
        busy = false;
    }
}

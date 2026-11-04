using UnityEngine;

// Kuyunun yanindaki buyuk kova. Kuyudan cekilince dolar, oyuncunun kovalari bundan doldurulur.
// WaterSource ile ayni nesnede durur (kova doldurma hedefi).
public class BigBucket : MonoBehaviour
{
    public int capacity = 6;   // kac kucuk kova doldurabilir

    Transform water;
    BucketWater info;
    float fill;

    public float Fill => fill;

    void Awake()
    {
        water = transform.Find("Water");
        if (water != null) info = water.GetComponent<BucketWater>();
        Apply();
    }

    public void SetFill(float f)
    {
        fill = Mathf.Clamp01(f);
        Apply();
    }

    // Bir kucuk kova kadar su verir
    public bool TryTake()
    {
        float step = 1f / capacity;
        if (fill < step - 0.001f) return false;
        SetFill(fill - step);
        return true;
    }

    void Apply()
    {
        if (water == null) return;
        water.gameObject.SetActive(fill > 0.02f);
        if (info != null)
            water.localPosition = new Vector3(0f, Mathf.Lerp(info.emptyY, info.fullY, fill), 0f);
    }
}

using UnityEngine;

// Kova (buyuk kova / kuyu kovasi). Su parcaciklari icine dusunce dolar.
// WaterSource ile ayni nesnede durur (kova doldurma hedefi).
public class BigBucket : MonoBehaviour
{
    public int capacity = 6;                  // kac kucuk kova doldurabilir
    public bool collectsParticles;            // fizik suyunu toplar (buyuk kova)
    public float fillPerParticle = 0.5f / 900f;

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

    void Start()
    {
        if (!collectsParticles) return;
        float s = transform.lossyScale.y;
        Vector3 c = transform.position;
        float half = 0.17f * s;
        WaterSim.RegisterContainer(this, c, half * 0.86f, half * 1.03f, c.y - half, c.y + half - 0.03f);
    }

    public void AddParticle()
    {
        SetFill(fill + fillPerParticle);
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

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
        float R = info != null ? info.rimRadius * s : 0.15f * s;
        float yFloor = info != null ? transform.TransformPoint(new Vector3(0f, info.emptyY, 0f)).y : transform.position.y - 0.15f * s;
        float yRim = info != null ? transform.TransformPoint(new Vector3(0f, info.fullY, 0f)).y : transform.position.y + 0.15f * s;
        WaterSim.RegisterContainer(this, transform.position, R * 0.95f, R * 1.12f, yFloor, yRim - 0.03f, 0.12f);
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
        if (info != null) info.SetFill(fill);
    }
}

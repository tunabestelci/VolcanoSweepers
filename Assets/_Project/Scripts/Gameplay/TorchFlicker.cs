using UnityEngine;

// Mesale: isik titrer, alev mesh'i dalgalanir.
public class TorchFlicker : MonoBehaviour
{
    public Light torchLight;
    public Transform flame;
    public float baseIntensity = 2.2f;
    float seed;
    Vector3 flameBase;

    void Start()
    {
        seed = Random.value * 100f;
        if (flame != null) flameBase = flame.localScale;
    }

    void Update()
    {
        float n = Mathf.PerlinNoise(Time.time * 5.5f, seed) * 0.6f + Mathf.PerlinNoise(Time.time * 13f, seed + 9f) * 0.4f;
        if (torchLight != null)
        {
            torchLight.intensity = baseIntensity * (0.78f + n * 0.5f);
            torchLight.range = 11f + n * 1.5f;
        }
        if (flame != null)
        {
            float k = 0.92f + n * 0.2f;
            flame.localScale = new Vector3(flameBase.x * (0.96f + n * 0.08f), flameBase.y * k, flameBase.z * (0.96f + n * 0.08f));
        }
    }
}

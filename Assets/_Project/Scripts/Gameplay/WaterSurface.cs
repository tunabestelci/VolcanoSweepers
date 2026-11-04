using UnityEngine;

// Su yuzeyinin dokusunu ve normal haritasini yavasca kaydirir (dalgalanma hissi).
[RequireComponent(typeof(Renderer))]
public class WaterSurface : MonoBehaviour
{
    public Vector2 speed = new Vector2(0.018f, 0.011f);
    Material mat;
    Vector2 offset;

    void Awake()
    {
        mat = GetComponent<Renderer>().material;
    }

    void Update()
    {
        offset += speed * Time.deltaTime;
        mat.SetTextureOffset("_BaseMap", offset);
        mat.SetTextureOffset("_BumpMap", new Vector2(-offset.y * 1.7f, offset.x * 1.3f));
    }
}

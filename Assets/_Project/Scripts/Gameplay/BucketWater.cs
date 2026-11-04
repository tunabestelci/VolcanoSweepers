using UnityEngine;

// Kovanin icindeki su. Bu bilesen "Water" nesnesinin uzerinde durur ve yalniz seviyeyi tasir
// (localPosition.y = suyun kabin icindeki yuksekligi). Gorunen su, kabin icindeki "WaterBody"
// silindiridir: LiquidVessel shader'i onu dunyada yatay bir duzlemden keser, boylece kova egilse
// de su yuzeyi dunyaya gore duz kalir, kapak gibi donen bir disk yoktur.
public class BucketWater : MonoBehaviour
{
    public float emptyY;   // kova ici taban
    public float fullY;    // agiz seviyesi (dolu)

    Transform bucket;
    Transform body;
    Renderer bodyRenderer;
    MaterialPropertyBlock block;
    float bodyDia;
    static readonly int FillY = Shader.PropertyToID("_FillY");

    void Awake()
    {
        bucket = transform.parent;
        var disc = GetComponent<Renderer>();
        if (disc != null) disc.enabled = false;     // eski duz disk gorunmesin
        Bind();
    }

    void Bind()
    {
        if (body != null || bucket == null) return;
        body = bucket.Find("WaterBody");
        if (body == null) return;
        bodyRenderer = body.GetComponent<Renderer>();
        bodyDia = body.localScale.x;
        block = new MaterialPropertyBlock();
    }

    void OnEnable()
    {
        Bind();
        if (body != null) body.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (body != null) body.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (body == null) return;
        float h = Mathf.Max(0.01f, fullY - emptyY);
        body.localPosition = new Vector3(0f, emptyY + h * 0.5f, 0f);
        body.localRotation = Quaternion.identity;
        body.localScale = new Vector3(bodyDia, h * 0.5f, bodyDia);

        // Su yuzeyi: kabin ekseni uzerindeki seviye noktasindan gecen yatay duzlem
        float levelY = bucket.TransformPoint(new Vector3(0f, transform.localPosition.y, 0f)).y;
        bodyRenderer.GetPropertyBlock(block);
        block.SetFloat(FillY, levelY);
        bodyRenderer.SetPropertyBlock(block);
    }
}

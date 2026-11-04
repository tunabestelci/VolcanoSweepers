using UnityEngine;

// Kovanin icindeki su. Bu bilesen "Water" nesnesinin uzerinde durur ve yalniz seviyeyi tasir
// (localPosition.y = suyun kabin icindeki yuksekligi). Gorunen su, kabin ic seklinden uretilmis
// "WaterBody" mesh'idir: LiquidVessel shader'i onu dunyada yatay bir duzlemden keser, boylece kova
// egilse de su yuzeyi dunyaya gore duz kalir.
public class BucketWater : MonoBehaviour
{
    public float emptyY;          // ic taban
    public float fullY;           // agiz seviyesi (dolu)
    public float rimRadius;       // agizdaki ic yaricap
    public float totalVolume;     // ic hacim (kova yerel birimi^3)
    public float[] levelTable;    // doluluk (0..1, 65 ornek) -> seviye yuksekligi (hacme gore)

    Transform bucket;
    Transform body;
    Renderer bodyRenderer;
    MaterialPropertyBlock block;
    float fill;
    static readonly int FillY = Shader.PropertyToID("_FillY");

    public float Fill => fill;

    // Doluluk oranina karsilik gelen su seviyesi (kabin ic seklini hesaba katar)
    public float LevelY(float f)
    {
        if (levelTable == null || levelTable.Length < 2) return Mathf.Lerp(emptyY, fullY, f);
        float t = Mathf.Clamp01(f) * (levelTable.Length - 1);
        int i = Mathf.Min((int)t, levelTable.Length - 2);
        return Mathf.Lerp(levelTable[i], levelTable[i + 1], t - i);
    }

    public void SetFill(float f)
    {
        fill = Mathf.Clamp01(f);
        transform.localPosition = new Vector3(0f, LevelY(fill), 0f);
    }

    void Awake()
    {
        bucket = transform.parent;
        var disc = GetComponent<Renderer>();
        if (disc != null) disc.enabled = false;
        Bind();
    }

    void Bind()
    {
        if (body != null) return;
        if (bucket == null) bucket = transform.parent;
        if (bucket == null) return;
        body = bucket.Find("WaterBody");
        if (body == null) return;
        bodyRenderer = body.GetComponent<Renderer>();
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

    // Su yuzeyi: kabin ekseni uzerindeki seviye noktasindan gecen yatay duzlem
    public void Refresh()
    {
        Bind();
        if (body == null) return;
        float levelY = bucket.TransformPoint(new Vector3(0f, transform.localPosition.y, 0f)).y;
        bodyRenderer.GetPropertyBlock(block);
        block.SetFloat(FillY, levelY);
        bodyRenderer.SetPropertyBlock(block);
    }

    void LateUpdate()
    {
        Refresh();
    }
}

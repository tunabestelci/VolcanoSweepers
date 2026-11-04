using UnityEngine;

// Cevher deposu: elindeki kovalardan cevher bosaltilir, 500 cevhere kadar dolar.
public class OreBin : MonoBehaviour
{
    public static OreBin I;
    public int capacity = 500;
    public Collider hitCollider;
    public Transform pile;                // icindeki cevher yigini (dolulukla yukselir)
    public Vector3 pileFullSize = new Vector3(3.2f, 0.7f, 0.9f);
    public Vector3 pileBaseLocal;         // yigin tabani (yerel)

    public int Count { get; private set; }

    void Awake() { I = this; Apply(); }

    public bool LooksAt(Ray ray, float maxDist)
    {
        return hitCollider != null && hitCollider.Raycast(ray, out _, maxDist);
    }

    // Kabul edilen cevher sayisini dondurur
    public int Deposit(int n)
    {
        int take = Mathf.Clamp(n, 0, capacity - Count);
        Count += take;
        Apply();
        return take;
    }

    void Apply()
    {
        if (pile == null) return;
        float f = Mathf.Clamp01(Count / (float)capacity);
        pile.gameObject.SetActive(Count > 0);
        float h = Mathf.Max(0.04f, pileFullSize.y * f);
        pile.localScale = new Vector3(pileFullSize.x, h, pileFullSize.z);
        pile.localPosition = pileBaseLocal + new Vector3(0f, h * 0.5f, 0f);
    }
}

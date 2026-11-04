using UnityEngine;

// Kovanin icindeki su yuzeyi (bu bilesen su nesnesinin uzerinde durur).
// Kova egilse de su yuzeyi dunyaya gore duz kalir; kova cok egildikce yuzey kuculur.
public class BucketWater : MonoBehaviour
{
    public float emptyY;
    public float fullY;

    Transform bucket;
    Vector3 baseScale;

    void Awake()
    {
        bucket = transform.parent;
        baseScale = transform.localScale;
    }

    void LateUpdate()
    {
        if (bucket == null) return;
        Quaternion level = Quaternion.FromToRotation(bucket.up, Vector3.up);
        transform.rotation = level * bucket.rotation;

        float tilt = Vector3.Angle(bucket.up, Vector3.up);
        float k = Mathf.Clamp01(1f - tilt / 115f);
        float s = 0.35f + 0.65f * k;
        transform.localScale = new Vector3(baseScale.x * s, baseScale.y, baseScale.z * s);
    }
}

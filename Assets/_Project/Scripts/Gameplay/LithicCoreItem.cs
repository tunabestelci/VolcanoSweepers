using System.Collections.Generic;
using UnityEngine;

public class LithicCoreItem : MonoBehaviour
{
    public static readonly List<LithicCoreItem> All = new List<LithicCoreItem>();

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    public static LithicCoreItem Nearest(Vector3 pos, float maxDist)
    {
        LithicCoreItem best = null;
        float bestD = maxDist;
        foreach (var c in All)
        {
            float d = Vector3.Distance(c.transform.position, pos);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }
}

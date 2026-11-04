using System.Collections.Generic;
using UnityEngine;

// Kovalarin doldurulabildigi su kaynagi (kuyu kovasi). Nisan kontrolu fizige bagli degil,
// isin-kure testiyle yapilir; boylece collider/senkron sorunlari secimi bozmaz.
public class WaterSource : MonoBehaviour
{
    public static readonly List<WaterSource> All = new List<WaterSource>();

    public float PickRadius => 0.19f * transform.lossyScale.y * 1.2f;

    void OnEnable() { All.Add(this); }
    void OnDisable() { All.Remove(this); }

    // Isin bu kaynagi kesiyorsa mesafeyi dondurur
    public bool Hit(Vector3 origin, Vector3 dir, float maxDist, out float t)
    {
        Vector3 c = transform.position;
        t = Vector3.Dot(c - origin, dir);
        if (t < 0f || t > maxDist) return false;
        return (origin + dir * t - c).magnitude <= PickRadius;
    }
}

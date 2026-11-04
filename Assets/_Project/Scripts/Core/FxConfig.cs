using UnityEngine;

public class FxConfig : MonoBehaviour
{
    public Material particleMaterial;
    public GameObject waterDropPrefab;

    void Awake()
    {
        Fx.SoftMat = particleMaterial;
        WaterDrop.Setup(waterDropPrefab);
    }
}

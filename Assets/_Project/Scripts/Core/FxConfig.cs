using UnityEngine;

public class FxConfig : MonoBehaviour
{
    public Material particleMaterial;

    void Awake()
    {
        Fx.SoftMat = particleMaterial;
    }
}

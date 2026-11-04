using UnityEngine;

// Kodla uretilen parcacik efektleri. Material FxConfig tarafindan atanir.
public static class Fx
{
    public static Material SoftMat;

    static ParticleSystem Make(string name, Vector3 pos, Quaternion rot, out ParticleSystem.MainModule main)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        go.transform.SetPositionAndRotation(pos, rot);
        var ps = go.AddComponent<ParticleSystem>();
        main = ps.main;
        main.loop = false;
        main.playOnAwake = true;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = SoftMat;
        return ps;
    }

    static void Burst(ParticleSystem ps, int count)
    {
        var e = ps.emission;
        e.rateOverTime = 0f;
        e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
    }

    static void Fade(ParticleSystem ps, Color from, Color to, float endScale)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(from.a, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(g);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, endScale));
    }

    public static void Steam(Vector3 pos, float scale = 1f)
    {
        var ps = Make("FX_Steam", pos, Quaternion.Euler(-90f, 0f, 0f), out var main);
        main.duration = 1.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.5f * scale, 1.1f * scale);
        main.startColor = new Color(1f, 1f, 1f, 0.55f);
        main.gravityModifier = -0.15f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 22f;
        shape.radius = 0.55f * scale;
        Burst(ps, 38);
        Fade(ps, new Color(1f, 1f, 1f, 0.55f), new Color(0.8f, 0.8f, 0.85f, 0f), 2.4f);
        ps.gameObject.SetActive(true);
    }

    public static void Pop(Vector3 pos, float scale = 1f)
    {
        var ps = Make("FX_Pop", pos, Quaternion.identity, out var main);
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f * scale, 0.14f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.2f, 1f), new Color(1f, 0.35f, 0.05f, 1f));
        main.gravityModifier = 1.2f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.08f * scale;
        Burst(ps, 14);
        Fade(ps, new Color(1f, 0.7f, 0.15f, 1f), new Color(1f, 0.2f, 0f, 0f), 0.3f);
        ps.gameObject.SetActive(true);
    }

    public static void Chips(Vector3 pos, int count = 10)
    {
        var ps = Make("FX_Chips", pos, Quaternion.identity, out var main);
        main.duration = 0.6f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.1f, 0.1f, 0.14f, 1f), new Color(0.35f, 0.3f, 0.4f, 1f));
        main.gravityModifier = 2.5f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.1f;
        Burst(ps, count);
        Fade(ps, new Color(0.2f, 0.2f, 0.25f, 1f), new Color(0.1f, 0.1f, 0.12f, 0f), 0.5f);
        ps.gameObject.SetActive(true);
    }

    // from -> to arasinda akan su
    public static void WaterStream(Vector3 from, Vector3 to, float duration)
    {
        Vector3 dir = to - from;
        var ps = Make("FX_WaterStream", from, Quaternion.LookRotation(dir.normalized), out var main);
        main.duration = duration;
        float dist = Mathf.Max(0.5f, dir.magnitude);
        main.startLifetime = 0.55f;
        main.startSpeed = dist / 0.55f * 0.9f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        main.startColor = new Color(0.45f, 0.75f, 1f, 0.9f);
        main.gravityModifier = 1.2f;
        var e = ps.emission;
        e.rateOverTime = 160f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 3f;
        shape.radius = 0.05f;
        Fade(ps, new Color(0.5f, 0.8f, 1f, 0.9f), new Color(0.7f, 0.9f, 1f, 0f), 1.3f);
        ps.gameObject.SetActive(true);
    }

    public static void Splash(Vector3 pos)
    {
        var ps = Make("FX_Splash", pos, Quaternion.Euler(-90f, 0f, 0f), out var main);
        main.duration = 0.5f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.15f);
        main.startColor = new Color(0.55f, 0.8f, 1f, 0.9f);
        main.gravityModifier = 1.8f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 35f;
        shape.radius = 0.1f;
        Burst(ps, 18);
        Fade(ps, new Color(0.6f, 0.85f, 1f, 0.9f), new Color(0.8f, 0.95f, 1f, 0f), 0.6f);
        ps.gameObject.SetActive(true);
    }
}

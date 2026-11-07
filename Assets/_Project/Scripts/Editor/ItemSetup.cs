using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Baslangic kaplari (bardak, surahi), el aletleri (kasik, spatula) ve mesale prefablarini kurar.
public static class ItemSetup
{
    const string Art = "Assets/_Project/Art";
    const string Mdl = Art + "/Models/Items/";
    const string Tx = Art + "/Textures/Items/";
    const string Prefabs = "Assets/_Project/Prefabs";

    static Texture2D Tex(string p) => AssetDatabase.LoadAssetAtPath<Texture2D>(p);

    static void FixModel(string path)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) return;
        bool dirty = false;
        if (mi.materialImportMode != ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (mi.importNormals != ModelImporterNormals.Import) { mi.importNormals = ModelImporterNormals.Import; dirty = true; }
        if (mi.importTangents != ModelImporterTangents.CalculateMikk) { mi.importTangents = ModelImporterTangents.CalculateMikk; dirty = true; }
        if (mi.meshCompression != ModelImporterMeshCompression.Off) { mi.meshCompression = ModelImporterMeshCompression.Off; dirty = true; }
        if (dirty) mi.SaveAndReimport();
    }

    static void NormalMap(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti != null && ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
    }

    static Material Lit(string path, Color color, Texture2D baseMap, float smooth, float metal = 0f, Color? emission = null)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", color);
        mat.SetTexture("_BaseMap", baseMap);
        mat.SetFloat("_Smoothness", smooth);
        mat.SetFloat("_Metallic", metal);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material Glass(string path)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        mat.SetColor("_BaseColor", new Color(0.9f, 0.88f, 0.84f, 0.13f));       // ust uste binen yuzler (dis/ic duvar) yuzunden dusuk alfa
        mat.SetFloat("_Smoothness", 0.92f);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", 5f);
        mat.SetFloat("_DstBlend", 10f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetFloat("_SrcBlend", 5f);
        if (mat.HasProperty("_SrcBlendAlpha")) { mat.SetFloat("_SrcBlendAlpha", 1f); mat.SetFloat("_DstBlendAlpha", 10f); }
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Beyaz seramik (opak) bardak malzemesi
    static Material Ceramic(string path)
    {
        var mat = Lit(path, new Color(0.96f, 0.95f, 0.92f), null, 0.5f);
        mat.SetFloat("_Surface", 0f);
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Opaque");
        mat.SetFloat("_SrcBlend", 1f); mat.SetFloat("_DstBlend", 0f); mat.SetFloat("_ZWrite", 1f);
        mat.renderQueue = 2000;
        mat.SetFloat("_Cull", 0f);          // ince cidar: ic yuzu de gorunsun
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // Yumusak yuvarlak alev lekesi (parcacik dokusu)
    static Texture2D FireBlob()
    {
        string path = Tx + "FireBlob.png";
        if (!File.Exists(path))
        {
            const int N = 128;
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, w = (y + 0.5f) / N * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + w * w);
                    float a = Mathf.Clamp01((1f - r) / 0.75f);
                    a = a * a * (3f - 2f * a);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
        return Tex(path);
    }

    static Material ParticleMaterial(string name, bool additive)
    {
        string path = Art + "/Materials/" + name;
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, path); }
        mat.shader = sh;
        mat.SetTexture("_BaseMap", FireBlob());
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", 5f);
        mat.SetFloat("_DstBlend", additive ? 1f : 10f);
        if (mat.HasProperty("_SrcBlendAlpha")) { mat.SetFloat("_SrcBlendAlpha", additive ? 0f : 1f); mat.SetFloat("_DstBlendAlpha", additive ? 1f : 10f); }
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    class ProfileData { public List<Vector2> pts = new List<Vector2>(); public float height; }

    static ProfileData ReadProfile(string path)
    {
        string t = File.ReadAllText(path);
        var pd = new ProfileData();
        var mh = Regex.Match(t, "\"height\"\\s*:\\s*(-?[0-9.eE+-]+)");
        pd.height = float.Parse(mh.Groups[1].Value, CultureInfo.InvariantCulture);
        foreach (Match m in Regex.Matches(t, "\\[\\s*(-?[0-9.eE+-]+)\\s*,\\s*(-?[0-9.eE+-]+)\\s*\\]"))
            pd.pts.Add(new Vector2(float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)));
        return pd;
    }

    // Cam kap + icindeki su (kovayla ayni sistem: ic profilden uretilen su hacmi)
    public static GameObject BuildVessel(string name, string fbx, string profileJson, float scale, Material glass, Material liquid, float handleYaw = 0f)
    {
        FixModel(fbx);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var pd = ReadProfile(profileJson);
        float H = pd.height;
        var root = new GameObject(name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        // FBX'te kabin ekseni Z (Blender Z-yukari): -90 X ile dik (Y-yukari) yapilir; sonra sap yonu icin dondurulur
        model.transform.localRotation = Quaternion.Euler(-90f, handleYaw, 0f);
        model.transform.localScale = Vector3.one;
        model.transform.localPosition = Vector3.zero;
        {
            // Ekseni ve tabani bul: en alttaki %10'luk tepelerin merkezi (sap bu bolgede degil)
            float minY = float.MaxValue;
            var pts = new List<Vector3>();
            foreach (var mf in model.GetComponentsInChildren<MeshFilter>())
                foreach (var v in mf.sharedMesh.vertices) { var w = mf.transform.TransformPoint(v); pts.Add(w); minY = Mathf.Min(minY, w.y); }
            float sx = 0f, sz = 0f; int cnt = 0;
            foreach (var w in pts) if (w.y < minY + H * 0.1f) { sx += w.x; sz += w.z; cnt++; }
            Vector3 axis = new Vector3(sx / Mathf.Max(1, cnt), minY, sz / Mathf.Max(1, cnt));
            model.transform.localPosition = new Vector3(-axis.x, -H * 0.5f - axis.y, -axis.z);
            Debug.Log($"[Item] {name}: eksen merkezi=({axis.x:F3},{axis.z:F3}) taban y={minY:F3}");
        }
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterials = new[] { glass };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        var b = new Bounds(); bool first = true;
        foreach (var r in model.GetComponentsInChildren<Renderer>()) { if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds); }
        Debug.Log($"[Item] {name}: model sinirlari merkez={b.center:F3} boyut={b.size:F3} (beklenen yukseklik {H:F3})");

        var inner = new List<Vector2>();
        foreach (var p in pd.pts) inner.Add(new Vector2(p.x - H * 0.5f, p.y));      // merkezli kok uzayi
        var water = new GameObject("Water");
        water.transform.SetParent(root.transform, false);
        var mesh = BucketProfile.BuildWaterMesh(inner, out float vol, out float[] table, out float rim, out float floorY, out float fullY);
        Directory.CreateDirectory(Mdl + "Parts");
        string meshPath = Mdl + "Parts/" + name + "WaterVolume.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(mesh, meshPath);
        mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        var bw = water.AddComponent<BucketWater>();
        bw.emptyY = floorY; bw.fullY = fullY; bw.rimRadius = rim; bw.totalVolume = vol; bw.levelTable = table;
        water.transform.localPosition = new Vector3(0f, floorY, 0f);
        Debug.Log($"[Item] {name}: ic taban={floorY:F3} agiz={fullY:F3} agiz yaricapi={rim:F3} hacim={vol * 1e6f:F0} mL (model birimi)");

        var body = new GameObject("WaterBody");
        body.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = body.AddComponent<MeshRenderer>();
        mr.sharedMaterial = liquid;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        body.transform.SetParent(root.transform, false);
        body.SetActive(false);
        root.transform.localScale = Vector3.one * scale;
        string pp = $"{Prefabs}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, pp);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // El aleti: uzun ekseni one (+Z) bakar, kazan (genis) uc one; merkez orijinde
    public static GameObject BuildTool(string name, string fbx, Material[] mats, float length, float rollDegrees)
    {
        FixModel(fbx);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var root = new GameObject(name);
        var pivot = new GameObject("Pivot"); pivot.transform.SetParent(root.transform, false);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, pivot.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        var rends = model.GetComponentsInChildren<MeshRenderer>();
        foreach (var r in rends) { var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)]; for (int i = 0; i < arr.Length; i++) arr[i] = mats[Mathf.Min(i, mats.Length - 1)]; r.sharedMaterials = arr; }
        Bounds Bnd() { var bb = new Bounds(); bool f = true; foreach (var r in rends) { if (f) { bb = r.bounds; f = false; } else bb.Encapsulate(r.bounds); } return bb; }
        var b0 = Bnd();
        int axis = b0.size.x >= b0.size.y && b0.size.x >= b0.size.z ? 0 : (b0.size.y >= b0.size.z ? 1 : 2);
        // uzun ekseni +Z'ye cevir
        if (axis == 0) pivot.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        else if (axis == 1) pivot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        // kazan ucunu bul: +Z yarisi ve -Z yarisi icin yanal yayilim
        var mf = model.GetComponentInChildren<MeshFilter>();
        var verts = mf.sharedMesh.vertices;
        float zMin = float.MaxValue, zMax = float.MinValue;
        var wp = new Vector3[verts.Length];
        for (int i = 0; i < verts.Length; i++)
        {
            wp[i] = root.transform.InverseTransformPoint(mf.transform.TransformPoint(verts[i]));
            zMin = Mathf.Min(zMin, wp[i].z); zMax = Mathf.Max(zMax, wp[i].z);
        }
        float lo = zMin + (zMax - zMin) * 0.18f, hi = zMax - (zMax - zMin) * 0.18f;
        float sLo = 0, sHi = 0; int nLo = 0, nHi = 0;
        foreach (var p in wp) { if (p.z <= lo) { sLo += Mathf.Abs(p.x) + Mathf.Abs(p.y); nLo++; } if (p.z >= hi) { sHi += Mathf.Abs(p.x) + Mathf.Abs(p.y); nHi++; } }
        float spreadLo = nLo > 0 ? sLo / nLo : 0f, spreadHi = nHi > 0 ? sHi / nHi : 0f;
        if (spreadLo > spreadHi) pivot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * pivot.transform.localRotation;
        pivot.transform.localRotation = pivot.transform.localRotation * Quaternion.Euler(0f, 0f, rollDegrees);
        var b1 = Bnd();
        float len = b1.size.z;
        float k = length / Mathf.Max(len, 1e-4f);
        pivot.transform.localScale = Vector3.one * k;
        var b2 = Bnd();
        pivot.transform.localPosition = -(b2.center - root.transform.position);
        Debug.Log($"[Item] {name}: eksen={axis} kazan yayilimi alt={spreadLo:F3} ust={spreadHi:F3} boy={b2.size:F3}");
        string pp = $"{Prefabs}/{name}.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, pp);
        Object.DestroyImmediate(root);
        return prefab;
    }

    public static GameObject BuildTorch(Material glowMat, Material particleMat)
    {
        FixModel(Mdl + "Torch.fbx");
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(Mdl + "Torch.fbx");
        var root = new GameObject("Torch");
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        // FBX dugumlerinde 0.01 olcek/oteleme kalmis (mesh zaten dogru boyutta): hepsini sifirla
        foreach (var tr in model.GetComponentsInChildren<Transform>(true))
        { tr.localPosition = Vector3.zero; tr.localScale = Vector3.one; }
        string M = Art + "/Materials/";
        // [metal, wood, leather, rope, metallic_clean]
        var metal = Lit(M + "Torch_Metal.mat", new Color(0.42f, 0.44f, 0.55f), null, 0.5f, 0.6f);
        var wood = Lit(M + "Torch_Wood.mat", new Color(0.32f, 0.18f, 0.09f), null, 0.15f);
        var leather = Lit(M + "Torch_Leather.mat", new Color(0.55f, 0.36f, 0.2f), null, 0.2f);
        var rope = Lit(M + "Torch_Rope.mat", new Color(0.62f, 0.5f, 0.3f), null, 0.1f);
        var clean = Lit(M + "Torch_MetalClean.mat", new Color(0.5f, 0.52f, 0.62f), null, 0.6f, 0.7f);
        Transform flame = null;
        foreach (var r in model.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.name.ToLower().Contains("flame"))
            {
                r.sharedMaterials = new[] { glowMat };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                flame = r.transform;
            }
            else
            {
                var arr = new Material[Mathf.Max(1, r.sharedMaterials.Length)];
                var order = new[] { metal, wood, leather, rope, clean };
                for (int i = 0; i < arr.Length; i++) arr[i] = order[Mathf.Min(i, order.Length - 1)];
                r.sharedMaterials = arr;
            }
        }
        // isik
        var lg = new GameObject("TorchLight");
        lg.transform.SetParent(root.transform, false);
        lg.transform.localPosition = new Vector3(0f, 1.55f, 0f);
        var li = lg.AddComponent<Light>();
        li.type = LightType.Point; li.color = new Color(1f, 0.6f, 0.25f); li.range = 11f; li.intensity = 2.2f; li.shadows = LightShadows.None;
        // Gercek alev: ust uste binen yumusak alev parcaciklari (yumusak ucgen sekilli, toplamsal), ic cekirdek, kivilcim ve duman
        var fireMat = ParticleMaterial("TorchFire.mat", true);
        var smokeMat = ParticleMaterial("TorchSmoke.mat", false);
        Transform FireSystem(string nm, float y, Material mat, out ParticleSystem outPs)
        {
            var go = new GameObject(nm);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, y, 0f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);      // konisel yayilim yukari baksin
            var p = go.AddComponent<ParticleSystem>();
            p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            outPs = p;
            return go.transform;
        }

        // 1) dis alev
        FireSystem("Flame", 1.36f, fireMat, out var flamePs);
        {
            var m = flamePs.main;
            m.loop = true; m.playOnAwake = true; m.maxParticles = 80;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.85f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.0f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(1f, 1f, 1f, 0.8f);
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            var e = flamePs.emission; e.rateOverTime = 46f;
            var s = flamePs.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 5f; s.radius = 0.04f;
            var sz = flamePs.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.22f, 1f), new Keyframe(1f, 0.08f)));
            var cl = flamePs.colorOverLifetime; cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.95f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.62f, 0.16f), 0.4f), new GradientColorKey(new Color(0.85f, 0.14f, 0.02f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.12f), new GradientAlphaKey(0.55f, 0.65f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            var nz = flamePs.noise; nz.enabled = true; nz.strength = 0.1f; nz.frequency = 1.3f; nz.scrollSpeed = 0.8f; nz.damping = true;
            var rot = flamePs.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
        }
        // 2) sicak ic cekirdek
        FireSystem("FlameCore", 1.33f, fireMat, out var corePs);
        {
            var m = corePs.main;
            m.loop = true; m.playOnAwake = true; m.maxParticles = 40;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.28f, 0.45f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.13f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(1f, 0.92f, 0.6f, 0.65f);
            m.simulationSpace = ParticleSystemSimulationSpace.Local;
            var e = corePs.emission; e.rateOverTime = 26f;
            var s = corePs.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 3f; s.radius = 0.025f;
            var sz = corePs.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.1f)));
            var cl = corePs.colorOverLifetime; cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 1f, 0.85f), 0f), new GradientColorKey(new Color(1f, 0.8f, 0.3f), 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
        }
        // 3) kivilcimlar
        FireSystem("Embers", 1.5f, fireMat, out var emberPs);
        {
            var m = emberPs.main;
            m.loop = true; m.playOnAwake = true; m.maxParticles = 30;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.7f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.04f);
            m.startColor = new Color(1f, 0.7f, 0.25f, 1f);
            m.gravityModifier = -0.12f;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = emberPs.emission; e.rateOverTime = 7f;
            var s = emberPs.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 18f; s.radius = 0.05f;
            var cl = emberPs.colorOverLifetime; cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            var nz = emberPs.noise; nz.enabled = true; nz.strength = 0.5f; nz.frequency = 0.7f;
        }
        // 4) ince duman
        FireSystem("Smoke", 1.62f, smokeMat, out var smokePs);
        {
            var m = smokePs.main;
            m.loop = true; m.playOnAwake = true; m.maxParticles = 24;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.3f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.32f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(0.12f, 0.1f, 0.09f, 0.28f);
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            var e = smokePs.emission; e.rateOverTime = 5f;
            var s = smokePs.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 10f; s.radius = 0.04f;
            var sz = smokePs.sizeOverLifetime; sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 1.6f)));
            var cl = smokePs.colorOverLifetime; cl.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            cl.color = g;
            var nz = smokePs.noise; nz.enabled = true; nz.strength = 0.25f; nz.frequency = 0.5f;
        }
        // Eski sabit mesh alev kapatilir: yerini parcacik alev aldi
        if (flame != null) flame.gameObject.SetActive(false);
        var tf = root.AddComponent<TorchFlicker>();
        tf.torchLight = li; tf.flame = flame; tf.baseIntensity = 2.2f;
        root.transform.localScale = Vector3.one * 1.3f;
        string pp = $"{Prefabs}/Torch.prefab";
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, pp);
        Object.DestroyImmediate(root);
        return prefab;
    }

    public class Built { public GameObject[] vessels = new GameObject[3]; public GameObject[] tools = new GameObject[3]; public GameObject torch; }

    // bucket/pickaxe: mevcut prefablar (3. kademe)
    public static Built Build(Material liquid, Material particle, GameObject bucket, GameObject pickaxe)
    {
        var res = new Built();
        foreach (var n in new[] { "Spatula_normal" }) NormalMap(Tx + n + ".png");
        foreach (var n in new[] { "Spatula_base", "Spatula_rough", "Spatula_metal", "Spoon_Color" }) AssetDatabase.ImportAsset(Tx + n + ".png", ImportAssetOptions.ForceUpdate);
        string M = Art + "/Materials/";
        var glass = Glass(M + "Glass.mat");
        var ceramic = Ceramic(M + "MugCeramic.mat");
        res.vessels[0] = BuildVessel("Mug", Mdl + "Mug.fbx", Mdl + "Mug_profile.json", 1.7f, ceramic, liquid, -90f);
        res.vessels[1] = BuildVessel("Jug", Mdl + "Jug.fbx", Mdl + "Jug_profile.json", 1.0f, glass, liquid, 180f);
        res.vessels[2] = bucket;

        var spoonMat = Lit(M + "Spoon.mat", Color.white, Tex(Tx + "Spoon_Color.png"), 0.45f, 0.2f);
        var spat = Lit(M + "Spatula.mat", Color.white, Tex(Tx + "Spatula_base.png"), 0.4f, 0.3f);
        spat.SetTexture("_BumpMap", Tex(Tx + "Spatula_normal.png")); spat.EnableKeyword("_NORMALMAP"); spat.SetFloat("_BumpScale", 1f);
        res.tools[0] = BuildTool("Spoon", Mdl + "Spoon.fbx", new[] { spoonMat }, 0.40f, 0f);
        res.tools[1] = BuildTool("Spatula", Mdl + "Spatula.fbx", new[] { spat }, 0.52f, 0f);
        res.tools[2] = pickaxe;

        var flameMat = AssetDatabase.LoadAssetAtPath<Material>(M + "TorchFlame.mat");
        if (flameMat == null) { flameMat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(flameMat, M + "TorchFlame.mat"); }
        flameMat.shader = Shader.Find("Universal Render Pipeline/Unlit");
        flameMat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.12f));
        EditorUtility.SetDirty(flameMat);
        res.torch = BuildTorch(flameMat, particle);
        return res;
    }
}

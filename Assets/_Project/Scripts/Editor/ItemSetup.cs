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
    public static GameObject BuildVessel(string name, string fbx, string profileJson, float scale, Material glass, Material liquid)
    {
        FixModel(fbx);
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var pd = ReadProfile(profileJson);
        float H = pd.height;
        var root = new GameObject(name);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(asset, root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        model.transform.localPosition = new Vector3(0f, -H * 0.5f, 0f);
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;
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
        lg.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        var li = lg.AddComponent<Light>();
        li.type = LightType.Point; li.color = new Color(1f, 0.6f, 0.25f); li.range = 11f; li.intensity = 2.2f; li.shadows = LightShadows.None;
        // kivilcim/alev parcaciklari
        var psGo = new GameObject("Embers");
        psGo.transform.SetParent(root.transform, false);
        psGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        var ps = psGo.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.loop = true; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
        main.startColor = new Color(1f, 0.7f, 0.25f, 0.9f);
        main.gravityModifier = -0.25f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var em = ps.emission; em.rateOverTime = 14f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 0.06f;
        var col = ps.colorOverLifetime; col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.25f, 0.05f), 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = grad;
        var psr = psGo.GetComponent<ParticleSystemRenderer>();
        psr.sharedMaterial = particleMat;
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
        res.vessels[0] = BuildVessel("Mug", Mdl + "Mug.fbx", Mdl + "Mug_profile.json", 1.7f, glass, liquid);
        res.vessels[1] = BuildVessel("Jug", Mdl + "Jug.fbx", Mdl + "Jug_profile.json", 1.0f, glass, liquid);
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

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

/// <summary>
/// Generates the 4 core geometric Modular Prefabs (Block, Wedge, OuterCorner, InnerCorner)
/// in Assets/Prefabs/ModularKit/. Swap Material and Scale per instance to build any level.
/// </summary>
public static class RemakeModularKitBuilder
{
    private const string KitFolder = "Assets/Prefabs/ModularKit";

    [MenuItem("Remake/Create 4 Modular Kit Prefabs")]
    public static void CreateModularKitPrefabs()
    {
        EnsureFolder("Assets/Prefabs", "ModularKit");

        var defaultMatPath = "Assets/Materials/M_Modular_Default.mat";
        var defaultMat = AssetDatabase.LoadAssetAtPath<Material>(defaultMatPath);
        if (defaultMat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            defaultMat = new Material(shader);
            AssetDatabase.CreateAsset(defaultMat, defaultMatPath);
        }
        defaultMat.SetTexture("_BaseMap", null);
        defaultMat.SetTexture("_BumpMap", null);
        defaultMat.SetColor("_BaseColor", new Color(0.82f, 0.84f, 0.86f));
        defaultMat.SetFloat("_Smoothness", 0.2f);
        EditorUtility.SetDirty(defaultMat);

        CreateSingleBlockPrefab("Mod_Block", 4f, 4f, 4f, defaultMat);
        CreateWedgePrefab("Mod_Wedge", 4f, 4f, 4f, defaultMat);
        CreateOuterCornerPrefab("Mod_OuterCorner", 4f, 4f, 4f, defaultMat);
        CreateInnerCornerPrefab("Mod_InnerCorner", 4f, 4f, 4f, defaultMat);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RemakeModularKit] Created 4 geometric Modular Prefabs in Assets/Prefabs/ModularKit/!");
    }

    [MenuItem("Remake/Create Exclamation Switch Prefab")]
    public static void CreateExclamationSwitchPrefab()
    {
        EnsureFolder("Assets/Prefabs", "ModularKit");
        EnsureFolder("Assets", "Materials");

        var goldMat = GetOrCreateLitColorMat("Assets/Materials/M_Switch_Gold.mat", new Color(0.92f, 0.72f, 0.14f), 0.35f);
        var redMat = GetOrCreateLitColorMat("Assets/Materials/M_Switch_Red.mat", new Color(0.86f, 0.11f, 0.11f), 0.40f);
        var whiteMat = GetOrCreateLitColorMat("Assets/Materials/M_Switch_White.mat", Color.white, 0.2f);

        var root = new GameObject("Mod_ExclamationSwitch");

        // 1. Golden/Yellow square base plate
        var basePb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(2.4f, 0.22f, 2.4f));
        basePb.gameObject.name = "BasePlate";
        basePb.transform.SetParent(root.transform, false);
        basePb.transform.localPosition = new Vector3(0f, 0.11f, 0f);
        basePb.GetComponent<MeshRenderer>().sharedMaterial = goldMat;
        basePb.ToMesh();
        basePb.Refresh();
        basePb.gameObject.AddComponent<BoxCollider>();

        // 2. Red beveled frustum button cap
        var capRoot = new GameObject("ButtonCap");
        capRoot.transform.SetParent(root.transform, false);
        capRoot.transform.localPosition = new Vector3(0f, 0.22f, 0f);

        float bw = 0.95f, tw = 0.80f, h = 0.22f;
        Vector3[] verts =
        {
            new(-bw, 0f, -bw), new(bw, 0f, -bw), new(bw, 0f, bw), new(-bw, 0f, bw),
            new(-tw, h,  -tw), new(tw, h,  -tw), new(tw, h,  tw), new(-tw, h,  tw)
        };
        var faces = new List<Face>
        {
            new(new[] { 0, 1, 2, 0, 2, 3 }), // bottom
            new(new[] { 4, 7, 6, 4, 6, 5 }), // top
            new(new[] { 0, 4, 5, 0, 5, 1 }), // front
            new(new[] { 1, 5, 6, 1, 6, 2 }), // right
            new(new[] { 2, 6, 7, 2, 7, 3 }), // back
            new(new[] { 3, 7, 4, 3, 4, 0 })  // left
        };
        var capPb = ProBuilderMesh.Create(verts, faces);
        capPb.gameObject.name = "RedButtonMesh";
        capPb.transform.SetParent(capRoot.transform, false);
        capPb.GetComponent<MeshRenderer>().sharedMaterial = redMat;
        capPb.ToMesh();
        capPb.Refresh();
        var capCol = capPb.gameObject.AddComponent<BoxCollider>();
        capCol.center = new Vector3(0f, h * 0.5f, 0f);
        capCol.size = new Vector3(bw * 2f, h, bw * 2f);

        // 3. White '!' symbol on top of the red button
        var barPb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(0.24f, 0.03f, 0.68f));
        barPb.gameObject.name = "ExclamationBar";
        barPb.transform.SetParent(capRoot.transform, false);
        barPb.transform.localPosition = new Vector3(0f, h + 0.015f, 0.16f);
        barPb.GetComponent<MeshRenderer>().sharedMaterial = whiteMat;
        barPb.ToMesh();
        barPb.Refresh();

        var dotPb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(0.24f, 0.03f, 0.24f));
        dotPb.gameObject.name = "ExclamationDot";
        dotPb.transform.SetParent(capRoot.transform, false);
        dotPb.transform.localPosition = new Vector3(0f, h + 0.015f, -0.38f);
        dotPb.GetComponent<MeshRenderer>().sharedMaterial = whiteMat;
        dotPb.ToMesh();
        dotPb.Refresh();

        // 4. Trigger collider & ExclamationSwitch component
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0.42f, 0f);
        trigger.size = new Vector3(1.8f, 0.35f, 1.8f);

        var sw = root.AddComponent<ExclamationSwitch>();
        var so = new SerializedObject(sw);
        so.FindProperty("buttonCap").objectReferenceValue = capRoot.transform;
        so.ApplyModifiedPropertiesWithoutUndo();

        EnsureFolder("Assets/Prefabs", "Mechanics");
        string prefabPath = "Assets/Prefabs/Mechanics/ExclamationSwitch.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RemakeModularKit] Created Exclamation Switch Prefab at {prefabPath}!");
    }

    private static Material GetOrCreateLitColorMat(string path, Color color, float smoothness)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void CreateSingleBlockPrefab(string name, float w, float h, float d, Material mat)
    {
        var go = new GameObject(name);
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(w, h, d));
        pb.transform.SetParent(go.transform, false);
        pb.gameObject.name = "Mesh";
        if (mat != null) pb.GetComponent<MeshRenderer>().sharedMaterial = mat;
        pb.ToMesh();
        pb.Refresh();
        pb.gameObject.AddComponent<BoxCollider>();
        PrefabUtility.SaveAsPrefabAsset(go, $"{KitFolder}/{name}.prefab");
        Object.DestroyImmediate(go);
    }

    private static void CreateWedgePrefab(string name, float w, float h, float d, Material mat)
    {
        float hx = w * 0.5f, hy = h * 0.5f, hz = d * 0.5f;
        Vector3[] verts =
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz),
            new(-hx,  hy,  hz), new(hx,  hy,  hz)
        };
        var faces = new List<Face>
        {
            new(new[] { 0, 1, 2, 0, 2, 3 }),
            new(new[] { 0, 4, 5, 0, 5, 1 }),
            new(new[] { 3, 2, 5, 3, 5, 4 }),
            new(new[] { 0, 3, 4 }),
            new(new[] { 1, 5, 2 })
        };
        SaveCustomProBuilderPrefab(name, verts, faces, mat);
    }

    private static void CreateOuterCornerPrefab(string name, float w, float h, float d, Material mat)
    {
        float hx = w * 0.5f, hy = h * 0.5f, hz = d * 0.5f;
        Vector3[] verts =
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz),
            new( hx,  hy,  hz)
        };
        var faces = new List<Face>
        {
            new(new[] { 0, 1, 2, 0, 2, 3 }),
            new(new[] { 0, 4, 1 }),
            new(new[] { 0, 3, 4 }),
            new(new[] { 1, 4, 2 }),
            new(new[] { 3, 2, 4 })
        };
        SaveCustomProBuilderPrefab(name, verts, faces, mat);
    }

    private static void CreateInnerCornerPrefab(string name, float w, float h, float d, Material mat)
    {
        float hx = w * 0.5f, hy = h * 0.5f, hz = d * 0.5f;
        Vector3[] verts =
        {
            new(-hx, -hy, -hz), new(hx, -hy, -hz), new(hx, -hy, hz), new(-hx, -hy, hz),
            new( hx,  hy, -hz), new(hx,  hy,  hz), new(-hx,  hy,  hz)
        };
        var faces = new List<Face>
        {
            new(new[] { 0, 1, 2, 0, 2, 3 }),
            new(new[] { 0, 6, 5 }),
            new(new[] { 0, 5, 4 }),
            new(new[] { 0, 3, 6 }),
            new(new[] { 0, 4, 1 }),
            new(new[] { 1, 4, 5, 1, 5, 2 }),
            new(new[] { 3, 2, 5, 3, 5, 6 })
        };
        SaveCustomProBuilderPrefab(name, verts, faces, mat);
    }

    private static void SaveCustomProBuilderPrefab(string name, Vector3[] verts, List<Face> faces, Material mat)
    {
        var pb = ProBuilderMesh.Create(verts, faces);
        pb.gameObject.name = name;
        if (mat != null) pb.GetComponent<MeshRenderer>().sharedMaterial = mat;
        pb.ToMesh();
        pb.Refresh();
        var mc = pb.gameObject.AddComponent<MeshCollider>();
        mc.convex = true;

        PrefabUtility.SaveAsPrefabAsset(pb.gameObject, $"{KitFolder}/{name}.prefab");
        Object.DestroyImmediate(pb.gameObject);
    }

    private static void EnsureFolder(string parent, string child)
    {
        if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
            AssetDatabase.CreateFolder(parent, child);
    }
}

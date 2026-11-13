using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.ProBuilder;

public static class RemakeBobOmbIslandLook
{
    private const string ScenePath = "Assets/Scenes/BobOmbBattlefield.unity";

    [MenuItem("Remake/Fix Bob-omb Battlefield Holes & Reference Palette")]
    public static void ApplyIslandLook()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Remove the ocean plane that was showing through floor gaps
        var oldOcean = GameObject.Find("Remake_SurroundingOcean");
        if (oldOcean != null) Object.DestroyImmediate(oldOcean);

        var oldPatch = GameObject.Find("Remake_FloorPatch");
        if (oldPatch != null) Object.DestroyImmediate(oldPatch);

        // Measure SM64_BOB_StaticProBuilder bounds
        var staticGeo = GameObject.Find("SM64_BOB_StaticProBuilder");
        Bounds mapBounds = new Bounds(Vector3.zero, new Vector3(140f, 30f, 140f));
        if (staticGeo != null)
        {
            var rends = staticGeo.GetComponentsInChildren<Renderer>(true);
            if (rends.Length > 0)
            {
                mapBounds = rends[0].bounds;
                for (int i = 1; i < rends.Length; i++)
                    mapBounds.Encapsulate(rends[i].bounds);
            }
        }
        Debug.Log($"[RemakeBobOmb] Map bounds: center={mapBounds.center}, min={mapBounds.min}, max={mapBounds.max}");

        // Create a solid ProBuilder base floor & perimeter grass skirt beneath the island
        // so zero gaps/holes expose void below the courtyard or meadows
        var patchRoot = new GameObject("Remake_FloorPatch");
        var grassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Grass.mat");
        var stoneMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_Stone.mat");

        float baseFloorY = mapBounds.min.y - 0.15f;
        CreatePBFloor(patchRoot.transform, "PB_ValleyBase_Grass",
            new Vector3(mapBounds.center.x, baseFloorY - 0.5f, mapBounds.center.z),
            new Vector3(mapBounds.size.x + 80f, 1.0f, mapBounds.size.z + 80f),
            grassMat);

        // Seal the courtyard/gate depression with stone floor just below courtyard Y
        CreatePBFloor(patchRoot.transform, "PB_CourtyardSeal_Stone",
            new Vector3(mapBounds.center.x - 10f, mapBounds.min.y + 0.05f, mapBounds.center.z),
            new Vector3(mapBounds.size.x * 0.65f, 0.5f, mapBounds.size.z * 0.65f),
            stoneMat);

        // Bright blue sky & light horizon haze
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = new Color(0.58f, 0.83f, 0.99f);
        RenderSettings.fogDensity = 0.0012f;

        var skyMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_SunlitSkybox.mat");
        if (skyMat != null)
        {
            if (skyMat.HasProperty("_SkyTint")) skyMat.SetColor("_SkyTint", new Color(0.22f, 0.62f, 0.98f));
            if (skyMat.HasProperty("_GroundColor")) skyMat.SetColor("_GroundColor", new Color(0.42f, 0.76f, 0.98f));
            if (skyMat.HasProperty("_TopColor")) skyMat.SetColor("_TopColor", new Color(0.16f, 0.54f, 0.94f));
            if (skyMat.HasProperty("_HorizonColor")) skyMat.SetColor("_HorizonColor", new Color(0.68f, 0.89f, 1.0f));
            if (skyMat.HasProperty("_BaseColor")) skyMat.SetColor("_BaseColor", new Color(0.32f, 0.72f, 0.98f));
            EditorUtility.SetDirty(skyMat);
            RenderSettings.skybox = skyMat;
        }

        // Reference palette colors
        SetBaseColor("Assets/Materials/Mat_Grass.mat", new Color(0.55f, 0.74f, 0.18f));
        SetBaseColor("Assets/Materials/Mat_Stone.mat", new Color(0.63f, 0.57f, 0.49f));
        SetBaseColor("Assets/Materials/Mat_Terracotta.mat", new Color(0.60f, 0.36f, 0.24f));

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static void CreatePBFloor(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        pb.gameObject.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.position = pos;
        var mr = pb.GetComponent<MeshRenderer>();
        if (mr != null && mat != null) mr.sharedMaterial = mat;
        pb.ToMesh();
        pb.Refresh();
        if (pb.GetComponent<MeshCollider>() == null)
            pb.gameObject.AddComponent<MeshCollider>();
    }

    private static void SetBaseColor(string matPath, Color c)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat != null && mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", c);
            EditorUtility.SetDirty(mat);
        }
    }
}

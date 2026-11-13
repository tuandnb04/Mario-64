using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class WaterPrefabCreator
{
    private const string PrefabPath = "Assets/Prefabs/Environment/Water.prefab";

    [InitializeOnLoadMethod]
    private static void EnsureWaterPrefabOnLoad()
    {
        if (File.Exists(PrefabPath)) return;
        EditorApplication.delayCall += () =>
        {
            if (!File.Exists(PrefabPath)) CreateWaterPrefab();
        };
    }

    [MenuItem("Remake/Create Water Prefab")]
    public static void CreateWaterPrefab()
    {
        const string prefabDir = "Assets/Prefabs/Environment";
        const string prefabPath = prefabDir + "/Water.prefab";
        if (!Directory.Exists(prefabDir))
        {
            Directory.CreateDirectory(prefabDir);
            AssetDatabase.Refresh();
        }

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Water";
        go.transform.localScale = new Vector3(10f, 4f, 10f);

        var box = go.GetComponent<BoxCollider>();
        if (box != null) box.isTrigger = true;

        var mr = go.GetComponent<MeshRenderer>();
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Mat_WaterSurface.mat");
        if (mr != null && mat != null)
        {
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        go.AddComponent<WaterVolume>();

        PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        AssetDatabase.SaveAssets();
        Debug.Log("[WaterPrefabCreator] Created " + prefabPath);
    }
}


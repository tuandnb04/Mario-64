using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RemakeBobOmbTerrainBuilder
{
    private const string ScenePath = "Assets/Scenes/BobOmbBattlefield.unity";
    private const string TerrainAssetFolder = "Assets/Settings/RemakeTerrains";

    [MenuItem("Remake/Build High-Res Smooth Terrain (Bob-omb Battlefield)")]
    public static void BuildSmoothTerrain()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Clean up previous terrain or floor patches
        var oldPatch = GameObject.Find("Remake_FloorPatch");
        if (oldPatch != null) Object.DestroyImmediate(oldPatch);
        var oldTerrain = GameObject.Find("Remake_SmoothTerrain");
        if (oldTerrain != null) Object.DestroyImmediate(oldTerrain);

        var staticGeo = GameObject.Find("SM64_BOB_StaticProBuilder");
        if (staticGeo == null)
        {
            Debug.LogError("[RemakeTerrain] SM64_BOB_StaticProBuilder not found!");
            return;
        }

        // Ensure staticGeo is active during raycast sampling
        staticGeo.SetActive(true);
        var rends = staticGeo.GetComponentsInChildren<Renderer>(true);
        Bounds bounds = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++)
            bounds.Encapsulate(rends[i].bounds);

        // Ensure colliders exist on staticGeo for accurate height sampling
        var meshFilters = staticGeo.GetComponentsInChildren<MeshFilter>(true);
        foreach (var mf in meshFilters)
        {
            if (mf.GetComponent<Collider>() == null && mf.sharedMesh != null)
                mf.gameObject.AddComponent<MeshCollider>();
        }
        Physics.SyncTransforms();

        int res = 257;
        float pad = 16f;
        float terrainW = bounds.size.x + pad * 2f;
        float terrainL = bounds.size.z + pad * 2f;
        float minY = bounds.min.y - 2f;
        float maxH = bounds.size.y + 8f;

        float[,] heights = new float[res, res];
        float rayStartY = bounds.max.y + 30f;
        float rayDist = maxH + 60f;

        // Sample existing colliders on staticGeo
        for (int z = 0; z < res; z++)
        {
            float tz = (float)z / (res - 1);
            float worldZ = (bounds.min.z - pad) + tz * terrainL;
            for (int x = 0; x < res; x++)
            {
                float tx = (float)x / (res - 1);
                float worldX = (bounds.min.x - pad) + tx * terrainW;

                float sampledY = minY + 1.5f;
                var hits = Physics.RaycastAll(new Vector3(worldX, rayStartY, worldZ), Vector3.down, rayDist);
                if (hits.Length > 0)
                {
                    float highestGroundY = float.MinValue;
                    float lowestGroundY = float.MaxValue;
                    bool anyStatic = false;
                    foreach (var h in hits)
                    {
                        if (!h.collider.transform.IsChildOf(staticGeo.transform)) continue;
                        anyStatic = true;
                        if (h.point.y > highestGroundY) highestGroundY = h.point.y;
                        if (h.point.y < lowestGroundY) lowestGroundY = h.point.y;
                    }
                    if (anyStatic)
                    {
                        // If there is a >6m vertical gap (e.g. Floating Sky Island above meadow), sample the ground below it
                        sampledY = (highestGroundY - lowestGroundY > 6f) ? lowestGroundY : highestGroundY;
                    }
                }
                heights[z, x] = Mathf.Clamp01((sampledY - minY) / maxH);
            }
        }

        // Multi-pass Gaussian smoothing to eliminate low-poly faceting
        heights = SmoothHeights(heights, res, passes: 3);

        // Create TerrainData & TerrainLayers
        EnsureFolder(TerrainAssetFolder);
        string tdPath = $"{TerrainAssetFolder}/BobOmb_TerrainData.asset";
        var terrainData = AssetDatabase.LoadAssetAtPath<TerrainData>(tdPath);
        if (terrainData == null)
        {
            terrainData = new TerrainData();
            AssetDatabase.CreateAsset(terrainData, tdPath);
        }

        terrainData.heightmapResolution = res;
        terrainData.size = new Vector3(terrainW, maxH, terrainL);
        terrainData.SetHeights(0, 0, heights);

        var grassLayer = CreateOrLoadLayer("TL_BobOmb_Grass", "T_BobOmb_Grass", new Color(0.55f, 0.74f, 0.18f), "Assets/Textures/RemakeStylized/T_StylizedGrass_Normal.png");
        var dirtLayer = CreateOrLoadLayer("TL_BobOmb_Dirt", "T_BobOmb_Dirt", new Color(0.76f, 0.58f, 0.36f), "Assets/Textures/RemakeStylized/T_StylizedStone_Normal.png");
        var rockLayer = CreateOrLoadLayer("TL_BobOmb_Rock", "T_BobOmb_Rock", new Color(0.58f, 0.52f, 0.44f), "Assets/Textures/RemakeStylized/T_StylizedStone_Normal.png");

        terrainData.terrainLayers = new[] { grassLayer, dirtLayer, rockLayer };

        // Paint slope-based splatmap (flat = grass, medium slope = dirt path/transition, steep = cliff rock)
        int alphaRes = 256;
        terrainData.alphamapResolution = alphaRes;
        float[,,] alphaMap = new float[alphaRes, alphaRes, 3];
        for (int y = 0; y < alphaRes; y++)
        {
            for (int x = 0; x < alphaRes; x++)
            {
                float normX = (float)x / (alphaRes - 1);
                float normY = (float)y / (alphaRes - 1);
                float steepness = terrainData.GetSteepness(normX, normY);

                float rockW = Mathf.SmoothStep(0f, 1f, (steepness - 28f) / 18f);
                float dirtW = Mathf.SmoothStep(0f, 1f, (steepness - 14f) / 10f) * (1f - rockW);
                float grassW = Mathf.Max(0f, 1f - rockW - dirtW);

                float sum = grassW + dirtW + rockW;
                alphaMap[y, x, 0] = grassW / sum;
                alphaMap[y, x, 1] = dirtW / sum;
                alphaMap[y, x, 2] = rockW / sum;
            }
        }
        terrainData.SetAlphamaps(0, 0, alphaMap);

        var terrainGO = Terrain.CreateTerrainGameObject(terrainData);
        terrainGO.name = "Remake_SmoothTerrain";
        terrainGO.transform.position = new Vector3(bounds.min.x - pad, minY, bounds.min.z - pad);

        var terrain = terrainGO.GetComponent<Terrain>();
        terrain.heightmapPixelError = 2f;
        terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;

        // Hide the old low-poly SM64_BOB_StaticProBuilder mesh
        staticGeo.SetActive(false);

        EditorUtility.SetDirty(terrainData);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[RemakeBobOmbTerrainBuilder] Built smooth high-res Unity Terrain for BobOmbBattlefield!");
    }

    private static float[,] SmoothHeights(float[,] src, int res, int passes)
    {
        float[,] current = src;
        for (int p = 0; p < passes; p++)
        {
            float[,] next = new float[res, res];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float sum = 0f;
                    int count = 0;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int nz = Mathf.Clamp(z + dz, 0, res - 1);
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = Mathf.Clamp(x + dx, 0, res - 1);
                            int w = (dx == 0 && dz == 0) ? 4 : (dx == 0 || dz == 0 ? 2 : 1);
                            sum += current[nz, nx] * w;
                            count += w;
                        }
                    }
                    next[z, x] = sum / count;
                }
            }
            current = next;
        }
        return current;
    }

    private static TerrainLayer CreateOrLoadLayer(string layerName, string texName, Color tint, string normalPath)
    {
        string layerPath = $"{TerrainAssetFolder}/{layerName}.terrainlayer";
        var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
        if (layer == null)
        {
            layer = new TerrainLayer();
            AssetDatabase.CreateAsset(layer, layerPath);
        }

        string texPath = $"Assets/Textures/RemakeStylized/{texName}.png";
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        if (tex == null)
        {
            var t = new Texture2D(128, 128, TextureFormat.RGBA32, true);
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float n = Mathf.PerlinNoise(x * 0.12f, y * 0.12f);
                    float shade = Mathf.Lerp(0.82f, 1.08f, n);
                    t.SetPixel(x, y, new Color(tint.r * shade, tint.g * shade, tint.b * shade, 1f));
                }
            }
            t.Apply();
            File.WriteAllBytes(texPath, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        layer.diffuseTexture = tex;
        layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
        layer.tileSize = new Vector2(10f, 10f);
        layer.smoothness = 0.15f;
        EditorUtility.SetDirty(layer);
        return layer;
    }

    private static void EnsureFolder(string folder)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets/Settings", "RemakeTerrains");
    }
}

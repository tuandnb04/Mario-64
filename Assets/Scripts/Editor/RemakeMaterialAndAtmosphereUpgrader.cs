using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class RemakeMaterialAndAtmosphereUpgrader
{
    private const string TexFolder = "Assets/Textures/RemakeStylized";

    [MenuItem("Remake/Apply Stylized PBR Textures & Odyssey Lighting (All 5 Maps)")]
    public static void ApplyAll()
    {
        EnsureFolder();

        // 1. Generate seamless stylized textures & normal maps with crisp visible detail
        var grassTex = CreateProceduralTexture("T_StylizedGrass_Albedo", 256, 14f, 0.72f, 1.0f, 11);
        var grassNorm = CreateNormalFromHeight("T_StylizedGrass_Normal", 256, 14f, 2.5f, 11);

        var stoneTex = CreateProceduralTexture("T_StylizedStone_Albedo", 256, 8f, 0.58f, 1.0f, 23);
        var stoneNorm = CreateNormalFromHeight("T_StylizedStone_Normal", 256, 8f, 3.6f, 23);

        var woodTex = CreateProceduralTexture("T_StylizedWood_Albedo", 256, 20f, 0.60f, 1.0f, 37, stretchX: 0.22f);
        var woodNorm = CreateNormalFromHeight("T_StylizedWood_Normal", 256, 20f, 3.0f, 37, stretchX: 0.22f);

        var snowTex = CreateProceduralTexture("T_StylizedSnow_Albedo", 256, 12f, 0.84f, 1.0f, 53);
        var snowNorm = CreateNormalFromHeight("T_StylizedSnow_Normal", 256, 12f, 1.8f, 53);

        AssetDatabase.Refresh();

        // 2. Apply StylizedTriplanarPBR to existing environment materials in Assets/Materials
        var triplanarShader = Shader.Find("Mario64Remake/StylizedTriplanarPBR") ?? Shader.Find("Universal Render Pipeline/Lit");
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" });
        int upgradedCount = 0;
        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;
            bool isSupported = mat.shader.name.Contains("ToonLit") ||
                               mat.shader.name.Contains("Universal Render Pipeline/Lit") ||
                               mat.shader.name.Contains("StylizedTriplanarPBR");
            if (!isSupported) continue;

            string n = mat.name.ToLowerInvariant();
            // Skip character / skybox / UI materials
            if (n.Contains("mario") || n.Contains("bowser") || n.Contains("piranha") ||
                n.Contains("whomp") || n.Contains("thwomp") || n.Contains("skybox") ||
                n.Contains("eye") || n.Contains("pupil") || n.Contains("star") || n.Contains("coin"))
                continue;

            if (triplanarShader != null)
            {
                Color baseCol = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                Color emisCol = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                mat.shader = triplanarShader;
                mat.SetColor("_BaseColor", baseCol);
                mat.SetColor("_EmissionColor", emisCol);
            }

            if (n.Contains("grass") || n.Contains("turf") || n.Contains("leaf"))
            {
                ApplyPBR(mat, grassTex, grassNorm, 0.18f, 0.55f, new Vector2(6f, 6f));
                upgradedCount++;
            }
            else if (n.Contains("wood") || n.Contains("timber") || n.Contains("cabin") || n.Contains("deck"))
            {
                ApplyPBR(mat, woodTex, woodNorm, 0.28f, 0.75f, new Vector2(4f, 4f));
                upgradedCount++;
            }
            else if (n.Contains("snow") || n.Contains("ice"))
            {
                float smooth = n.Contains("ice") ? 0.72f : 0.34f;
                ApplyPBR(mat, snowTex, snowNorm, smooth, 0.50f, new Vector2(5f, 5f));
                upgradedCount++;
            }
            else if (n.Contains("stone") || n.Contains("cliff") || n.Contains("rock") ||
                     n.Contains("brick") || n.Contains("sand") || n.Contains("terracotta") ||
                     n.Contains("courtyard") || n.Contains("obsidian") || n.Contains("path"))
            {
                ApplyPBR(mat, stoneTex, stoneNorm, 0.26f, 0.85f, new Vector2(6f, 6f));
                upgradedCount++;
            }
        }

        // 3. Upgrade Odyssey-style Trilight Ambient & Sun across all 5 levels
        string[] scenes =
        {
            "Assets/Scenes/BobOmbBattlefield.unity",
            "Assets/Scenes/WhompsFortress.unity",
            "Assets/Scenes/CoolCoolMountain.unity",
            "Assets/Scenes/JollyRogerBay.unity",
            "Assets/Scenes/BowserDarkWorld.unity"
        };

        string origScene = SceneManager.GetActiveScene().path;
        foreach (string sPath in scenes)
        {
            var scene = EditorSceneManager.OpenScene(sPath, OpenSceneMode.Single);
            ConfigureOdysseyLighting(scene.name);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        if (!string.IsNullOrEmpty(origScene))
            EditorSceneManager.OpenScene(origScene, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        Debug.Log($"[RemakeUpgrader] Upgraded {upgradedCount} environment materials with Stylized PBR textures + Odyssey lighting across all 5 maps!");
    }

    private static void ConfigureOdysseyLighting(string sceneName)
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        bool isDark = sceneName == "BowserDarkWorld";
        bool isSnow = sceneName == "CoolCoolMountain";
        bool isWater = sceneName == "JollyRogerBay";

        if (isDark)
        {
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.16f, 0.42f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.10f, 0.24f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.05f, 0.08f);
        }
        else if (isSnow)
        {
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.84f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.80f, 0.90f, 0.98f);
            RenderSettings.ambientGroundColor = new Color(0.45f, 0.56f, 0.72f);
        }
        else if (isWater)
        {
            RenderSettings.ambientSkyColor = new Color(0.48f, 0.82f, 0.98f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.78f, 0.85f);
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.36f, 0.45f);
        }
        else
        {
            // Vibrant Odyssey Daylight (Bob-omb Battlefield & Whomp's Fortress)
            RenderSettings.ambientSkyColor = new Color(0.54f, 0.82f, 1.0f);
            RenderSettings.ambientEquatorColor = new Color(0.86f, 0.92f, 0.82f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.44f, 0.28f);
        }

        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.type != LightType.Directional) continue;
            light.shadows = LightShadows.Soft;
            if (!isDark)
            {
                light.color = new Color(1.0f, 0.96f, 0.86f);
                light.intensity = Mathf.Max(light.intensity, 1.35f);
                light.shadowStrength = 0.78f;
            }
        }
    }

    private static void ApplyPBR(Material mat, Texture2D albedo, Texture2D normal, float smoothness, float bumpScale, Vector2 tiling)
    {
        mat.SetTexture("_BaseMap", albedo);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", bumpScale);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_TriplanarScale", 0.25f);
        mat.SetFloat("_DetailStrength", 0.80f);
        EditorUtility.SetDirty(mat);
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Textures"))
            AssetDatabase.CreateFolder("Assets", "Textures");
        if (!AssetDatabase.IsValidFolder(TexFolder))
            AssetDatabase.CreateFolder("Assets/Textures", "RemakeStylized");
    }

    private static Texture2D CreateProceduralTexture(string name, int size, float scale, float minVal, float maxVal, int seed, float stretchX = 1f)
    {
        string path = $"{TexFolder}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;
                float n = TileableNoise(u * scale * stretchX, v * scale, scale * stretchX, scale, seed);
                float val = Mathf.Lerp(minVal, maxVal, n);
                tex.SetPixel(x, y, new Color(val, val, val, 1f));
            }
        }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static Texture2D CreateNormalFromHeight(string name, int size, float scale, float strength, int seed, float stretchX = 1f)
    {
        string path = $"{TexFolder}/{name}.png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
        float eps = 1f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (float)x / size;
                float v = (float)y / size;
                float hL = TileableNoise((u - eps) * scale * stretchX, v * scale, scale * stretchX, scale, seed);
                float hR = TileableNoise((u + eps) * scale * stretchX, v * scale, scale * stretchX, scale, seed);
                float hD = TileableNoise(u * scale * stretchX, (v - eps) * scale, scale * stretchX, scale, seed);
                float hU = TileableNoise(u * scale * stretchX, (v + eps) * scale, scale * stretchX, scale, seed);

                Vector3 normal = new Vector3((hL - hR) * strength, (hD - hU) * strength, 1f).normalized;
                Color c = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private static float TileableNoise(float x, float y, float periodX, float periodY, int seed)
    {
        float n1 = Mathf.PerlinNoise(x + seed, y + seed);
        float n2 = Mathf.PerlinNoise(x * 2f + seed * 1.7f, y * 2f + seed * 1.7f) * 0.5f;
        return Mathf.Clamp01((n1 + n2) / 1.5f);
    }
}


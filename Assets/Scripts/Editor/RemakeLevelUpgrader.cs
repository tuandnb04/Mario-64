using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class RemakeLevelUpgrader
{
    private static readonly string[] LevelScenes =
    {
        "Assets/Scenes/PeachCastleHub.unity",
        "Assets/Scenes/BobOmbBattlefield.unity",
        "Assets/Scenes/WhompsFortress.unity",
        "Assets/Scenes/CoolCoolMountain.unity",
        "Assets/Scenes/JollyRogerBay.unity",
        "Assets/Scenes/BowserDarkWorld.unity"
    };

    [MenuItem("Remake/Upgrade All 5 Level Scenes")]
    public static void UpgradeAllLevels()
    {
        string originalScene = SceneManager.GetActiveScene().path;

        foreach (string scenePath in LevelScenes)
        {
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string sceneName = scene.name;
            bool isDark = sceneName == "BowserDarkWorld" || sceneName == "JollyRogerBay";

            UpgradeLighting(sceneName, isDark);
            EnsureGlobalVolume(sceneName, isDark);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[RemakeLevelUpgrader] Upgraded & saved: {sceneName}");
        }

        if (!string.IsNullOrEmpty(originalScene))
            EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);

        AssetDatabase.SaveAssets();
        Debug.Log("[RemakeLevelUpgrader] Completed upgrading all 5 level scenes!");
    }

    private static void UpgradeLighting(string sceneName, bool isDark)
    {
        foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
        {
            if (light.type == LightType.Directional)
            {
                light.shadows = LightShadows.Soft;
                light.shadowStrength = isDark ? 0.9f : 0.82f;
            }
        }

        if (sceneName == "CoolCoolMountain")
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.78f, 0.89f, 0.97f);
            RenderSettings.fogDensity = 0.0045f;
        }
        else if (sceneName == "JollyRogerBay")
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.11f, 0.37f, 0.48f);
            RenderSettings.fogDensity = 0.005f;
        }
        else if (sceneName == "BowserDarkWorld")
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.07f, 0.03f, 0.10f);
            RenderSettings.fogDensity = 0.008f;
        }
    }

    private static void EnsureGlobalVolume(string sceneName, bool isDark)
    {
        Volume volume = Object.FindAnyObjectByType<Volume>();
        if (volume == null)
        {
            var go = new GameObject("RemakeGlobalVolume");
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
        }

        string profileFolder = "Assets/Settings/RemakeProfiles";
        if (!AssetDatabase.IsValidFolder(profileFolder))
        {
            AssetDatabase.CreateFolder("Assets/Settings", "RemakeProfiles");
        }

        string profilePath = $"{profileFolder}/{sceneName}_VolumeProfile.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }

        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(isDark ? 0.55f : 0.35f);
        bloom.threshold.Override(isDark ? 0.9f : 1.0f);
        bloom.scatter.Override(0.65f);

        if (!profile.TryGet<Tonemapping>(out var tonemapping))
            tonemapping = profile.Add<Tonemapping>(true);
        tonemapping.mode.Override(TonemappingMode.ACES);

        if (!profile.TryGet<ColorAdjustments>(out var colorAdj))
            colorAdj = profile.Add<ColorAdjustments>(true);
        colorAdj.contrast.Override(isDark ? 14f : 8f);
        colorAdj.saturation.Override(isDark ? 10f : 12f);

        if (!profile.TryGet<Vignette>(out var vignette))
            vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(isDark ? 0.28f : 0.15f);
        vignette.smoothness.Override(0.42f);

        volume.sharedProfile = profile;
        EditorUtility.SetDirty(profile);
    }
}


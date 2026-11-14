using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.ProBuilder;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Peach's Castle Hub (PeachCastleHub.unity) using ProBuilder, URP 17 materials,
/// existing project prefabs, and interactive CoursePaintingPortal frames; also wires
/// Return-to-Castle portals across all courses and registers scenes in EditorBuildSettings.
/// </summary>
public static class PeachCastleHubBuilder
{
    public const string HubScenePath = "Assets/Scenes/PeachCastleHub.unity";

    private static readonly string[] AllBuildScenePaths =
    {
        "Assets/Scenes/PeachCastleHub.unity",
        "Assets/Scenes/BobOmbBattlefield.unity",
        "Assets/Scenes/WhompsFortress.unity",
        "Assets/Scenes/JollyRogerBay.unity",
        "Assets/Scenes/CoolCoolMountain.unity",
        "Assets/Scenes/BowserDarkWorld.unity",
        "Assets/Scenes/Test.unity"
    };

    private static readonly string[] ReturnPortalTargetScenes =
    {
        "Assets/Scenes/BobOmbBattlefield.unity",
        "Assets/Scenes/WhompsFortress.unity",
        "Assets/Scenes/JollyRogerBay.unity",
        "Assets/Scenes/CoolCoolMountain.unity",
        "Assets/Scenes/BowserDarkWorld.unity",
        "Assets/Scenes/Test.unity"
    };

    [MenuItem("Remake/Build Peach's Castle Hub Scene")]
    public static void BuildPeachCastleHubScene()
    {
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Materials/PeachCastle");
        EnsureFolder("Assets/Materials/PaintingPortals");

        var originalScene = SceneManager.GetActiveScene().path;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Shared Project & Custom URP Materials
        var grassMat = LoadOrCreateLitMat("Assets/Materials/RemakeStylizedPBR/M_StylizedGrass.mat", new Color(0.32f, 0.72f, 0.24f), 0.2f);
        var pathMat = LoadOrCreateLitMat("Assets/Materials/RemakeStylizedPBR/M_StylizedDirtPath.mat", new Color(0.76f, 0.65f, 0.44f), 0.18f);
        var stoneMat = LoadOrCreateLitMat("Assets/Materials/RemakeStylizedPBR/M_StylizedStone.mat", new Color(0.86f, 0.84f, 0.80f), 0.28f);
        var castleWallMat = LoadOrCreateLitMat("Assets/Materials/PeachCastle/M_CastleCreamWall.mat", new Color(0.95f, 0.90f, 0.80f), 0.22f);
        var castleRoofMat = LoadOrCreateLitMat("Assets/Materials/PeachCastle/M_CastleRoyalRoof.mat", new Color(0.84f, 0.19f, 0.22f), 0.36f);
        var carpetMat = LoadOrCreateLitMat("Assets/Materials/PeachCastle/M_CastleRedCarpet.mat", new Color(0.76f, 0.12f, 0.16f), 0.15f);
        var marbleMat = LoadOrCreateLitMat("Assets/Materials/PeachCastle/M_CastleMarbleFloor.mat", new Color(0.92f, 0.91f, 0.89f), 0.45f);
        var goldMat = LoadOrCreateLitMat("Assets/Materials/M_BigKey_Gold.mat", new Color(1.0f, 0.80f, 0.18f), 0.78f, metallic: 0.75f);
        var woodMat = LoadOrCreateLitMat("Assets/Materials/RemakeStylizedPBR/M_StylizedWood.mat", new Color(0.56f, 0.36f, 0.20f), 0.25f);

        var envRoot = new GameObject("PeachCastle_Environment").transform;
        var portalsRoot = new GameObject("CoursePortals").transform;
        var propsRoot = new GameObject("CourtyardProps").transform;

        BuildExteriorCourtyardAndMoat(envRoot, grassMat, pathMat, stoneMat, woodMat);
        BuildCastleStructureAndInterior(envRoot, castleWallMat, castleRoofMat, carpetMat, marbleMat, stoneMat, goldMat);
        BuildCourtyardDecorAndCollectibles(propsRoot, stoneMat, goldMat);
        BuildGalleryPaintingPortals(portalsRoot, goldMat);

        var (mario, gameplayVcam) = SpawnCorePlayerAndCameras();
        EnsureCoreManagersAndLighting(mario, gameplayVcam);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, HubScenePath);
        AssetDatabase.SaveAssets();
        RegisterBuildSettingsScenes();

        if (!string.IsNullOrEmpty(originalScene) && originalScene != HubScenePath && File.Exists(originalScene))
        {
            EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
        }

        Debug.Log($"[PeachCastleHubBuilder] Built & saved {HubScenePath} with 5 Course Painting Portals + Test Pipe!");
    }

    [MenuItem("Remake/Add Return-to-Castle Portals To All Courses")]
    public static void AddReturnPortalsToAllCourses()
    {
        EnsureFolder("Assets/Materials/PaintingPortals");
        var goldMat = LoadOrCreateLitMat("Assets/Materials/M_BigKey_Gold.mat", new Color(1.0f, 0.80f, 0.18f), 0.78f, metallic: 0.75f);
        var originalScene = SceneManager.GetActiveScene().path;
        var updatedCount = 0;

        foreach (var scenePath in ReturnPortalTargetScenes)
        {
            if (!File.Exists(scenePath))
            {
                Debug.LogWarning($"[PeachCastleHubBuilder] Skipping missing scene: {scenePath}");
                continue;
            }

            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var existing = GameObject.Find("ReturnToCastlePortal");
            if (existing != null)
            {
                Object.DestroyImmediate(existing);
            }

            var hero = Object.FindAnyObjectByType<MarioRemakeController>();
            var spawnPos = hero != null ? hero.transform.position : Vector3.zero;
            var rightDir = hero != null ? hero.transform.right : Vector3.right;
            var fwdDir = hero != null ? hero.transform.forward : Vector3.forward;

            var basePos = spawnPos - rightDir * 3.8f + fwdDir * 2.2f;
            Physics.SyncTransforms();
            if (Physics.Raycast(basePos + Vector3.up * 4f, Vector3.down, out var hit, 16f))
            {
                basePos.y = hit.point.y;
            }
            var portalPos = basePos + Vector3.up * 1.65f;
            var portalRot = hero != null
                ? Quaternion.LookRotation(rightDir - fwdDir * 0.45f, Vector3.up)
                : Quaternion.Euler(0f, 90f, 0f);

            var portalGo = CreatePaintingPortalObject(
                null,
                "ReturnToCastlePortal",
                portalPos,
                portalRot,
                "PeachCastleHub",
                "RETURN TO PEACH'S CASTLE",
                requiredStars: 0,
                isReturnPortal: true,
                themeColor: new Color(1.0f, 0.84f, 0.24f, 1f),
                accentColor: new Color(0.95f, 0.28f, 0.48f, 1f),
                goldMat: goldMat,
                matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_ReturnCastle.mat");

            portalGo.name = "ReturnToCastlePortal";
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            updatedCount++;
        }

        RegisterBuildSettingsScenes();

        if (!string.IsNullOrEmpty(originalScene) && File.Exists(originalScene))
        {
            EditorSceneManager.OpenScene(originalScene, OpenSceneMode.Single);
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[PeachCastleHubBuilder] Added/updated Return-to-Castle portals across {updatedCount} course scenes.");
    }

    public static void RegisterBuildSettingsScenes()
    {
        var list = new List<EditorBuildSettingsScene>();
        foreach (var path in AllBuildScenePaths)
        {
            if (File.Exists(path))
            {
                list.Add(new EditorBuildSettingsScene(path, true));
            }
        }
        EditorBuildSettings.scenes = list.ToArray();
    }

    private static void BuildExteriorCourtyardAndMoat(
        Transform parent,
        Material grassMat,
        Material pathMat,
        Material stoneMat,
        Material woodMat)
    {
        // Main grassy courtyard south of the moat (z = -46..-14)
        CreatePBCube(parent, "Courtyard_GrassGround", new Vector3(0f, -0.5f, -30f), new Vector3(58f, 1f, 32f), grassMat, addCollider: true);
        CreatePBCube(parent, "Courtyard_OuterGrassLeft", new Vector3(-25f, -0.5f, 10f), new Vector3(14f, 1f, 52f), grassMat, addCollider: true);
        CreatePBCube(parent, "Courtyard_OuterGrassRight", new Vector3(25f, -0.5f, 10f), new Vector3(14f, 1f, 52f), grassMat, addCollider: true);

        // Dirt path walkway leading toward bridge
        CreatePBCube(parent, "Courtyard_Path", new Vector3(0f, 0.02f, -26f), new Vector3(5.6f, 0.06f, 24f), pathMat, addCollider: false);

        // Moat basin floor & side walls (z = -14..-2)
        CreatePBCube(parent, "Moat_FloorBasin", new Vector3(0f, -4.5f, -8f), new Vector3(44f, 1f, 14f), stoneMat, addCollider: true);
        CreatePBCube(parent, "Moat_NorthBank", new Vector3(0f, -2.5f, -1.5f), new Vector3(44f, 5f, 1.2f), stoneMat, addCollider: true);
        CreatePBCube(parent, "Moat_SouthBank", new Vector3(0f, -2.5f, -14.2f), new Vector3(44f, 5f, 1.2f), stoneMat, addCollider: true);

        // Water Volume in moat using Water.prefab
        var waterPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Water.prefab");
        if (waterPrefab != null)
        {
            var waterGo = (GameObject)PrefabUtility.InstantiatePrefab(waterPrefab);
            waterGo.name = "CastleMoat_Water";
            waterGo.transform.SetParent(parent, false);
            waterGo.transform.position = new Vector3(0f, -2.25f, -8f);
            waterGo.transform.localScale = new Vector3(42f, 4f, 12.2f);
        }

        // Stone & timber bridge over the moat (z = -14.5..-1.5, flush at y = 0)
        CreatePBCube(parent, "Bridge_Deck", new Vector3(0f, -0.25f, -8f), new Vector3(6.6f, 0.5f, 13.5f), woodMat, addCollider: true);
        CreatePBCube(parent, "Bridge_CurbLeft", new Vector3(-3.1f, 0.25f, -8f), new Vector3(0.45f, 0.65f, 13.5f), stoneMat, addCollider: true);
        CreatePBCube(parent, "Bridge_CurbRight", new Vector3(3.1f, 0.25f, -8f), new Vector3(0.45f, 0.65f, 13.5f), stoneMat, addCollider: true);

        // Stone forecourt plaza between moat and castle doors (z = -2..6)
        CreatePBCube(parent, "Castle_ForecourtPlaza", new Vector3(0f, -0.5f, 2f), new Vector3(40f, 1f, 8f), stoneMat, addCollider: true);

        // Courtyard boundary stone parapet walls
        CreatePBCube(parent, "Boundary_South", new Vector3(0f, 1.2f, -46f), new Vector3(58f, 2.6f, 1.2f), stoneMat, addCollider: true);
        CreatePBCube(parent, "Boundary_West", new Vector3(-29f, 1.2f, -6f), new Vector3(1.2f, 2.6f, 82f), stoneMat, addCollider: true);
        CreatePBCube(parent, "Boundary_East", new Vector3(29f, 1.2f, -6f), new Vector3(1.2f, 2.6f, 82f), stoneMat, addCollider: true);
    }

    private static void BuildCastleStructureAndInterior(
        Transform parent,
        Material wallMat,
        Material roofMat,
        Material carpetMat,
        Material marbleMat,
        Material stoneMat,
        Material goldMat)
    {
        // Castle Front Facade (z = 6) with wide open central doorway (x = -3.6..3.6, y = 0..5.6)
        CreatePBCube(parent, "Facade_WallLeft", new Vector3(-11.3f, 5.75f, 6f), new Vector3(15.4f, 11.5f, 1.6f), wallMat, addCollider: true);
        CreatePBCube(parent, "Facade_WallRight", new Vector3(11.3f, 5.75f, 6f), new Vector3(15.4f, 11.5f, 1.6f), wallMat, addCollider: true);
        CreatePBCube(parent, "Facade_DoorHeader", new Vector3(0f, 8.6f, 6f), new Vector3(7.4f, 5.8f, 1.6f), wallMat, addCollider: true);

        // Entrance arch trim & Peach emblem medallion above main gate
        var archPb = ShapeGenerator.GenerateArch(PivotLocation.Center, 180f, 3.6f, 0.7f, 1.7f, 12, true, true, true, true, true);
        ConfigureProBuilderMesh(archPb, parent, "Facade_DoorArch", new Vector3(0f, 2.2f, 6f), Quaternion.identity, goldMat, addCollider: false);

        var emblemPb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 20, 1.45f, 0.28f, 0, 1);
        ConfigureProBuilderMesh(emblemPb, parent, "Facade_PeachEmblem", new Vector3(0f, 8.6f, 5.12f), Quaternion.Euler(90f, 0f, 0f), goldMat, addCollider: false);

        // Front Left & Right Turrets + Conical Red Roofs
        BuildCastleTurret(parent, "Turret_FrontLeft", new Vector3(-18.5f, 0f, 5.8f), 2.8f, 13.5f, wallMat, roofMat, goldMat);
        BuildCastleTurret(parent, "Turret_FrontRight", new Vector3(18.5f, 0f, 5.8f), 2.8f, 13.5f, wallMat, roofMat, goldMat);

        // Central Clock/Spire Tower
        BuildCastleTurret(parent, "Turret_CentralSpire", new Vector3(0f, 11.2f, 8.5f), 3.1f, 9.0f, wallMat, roofMat, goldMat);

        // Interior Foyer & Painting Gallery (x = -18..18, z = 6..39)
        CreatePBCube(parent, "Foyer_MarbleFloor", new Vector3(0f, -0.5f, 22.5f), new Vector3(36.4f, 1f, 34f), marbleMat, addCollider: true);
        CreatePBCube(parent, "Foyer_RedCarpet", new Vector3(0f, 0.015f, 20.5f), new Vector3(6.4f, 0.04f, 28f), carpetMat, addCollider: false);
        CreatePBCube(parent, "Foyer_WestWall", new Vector3(-18.2f, 5.75f, 22.5f), new Vector3(1.2f, 11.5f, 34.4f), wallMat, addCollider: true);
        CreatePBCube(parent, "Foyer_EastWall", new Vector3(18.2f, 5.75f, 22.5f), new Vector3(1.2f, 11.5f, 34.4f), wallMat, addCollider: true);
        CreatePBCube(parent, "Foyer_NorthBackWall", new Vector3(0f, 5.75f, 39.2f), new Vector3(36.4f, 11.5f, 1.2f), wallMat, addCollider: true);
        CreatePBCube(parent, "Foyer_Ceiling", new Vector3(0f, 12.0f, 22.5f), new Vector3(37.6f, 1f, 34.4f), wallMat, addCollider: true);

        // Interior decorative stone columns flanking the red carpet
        float[] columnZs = { 11f, 19.5f, 28f };
        foreach (var cz in columnZs)
        {
            var leftCol = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, 0.65f, 11.4f, 0, 1);
            ConfigureProBuilderMesh(leftCol, parent, $"Foyer_ColumnL_{cz:0}", new Vector3(-6.2f, 5.7f, cz), Quaternion.identity, marbleMat, addCollider: true);
            var rightCol = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 12, 0.65f, 11.4f, 0, 1);
            ConfigureProBuilderMesh(rightCol, parent, $"Foyer_ColumnR_{cz:0}", new Vector3(6.2f, 5.7f, cz), Quaternion.identity, marbleMat, addCollider: true);
        }

        // Raised North Bowser Gallery Platform + Grand ProBuilder Staircase (z = 31.5..39)
        var stairsPb = ShapeGenerator.GenerateStair(PivotLocation.Center, new Vector3(8.4f, 1.1f, 4.2f), 6, true);
        ConfigureProBuilderMesh(stairsPb, parent, "Foyer_GrandStairs", new Vector3(0f, 0.55f, 33.4f), Quaternion.identity, stoneMat, addCollider: true);
        CreatePBCube(parent, "Foyer_NorthDais", new Vector3(0f, 0.55f, 37.0f), new Vector3(14f, 1.1f, 4.2f), marbleMat, addCollider: true);

        // Interior warm chandelier Point Lights
        CreateInteriorLight(parent, "Foyer_LightFront", new Vector3(0f, 9.2f, 14f), 28f, 3.2f);
        CreateInteriorLight(parent, "Foyer_LightMid", new Vector3(0f, 9.2f, 24f), 28f, 3.2f);
        CreateInteriorLight(parent, "Foyer_LightNorth", new Vector3(0f, 9.4f, 34f), 26f, 3.0f);
    }

    private static void BuildCastleTurret(
        Transform parent,
        string name,
        Vector3 basePos,
        float radius,
        float height,
        Material wallMat,
        Material roofMat,
        Material goldMat)
    {
        var towerPb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 16, radius, height, 0, 1);
        ConfigureProBuilderMesh(towerPb, parent, name + "_Tower", basePos + Vector3.up * (height * 0.5f), Quaternion.identity, wallMat, addCollider: true);

        var conePb = ShapeGenerator.GenerateCone(PivotLocation.Center, radius * 1.18f, height * 0.42f, 16);
        ConfigureProBuilderMesh(conePb, parent, name + "_Roof", basePos + Vector3.up * (height + height * 0.21f), Quaternion.identity, roofMat, addCollider: false);

        var finialPb = ShapeGenerator.GenerateCylinder(PivotLocation.Center, 10, 0.22f, 1.1f, 0, 1);
        ConfigureProBuilderMesh(finialPb, parent, name + "_Finial", basePos + Vector3.up * (height + height * 0.42f + 0.4f), Quaternion.identity, goldMat, addCollider: false);
    }

    private static void BuildCourtyardDecorAndCollectibles(Transform parent, Material stoneMat, Material goldMat)
    {
        // Courtyard Trees
        var treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/CastleTree.prefab");
        if (treePrefab != null)
        {
            Vector3[] treePositions =
            {
                new(-14.5f, 0f, -38f),
                new(14.5f, 0f, -38f),
                new(-18.5f, 0f, -22f),
                new(18.5f, 0f, -22f),
                new(-10.5f, 0f, -39f),
                new(10.5f, 0f, -39f)
            };
            for (var i = 0; i < treePositions.Length; i++)
            {
                var tree = (GameObject)PrefabUtility.InstantiatePrefab(treePrefab);
                tree.name = $"CastleTree_{i + 1}";
                tree.transform.SetParent(parent, false);
                tree.transform.position = treePositions[i];
            }
        }

        // Courtyard Flowers
        var flowerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/FlowerPatch.prefab");
        if (flowerPrefab != null)
        {
            Vector3[] flowerPositions =
            {
                new(-7.5f, 0.05f, -31f),
                new(7.5f, 0.05f, -31f),
                new(-7.5f, 0.05f, -21f),
                new(7.5f, 0.05f, -21f),
                new(-12f, 0.05f, 1.5f),
                new(12f, 0.05f, 1.5f)
            };
            for (var i = 0; i < flowerPositions.Length; i++)
            {
                var patch = (GameObject)PrefabUtility.InstantiatePrefab(flowerPrefab);
                patch.name = $"FlowerPatch_{i + 1}";
                patch.transform.SetParent(parent, false);
                patch.transform.position = flowerPositions[i];
            }
        }

        // Guiding Yellow Coins across Courtyard & Bridge
        var coinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Collectibles/YellowCoin.prefab");
        if (coinPrefab != null)
        {
            Vector3[] coinPositions =
            {
                new(0f, 0.7f, -27f),
                new(0f, 0.7f, -23f),
                new(0f, 0.7f, -19f),
                new(0f, 0.7f, -11f),
                new(0f, 0.7f, -8f),
                new(0f, 0.7f, -5f),
                new(0f, 0.7f, 10f),
                new(0f, 0.7f, 15f)
            };
            for (var i = 0; i < coinPositions.Length; i++)
            {
                var c = (GameObject)PrefabUtility.InstantiatePrefab(coinPrefab);
                c.name = $"HubYellowCoin_{i + 1}";
                c.transform.SetParent(parent, false);
                c.transform.position = coinPositions[i];
            }
        }

        // Courtyard Secret Pedestal + Castle Secret Star (unlockable immediately!)
        CreatePBCube(parent, "Courtyard_StarPedestal", new Vector3(13.5f, 0.55f, -22f), new Vector3(2.8f, 1.1f, 2.8f), stoneMat, addCollider: true);
        var starPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Collectibles/PowerStar.prefab");
        if (starPrefab != null)
        {
            var starGo = (GameObject)PrefabUtility.InstantiatePrefab(starPrefab);
            starGo.name = "CastleCourtyard_SecretStar1";
            starGo.transform.SetParent(parent, false);
            starGo.transform.position = new Vector3(13.5f, 2.15f, -22f);
            var col = starGo.GetComponent<StarCoinCollectible>();
            if (col != null)
            {
                col.kind = CollectibleKind.PowerStar;
                col.missionActIndex = 1;
                col.starTitle = "CASTLE COURTYARD SECRET STAR";
            }
        }

        // Test Playground Warp Pipe in Courtyard Left
        var pipePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Architecture/BowserWarpPipe.prefab");
        var pipePos = new Vector3(-11.5f, 0.0f, -24f);
        if (pipePrefab != null)
        {
            var pipe = (GameObject)PrefabUtility.InstantiatePrefab(pipePrefab);
            pipe.name = "WarpPipe_TestGym";
            pipe.transform.SetParent(parent, false);
            pipe.transform.position = pipePos;
            var box = pipe.GetComponent<BoxCollider>();
            if (box == null) box = pipe.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.3f, 0f);
            box.size = new Vector3(2.4f, 2.6f, 2.4f);

            var portal = pipe.GetComponent<CoursePaintingPortal>();
            if (portal == null) portal = pipe.AddComponent<CoursePaintingPortal>();
            portal.targetSceneName = "Test";
            portal.courseTitle = "TEST PLAYGROUND GYM";
            portal.requiredStars = 0;
            portal.portalThemeColor = new Color(0.24f, 0.85f, 0.42f, 1f);
            portal.labelLocalOffset = new Vector3(0f, 3.0f, 0f);
        }
        else
        {
            CreatePaintingPortalObject(
                parent,
                "Portal_TestGym",
                pipePos + Vector3.up * 1.6f,
                Quaternion.Euler(0f, 45f, 0f),
                "Test",
                "TEST PLAYGROUND GYM",
                requiredStars: 0,
                isReturnPortal: false,
                themeColor: new Color(0.24f, 0.85f, 0.42f, 1f),
                accentColor: new Color(1.0f, 0.86f, 0.22f, 1f),
                goldMat: goldMat,
                matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_TestGym.mat");
        }
    }

    private static void BuildGalleryPaintingPortals(Transform parent, Material goldMat)
    {
        CreatePaintingPortalObject(
            parent,
            "PaintingPortal_BobOmbBattlefield",
            new Vector3(-17.1f, 2.15f, 14f),
            Quaternion.Euler(0f, 90f, 0f),
            "BobOmbBattlefield",
            "COURSE 1: BOB-OMB BATTLEFIELD",
            requiredStars: 0,
            isReturnPortal: false,
            themeColor: new Color(0.24f, 0.78f, 0.34f, 1f),
            accentColor: new Color(0.98f, 0.82f, 0.22f, 1f),
            goldMat: goldMat,
            matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_BobOmb.mat");

        CreatePaintingPortalObject(
            parent,
            "PaintingPortal_WhompsFortress",
            new Vector3(-17.1f, 2.15f, 26f),
            Quaternion.Euler(0f, 90f, 0f),
            "WhompsFortress",
            "COURSE 2: WHOMP'S FORTRESS",
            requiredStars: 1,
            isReturnPortal: false,
            themeColor: new Color(0.46f, 0.62f, 0.88f, 1f),
            accentColor: new Color(0.92f, 0.78f, 0.42f, 1f),
            goldMat: goldMat,
            matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_Whomps.mat");

        CreatePaintingPortalObject(
            parent,
            "PaintingPortal_JollyRogerBay",
            new Vector3(17.1f, 2.15f, 14f),
            Quaternion.Euler(0f, -90f, 0f),
            "JollyRogerBay",
            "COURSE 3: JOLLY ROGER BAY",
            requiredStars: 3,
            isReturnPortal: false,
            themeColor: new Color(0.12f, 0.66f, 0.90f, 1f),
            accentColor: new Color(0.96f, 0.86f, 0.38f, 1f),
            goldMat: goldMat,
            matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_JollyRoger.mat");

        CreatePaintingPortalObject(
            parent,
            "PaintingPortal_CoolCoolMountain",
            new Vector3(17.1f, 2.15f, 26f),
            Quaternion.Euler(0f, -90f, 0f),
            "CoolCoolMountain",
            "COURSE 4: COOL, COOL MOUNTAIN",
            requiredStars: 3,
            isReturnPortal: false,
            themeColor: new Color(0.66f, 0.88f, 1.0f, 1f),
            accentColor: new Color(0.28f, 0.56f, 0.95f, 1f),
            goldMat: goldMat,
            matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_CoolCool.mat");

        CreatePaintingPortalObject(
            parent,
            "PaintingPortal_BowserDarkWorld",
            new Vector3(0f, 3.25f, 38.0f),
            Quaternion.Euler(0f, 180f, 0f),
            "BowserDarkWorld",
            "BOWSER IN THE DARK WORLD",
            requiredStars: 8,
            isReturnPortal: false,
            themeColor: new Color(0.84f, 0.18f, 0.36f, 1f),
            accentColor: new Color(0.98f, 0.54f, 0.14f, 1f),
            goldMat: goldMat,
            matAssetPath: "Assets/Materials/PaintingPortals/M_Painting_BowserDark.mat");
    }

    private static GameObject CreatePaintingPortalObject(
        Transform parent,
        string objectName,
        Vector3 position,
        Quaternion rotation,
        string targetSceneName,
        string courseTitle,
        int requiredStars,
        bool isReturnPortal,
        Color themeColor,
        Color accentColor,
        Material goldMat,
        string matAssetPath)
    {
        var root = new GameObject(objectName);
        if (parent != null) root.transform.SetParent(parent, false);
        root.transform.position = position;
        root.transform.rotation = rotation;

        // Trigger zone extending slightly outward in +Z so walking or jumping in activates smoothly
        var trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 0f, 0.45f);
        trigger.size = new Vector3(3.8f, 3.2f, 1.25f);

        var portal = root.AddComponent<CoursePaintingPortal>();
        portal.targetSceneName = targetSceneName;
        portal.courseTitle = courseTitle;
        portal.requiredStars = requiredStars;
        portal.isReturnToCastlePortal = isReturnPortal;
        portal.portalThemeColor = themeColor;
        portal.labelLocalOffset = new Vector3(0f, 2.35f, 0.25f);

        // Painting canvas mesh using Mario64Remake/PaintingPortalWave shader
        var paintingMat = CreateOrUpdatePaintingPortalMat(matAssetPath, themeColor, accentColor);
        var canvasPb = ShapeGenerator.GenerateCube(PivotLocation.Center, new Vector3(3.6f, 2.7f, 0.16f));
        var canvasGo = ConfigureProBuilderMesh(canvasPb, root.transform, "PaintingCanvas", Vector3.zero, Quaternion.identity, paintingMat, addCollider: false);
        portal.paintingCanvasRenderer = canvasGo.GetComponent<MeshRenderer>();

        // Ornate Golden Frame Top/Bottom/Left/Right Bars
        CreatePBCube(root.transform, "Frame_Top", new Vector3(0f, 1.47f, 0.04f), new Vector3(4.06f, 0.26f, 0.34f), goldMat, addCollider: false);
        CreatePBCube(root.transform, "Frame_Bottom", new Vector3(0f, -1.47f, 0.04f), new Vector3(4.06f, 0.26f, 0.34f), goldMat, addCollider: false);
        CreatePBCube(root.transform, "Frame_Left", new Vector3(-1.91f, 0f, 0.04f), new Vector3(0.24f, 2.72f, 0.34f), goldMat, addCollider: false);
        CreatePBCube(root.transform, "Frame_Right", new Vector3(1.91f, 0f, 0.04f), new Vector3(0.24f, 2.72f, 0.34f), goldMat, addCollider: false);

        return root;
    }

    private static (MarioRemakeController mario, GameObject vcamGo) SpawnCorePlayerAndCameras()
    {
        MarioRemakeController marioCtrl = null;
        var marioPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Characters/Mario.prefab");
        if (marioPrefab != null)
        {
            var marioGo = (GameObject)PrefabUtility.InstantiatePrefab(marioPrefab);
            marioGo.name = "Mario";
            marioGo.transform.position = new Vector3(0f, 0.5f, -32f);
            marioGo.transform.rotation = Quaternion.identity;
            marioCtrl = marioGo.GetComponent<MarioRemakeController>();
        }

        // Main Camera with URP Camera Data & CinemachineBrain
        var mainCamGo = new GameObject("Main Camera");
        mainCamGo.tag = "MainCamera";
        mainCamGo.transform.position = new Vector3(0f, 3.6f, -38.5f);
        mainCamGo.transform.LookAt(new Vector3(0f, 1.8f, -32f));
        var cam = mainCamGo.AddComponent<Camera>();
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 450f;
        mainCamGo.AddComponent<AudioListener>();
        mainCamGo.AddComponent<UniversalAdditionalCameraData>();
        var brain = mainCamGo.AddComponent<CinemachineBrain>();
        brain.DefaultBlend.Style = CinemachineBlendDefinition.Styles.EaseInOut;
        brain.DefaultBlend.Time = 0.45f;

        // Gameplay Cinemachine Camera (name must contain "Gameplay" for MarioRemakeController)
        var vcamGo = new GameObject("GameplayVCam");
        vcamGo.transform.position = new Vector3(0f, 3.6f, -38.5f);
        var vcam = vcamGo.AddComponent<CinemachineCamera>();
        if (marioCtrl != null)
        {
            vcam.Target.TrackingTarget = marioCtrl.cameraYawProxy != null
                ? marioCtrl.cameraYawProxy
                : marioCtrl.transform;
        }
        var lens = vcam.Lens;
        lens.FieldOfView = 54f;
        vcam.Lens = lens;

        var follow = vcamGo.AddComponent<CinemachineThirdPersonFollow>();
        follow.CameraDistance = 7.2f;
        follow.Damping = new Vector3(0.03f, 0.08f, 0.03f);
        follow.ShoulderOffset = new Vector3(0f, 0.44f, 0f);
        follow.VerticalArmLength = 0.22f;
        follow.CameraSide = 0.5f;

        return (marioCtrl, vcamGo);
    }

    private static void EnsureCoreManagersAndLighting(MarioRemakeController mario, GameObject gameplayVcam)
    {
        var saveGo = new GameObject("SaveManager");
        saveGo.AddComponent<SaveManager>();

        var audioGo = new GameObject("GameAudio");
        audioGo.AddComponent<GameAudio>();

        var vfxGo = new GameObject("VFXPool");
        vfxGo.AddComponent<VFXPool>();

        var gmGo = new GameObject("GameManager");
        var gm = gmGo.AddComponent<GameManager>();
        gm.player = mario;
        gm.gameplayVCamObject = gameplayVcam;
        gm.targetStarsToWin = 8;
        gm.currentState = GameState.Playing;

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();

        // Sun Light
        var sunGo = new GameObject("Directional Light");
        var sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.0f, 0.96f, 0.88f);
        sun.intensity = 1.45f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.80f;
        sunGo.transform.rotation = Quaternion.Euler(46f, -28f, 0f);
        sunGo.AddComponent<UniversalAdditionalLightData>();

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.56f, 0.84f, 1.0f);
        RenderSettings.ambientEquatorColor = new Color(0.88f, 0.92f, 0.84f);
        RenderSettings.ambientGroundColor = new Color(0.32f, 0.46f, 0.28f);

        // Global Post-Processing Volume
        var volGo = new GameObject("RemakeGlobalVolume");
        var vol = volGo.AddComponent<Volume>();
        vol.isGlobal = true;
        vol.priority = 10f;
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/RemakeVolumeProfile.asset");
        if (profile == null)
            profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DefaultVolumeProfile.asset");
        if (profile != null) vol.sharedProfile = profile;
    }

    private static GameObject CreatePBCube(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat, bool addCollider)
    {
        var pb = ShapeGenerator.GenerateCube(PivotLocation.Center, size);
        return ConfigureProBuilderMesh(pb, parent, name, localPos, Quaternion.identity, mat, addCollider);
    }

    private static GameObject ConfigureProBuilderMesh(
        ProBuilderMesh pb,
        Transform parent,
        string name,
        Vector3 localPos,
        Quaternion localRot,
        Material mat,
        bool addCollider)
    {
        pb.gameObject.name = name;
        pb.transform.SetParent(parent, false);
        pb.transform.localPosition = localPos;
        pb.transform.localRotation = localRot;
        var mr = pb.GetComponent<MeshRenderer>();
        if (mr != null && mat != null) mr.sharedMaterial = mat;
        pb.ToMesh();
        pb.Refresh();
        if (addCollider && pb.GetComponent<MeshCollider>() == null)
        {
            pb.gameObject.AddComponent<MeshCollider>();
        }
        return pb.gameObject;
    }

    private static void CreateInteriorLight(Transform parent, string name, Vector3 pos, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1.0f, 0.94f, 0.80f);
        l.range = range;
        l.intensity = intensity;
        l.shadows = LightShadows.None;
    }

    private static Material CreateOrUpdatePaintingPortalMat(string path, Color baseColor, Color accentColor)
    {
        var shader = Shader.Find("Mario64Remake/PaintingPortalWave");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (shader != null && mat.shader != shader)
        {
            mat.shader = shader;
        }

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_AccentColor")) mat.SetColor("_AccentColor", accentColor);
        if (mat.HasProperty("_FrameTrimColor")) mat.SetColor("_FrameTrimColor", new Color(1f, 0.94f, 0.62f, 1f));
        if (mat.HasProperty("_RippleAmplitude")) mat.SetFloat("_RippleAmplitude", 0.014f);
        if (mat.HasProperty("_RippleSpeed")) mat.SetFloat("_RippleSpeed", 4.0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material LoadOrCreateLitMat(string path, Color color, float smoothness, float metallic = 0f)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        mat = new Material(shader);
        mat.color = color;
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        var parts = folderPath.Split('/');
        var current = parts[0];
        for (var i = 1; i < parts.Length; i++)
        {
            var next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

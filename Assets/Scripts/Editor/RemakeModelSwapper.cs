using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class RemakeModelSwapper
{
    [MenuItem("Mario64/Remake/Swap Goomba, BobOmb & KingBobOmb to Blender Remake FBX")]
    public static void SwapEnemyModels()
    {
        AssetDatabase.Refresh();

        SwapPrefabVisual(
            "Assets/Prefabs/Enemies/Goomba.prefab",
            "Assets/Models/Enemies/Goomba_Remake.fbx",
            targetHeight: 1.0f);

        SwapPrefabVisual(
            "Assets/Prefabs/Enemies/BobOmb.prefab",
            "Assets/Models/Enemies/BobOmb_Remake.fbx",
            targetHeight: 1.05f,
            wireBobOmbPatrol: true);

        SwapPrefabVisual(
            "Assets/Prefabs/Enemies/KingBobOmb.prefab",
            "Assets/Models/Enemies/KingBobOmb_Remake.fbx",
            targetHeight: 2.35f,
            wireKingBobOmb: true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RemakeModelSwapper] Successfully swapped Goomba, BobOmb, and KingBobOmb to Blender Remake FBX models.");
    }

    [MenuItem("Mario64/Remake/Create & Attach Shared RoyalCrown to KingBobOmb & WhompKing")]
    public static void AttachSharedRoyalCrown()
    {
        AssetDatabase.Refresh();

        const string crownFbxPath = "Assets/Models/Props/RoyalCrown_SM64DS.fbx";
        const string crownPrefabPath = "Assets/Prefabs/Props/RoyalCrown.prefab";

        var crownFbx = AssetDatabase.LoadAssetAtPath<GameObject>(crownFbxPath);
        if (crownFbx == null)
        {
            Debug.LogError($"[RemakeModelSwapper] Missing RoyalCrown FBX at '{crownFbxPath}'.");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/Props"))
        {
            AssetDatabase.CreateFolder("Assets/Prefabs", "Props");
        }

        // Create/update the shared RoyalCrown prefab
        var tempCrown = (GameObject)PrefabUtility.InstantiatePrefab(crownFbx);
        tempCrown.name = "RoyalCrown";
        var crownPrefab = PrefabUtility.SaveAsPrefabAsset(tempCrown, crownPrefabPath);
        Object.DestroyImmediate(tempCrown);

        if (crownPrefab == null)
        {
            Debug.LogError($"[RemakeModelSwapper] Failed to create shared prefab at '{crownPrefabPath}'.");
            return;
        }

        // 1. Attach shared RoyalCrown to KingBobOmb.prefab
        using (var scope = new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/Enemies/KingBobOmb.prefab"))
        {
            var root = scope.prefabContentsRoot;
            var oldCrown = root.transform.Find("Crown");
            if (oldCrown != null) Object.DestroyImmediate(oldCrown.gameObject);

            var crownInst = (GameObject)PrefabUtility.InstantiatePrefab(crownPrefab, root.transform);
            crownInst.name = "Crown";
            crownInst.transform.localPosition = new Vector3(0f, 1.82f, 0f);
            crownInst.transform.localRotation = Quaternion.identity;
            crownInst.transform.localScale = Vector3.one * 0.92f;
        }

        // 2. Attach shared RoyalCrown to WhompKing.prefab
        using (var scope = new PrefabUtility.EditPrefabContentsScope("Assets/Prefabs/Enemies/WhompKing.prefab"))
        {
            var root = scope.prefabContentsRoot;
            var visualModel = root.transform.Find("VisualModel");
            var parentTr = visualModel != null ? visualModel : root.transform;

            var oldCrown = parentTr.Find("KingCrown") ?? parentTr.Find("Crown");
            if (oldCrown != null) Object.DestroyImmediate(oldCrown.gameObject);

            var crownInst = (GameObject)PrefabUtility.InstantiatePrefab(crownPrefab, parentTr);
            crownInst.name = "KingCrown";
            crownInst.transform.localPosition = new Vector3(0f, 3.83f, 0f);
            crownInst.transform.localRotation = Quaternion.identity;
            crownInst.transform.localScale = Vector3.one * 1.25f;
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RemakeModelSwapper] Created RoyalCrown.prefab and attached it to both KingBobOmb.prefab and WhompKing.prefab.");
    }

    private static void SwapPrefabVisual(
        string prefabPath,
        string fbxPath,
        float targetHeight,
        bool wireBobOmbPatrol = false,
        bool wireKingBobOmb = false)
    {
        var fbxAsset = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
        if (fbxAsset == null)
        {
            Debug.LogError($"[RemakeModelSwapper] Missing FBX at '{fbxPath}'.");
            return;
        }

        using var scope = new PrefabUtility.EditPrefabContentsScope(prefabPath);
        var root = scope.prefabContentsRoot;
        if (root == null) return;

        // Remove old primitive visual children while preserving any non-visual gameplay/audio/collider children
        var toDelete = new List<GameObject>();
        for (var i = 0; i < root.transform.childCount; i++)
        {
            var child = root.transform.GetChild(i).gameObject;
            var hasCollider = child.GetComponentInChildren<Collider>(true) != null;
            var hasAudio = child.GetComponentInChildren<AudioSource>(true) != null;
            var hasParticle = child.GetComponentInChildren<ParticleSystem>(true) != null;
            if (!hasCollider && !hasAudio && !hasParticle)
            {
                toDelete.Add(child);
            }
        }
        foreach (var go in toDelete)
        {
            Object.DestroyImmediate(go);
        }

        // Instantiate FBX temporarily to compute bounds & promote its direct children onto root
        var tempInstance = (GameObject)PrefabUtility.InstantiatePrefab(fbxAsset, root.transform);
        tempInstance.transform.localPosition = Vector3.zero;
        tempInstance.transform.localRotation = Quaternion.identity;
        tempInstance.transform.localScale = Vector3.one;

        var bounds = ComputeRendererBounds(tempInstance);
        var uniformScale = bounds.size.y > 0.05f ? targetHeight / bounds.size.y : 1f;
        tempInstance.transform.localScale = Vector3.one * uniformScale;

        // Recompute bounds after scaling to ground min.y at y = 0
        bounds = ComputeRendererBounds(tempInstance);
        var yOffset = -bounds.min.y;
        tempInstance.transform.localPosition = new Vector3(0f, yOffset, 0f);

        // Unpack & promote direct children of tempInstance to root so transform.Find("Arm_L"/"WindupKey"/"Foot_L") works directly
        PrefabUtility.UnpackPrefabInstance(tempInstance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        var promoted = new List<Transform>();
        for (var i = 0; i < tempInstance.transform.childCount; i++)
        {
            promoted.Add(tempInstance.transform.GetChild(i));
        }
        foreach (var ch in promoted)
        {
            ch.SetParent(root.transform, worldPositionStays: true);
        }
        Object.DestroyImmediate(tempInstance);

        if (wireBobOmbPatrol)
        {
            var patrol = root.GetComponent<BobOmbPatrol>();
            if (patrol != null)
            {
                var so = new SerializedObject(patrol);
                so.FindProperty("windupKey").objectReferenceValue = root.transform.Find("WindupKey");
                so.FindProperty("leftFoot").objectReferenceValue = root.transform.Find("Foot_L");
                so.FindProperty("rightFoot").objectReferenceValue = root.transform.Find("Foot_R");
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        if (wireKingBobOmb)
        {
            var bf = root.GetComponent<BobOmbBattlefieldController>();
            if (bf != null)
            {
                var so = new SerializedObject(bf);
                so.FindProperty("secondaryVisualTarget").objectReferenceValue = root.transform.Find("WindupKey");
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    private static Bounds ComputeRendererBounds(GameObject go)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = renderers[0].bounds;
        for (var i = 1; i < renderers.Length; i++)
        {
            b.Encapsulate(renderers[i].bounds);
        }
        return b;
    }
}


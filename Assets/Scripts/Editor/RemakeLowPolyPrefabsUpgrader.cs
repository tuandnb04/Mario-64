using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.ProBuilder;

public static class RemakeLowPolyPrefabsUpgrader
{
    private const string DonePrefKey = "Remake_LowPolyPrefabsUpgraded_v5";
    private const string BowserDonePrefKey = "Remake_BowserVisualPolish_v2";
    private const string KingBobOmbDonePrefKey = "Remake_KingBobOmbVisualPolish_v4";
    private const string BobOmbDonePrefKey = "Remake_BobOmbVisualPolish_v1";

    private static readonly string[] TargetPrefabPaths =
    {
        "Assets/Prefabs/Environment/CoralBoulder.prefab",
        "Assets/Prefabs/Environment/CastleTree.prefab",
        "Assets/Prefabs/Environment/SnowyPineTree.prefab",
        "Assets/Prefabs/Environment/FlowerPatch.prefab",
        "Assets/Prefabs/Environment/FlowerUnit.prefab",
        "Assets/Prefabs/Environment/ArenaPerimeterOrb.prefab",
        "Assets/Prefabs/Environment/SummitFlagpole.prefab",
        "Assets/Prefabs/Collectibles/PowerStar.prefab",
        "Assets/Prefabs/Collectibles/YellowCoin.prefab",
        "Assets/Prefabs/Collectibles/RedCoin.prefab",
        "Assets/Prefabs/Mechanics/RollingIronBall.prefab",
        "Assets/Prefabs/Mechanics/Snowball.prefab",
        "Assets/Prefabs/Mechanics/SnowmanBody.prefab",
        "Assets/Prefabs/Mechanics/SnowmanHead.prefab",
        "Assets/Prefabs/Mechanics/CarriableBomb.prefab",
        "Assets/Prefabs/Mechanics/SnapClamShell.prefab",
        "Assets/Prefabs/Mechanics/ClimbablePole.prefab",
        "Assets/Prefabs/Mechanics/FlameJetNozzle.prefab",
        "Assets/Prefabs/Mechanics/IceTeleporterNode.prefab",
        "Assets/Prefabs/Mechanics/JetStreamGeyser.prefab",
        "Assets/Prefabs/Mechanics/SkyCannon.prefab",
        "Assets/Prefabs/Enemies/BobOmb.prefab",
        "Assets/Prefabs/Enemies/BowserBoss.prefab",
        "Assets/Prefabs/Enemies/Goomba.prefab",
        "Assets/Prefabs/Enemies/KoopaTheQuick.prefab",
        "Assets/Prefabs/Enemies/MrBlizzard.prefab",
        "Assets/Prefabs/Enemies/Spindrift.prefab",
        "Assets/Prefabs/Enemies/HootOwl.prefab",
        "Assets/Prefabs/Enemies/UnagiEel.prefab",
        "Assets/Prefabs/Mechanics/BabyPenguin.prefab",
        "Assets/Prefabs/Characters/PenguinRacer.prefab",
        "Assets/Prefabs/Characters/PenguinMother.prefab"
    };

    [InitializeOnLoadMethod]
    private static void RunOnceOnLoad()
    {
        if (!EditorPrefs.GetBool(BowserDonePrefKey, false))
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(BowserDonePrefKey, false)) return;
                PolishBowserPrefab();
                EditorPrefs.SetBool(BowserDonePrefKey, true);
            };
        }

        if (!EditorPrefs.GetBool(KingBobOmbDonePrefKey, false))
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(KingBobOmbDonePrefKey, false)) return;
                PolishKingBobOmbPrefab();
                EditorPrefs.SetBool(KingBobOmbDonePrefKey, true);
            };
        }

        if (!EditorPrefs.GetBool(BobOmbDonePrefKey, false))
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorPrefs.GetBool(BobOmbDonePrefKey, false)) return;
                PolishBobOmbPrefab();
                EditorPrefs.SetBool(BobOmbDonePrefKey, true);
            };
        }

        if (EditorPrefs.GetBool(DonePrefKey, false)) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(DonePrefKey, false)) return;
            UpgradeAllLowPolyPrefabs();
            EditorPrefs.SetBool(DonePrefKey, true);
        };
    }

    [MenuItem("Remake/Polish Bowser Visuals")]
    public static void PolishBowserPrefab()
    {
        const string bowserPath = "Assets/Prefabs/Enemies/BowserBoss.prefab";
        if (!File.Exists(bowserPath)) return;
        var root = PrefabUtility.LoadPrefabContents(bowserPath);
        try
        {
            ApplyBowserVisualPolish(root);
            PrefabUtility.SaveAsPrefabAsset(root, bowserPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[LowPolyUpgrader] Polished BowserBoss.prefab visuals.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    [MenuItem("Remake/Upgrade Low-Poly Prefabs")]
    public static void UpgradeAllLowPolyPrefabs()
    {
        var upgradedPrefabs = 0;
        var upgradedMeshes = 0;

        foreach (var path in TargetPrefabPaths)
        {
            if (!File.Exists(path)) continue;

            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var pbMeshes = root.GetComponentsInChildren<ProBuilderMesh>(true);
                var modified = false;

                foreach (var pb in pbMeshes)
                {
                    if (pb == null || pb.faceCount == 0) continue;

                    // Skip crisp 6-face cubes/boxes that have no smooth faces
                    var hasSmoothFaces = false;
                    foreach (var f in pb.faces)
                    {
                        if (f.smoothingGroup > 0)
                        {
                            hasSmoothFaces = true;
                            break;
                        }
                    }
                    if (!hasSmoothFaces && pb.faceCount <= 12) continue;

                    // Only subdivide curved/low-poly meshes (< 140 faces)
                    if (pb.faceCount >= 140) continue;

                    var pbModified = SubdivideSmooth(pb);

                    if (pbModified)
                    {
                        modified = true;
                        pb.ToMesh();
                        pb.Refresh();
                        upgradedMeshes++;
                    }
                }

                if (path.EndsWith("BowserBoss.prefab"))
                {
                    if (ApplyBowserVisualPolish(root)) modified = true;
                }

                if (modified)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    upgradedPrefabs++;
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[LowPolyUpgrader] Skipped {path}: {ex.Message}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[LowPolyUpgrader] Upgraded {upgradedMeshes} ProBuilder meshes across {upgradedPrefabs} prefabs.");
    }

    private static bool SubdivideSmooth(ProBuilderMesh pb)
    {
        var srcPositions = pb.positions;
        var srcFaces = pb.faces;
        if (srcPositions == null || srcPositions.Count < 3 || srcFaces == null || srcFaces.Count == 0)
            return false;

        // Weld coincident vertices by quantized 3D position so UV/cap seams NEVER tear open!
        static (int, int, int) PosKey(Vector3 p) =>
            (Mathf.RoundToInt(p.x * 10000f), Mathf.RoundToInt(p.y * 10000f), Mathf.RoundToInt(p.z * 10000f));

        var posToCanon = new Dictionary<(int, int, int), int>(srcPositions.Count);
        var canonToIdx = new int[srcPositions.Count];
        var canonPositions = new List<Vector3>(srcPositions.Count);

        for (var i = 0; i < srcPositions.Count; i++)
        {
            var pk = PosKey(srcPositions[i]);
            if (!posToCanon.TryGetValue(pk, out var cid))
            {
                cid = canonPositions.Count;
                posToCanon[pk] = cid;
                canonPositions.Add(srcPositions[i]);
            }
            canonToIdx[i] = cid;
        }

        // Build adjacency & opposite vertices using canonical spatial IDs
        var neighbors = new Dictionary<int, HashSet<int>>();
        var edgeOpp = new Dictionary<long, List<int>>();

        static long EdgeKey(int a, int b) =>
            a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

        void AddEdge(int ca, int cb, int copp)
        {
            if (ca == cb) return;
            if (!neighbors.TryGetValue(ca, out var na)) { na = new HashSet<int>(); neighbors[ca] = na; }
            na.Add(cb);
            if (!neighbors.TryGetValue(cb, out var nb)) { nb = new HashSet<int>(); neighbors[cb] = nb; }
            nb.Add(ca);

            var key = EdgeKey(ca, cb);
            if (!edgeOpp.TryGetValue(key, out var list)) { list = new List<int>(2); edgeOpp[key] = list; }
            list.Add(copp);
        }

        var tris = new List<(int a, int b, int c, int submesh, int smoothGroup)>();
        foreach (var face in srcFaces)
        {
            var idx = face.indexes;
            for (var i = 0; i + 2 < idx.Count; i += 3)
            {
                int a = idx[i], b = idx[i + 1], c = idx[i + 2];
                tris.Add((a, b, c, face.submeshIndex, face.smoothingGroup));
                AddEdge(canonToIdx[a], canonToIdx[b], canonToIdx[c]);
                AddEdge(canonToIdx[b], canonToIdx[c], canonToIdx[a]);
                AddEdge(canonToIdx[c], canonToIdx[a], canonToIdx[b]);
            }
        }

        if (tris.Count == 0) return false;

        // Compute smoothed canonical positions (all coincident vertices move identically -> zero seams!)
        var smoothedCanon = new Vector3[canonPositions.Count];
        for (var c = 0; c < canonPositions.Count; c++)
        {
            var p = canonPositions[c];
            if (neighbors.TryGetValue(c, out var nSet) && nSet.Count >= 3)
            {
                var avg = Vector3.zero;
                foreach (var n in nSet) avg += canonPositions[n];
                avg /= nSet.Count;
                var weight = nSet.Count == 3 ? 0.22f : 0.32f;
                p = Vector3.Lerp(p, avg, weight);
            }
            smoothedCanon[c] = p;
        }

        var newPositions = new List<Vector3>(srcPositions.Count * 2);
        for (var i = 0; i < srcPositions.Count; i++)
        {
            newPositions.Add(smoothedCanon[canonToIdx[i]]);
        }

        var midpointCache = new Dictionary<long, int>();
        int GetMidpoint(int a, int b)
        {
            var ca = canonToIdx[a];
            var cb = canonToIdx[b];
            var key = EdgeKey(ca, cb);
            if (midpointCache.TryGetValue(key, out var idx)) return idx;

            var mid = (canonPositions[ca] + canonPositions[cb]) * 0.5f;
            if (edgeOpp.TryGetValue(key, out var opps) && opps.Count == 2)
            {
                mid = (canonPositions[ca] + canonPositions[cb]) * 0.375f +
                      (canonPositions[opps[0]] + canonPositions[opps[1]]) * 0.125f;
            }
            idx = newPositions.Count;
            newPositions.Add(mid);
            midpointCache[key] = idx;
            return idx;
        }

        var newFaces = new List<Face>(tris.Count * 4);
        foreach (var t in tris)
        {
            var mAB = GetMidpoint(t.a, t.b);
            var mBC = GetMidpoint(t.b, t.c);
            var mCA = GetMidpoint(t.c, t.a);

            var sg = t.smoothGroup > 0 ? t.smoothGroup : 1;

            newFaces.Add(new Face(new[] { t.a, mAB, mCA }) { submeshIndex = t.submesh, smoothingGroup = sg });
            newFaces.Add(new Face(new[] { t.b, mBC, mAB }) { submeshIndex = t.submesh, smoothingGroup = sg });
            newFaces.Add(new Face(new[] { t.c, mCA, mBC }) { submeshIndex = t.submesh, smoothingGroup = sg });
            newFaces.Add(new Face(new[] { mAB, mBC, mCA }) { submeshIndex = t.submesh, smoothingGroup = sg });
        }

        pb.RebuildWithPositionsAndFaces(newPositions, newFaces);
        return true;
    }

    private static bool ApplyBowserVisualPolish(GameObject root)
    {
        const string matDir = "Assets/Materials/Bowser";
        if (!Directory.Exists(matDir))
        {
            Directory.CreateDirectory(matDir);
            AssetDatabase.Refresh();
        }

        Material GetOrCreateMat(string fileName, Color col, float metallic = 0f, float smoothness = 0.32f)
        {
            var fullPath = $"{matDir}/{fileName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(fullPath);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, fullPath);
            }
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        var skinMat = GetOrCreateMat("Mat_Bowser_Skin", new Color(0.96f, 0.66f, 0.12f), 0f, 0.28f);
        var bellyMat = GetOrCreateMat("Mat_Bowser_Belly", new Color(0.89f, 0.80f, 0.62f), 0f, 0.22f);
        var shellGreenMat = GetOrCreateMat("Mat_Bowser_ShellGreen", new Color(0.13f, 0.46f, 0.18f), 0f, 0.38f);
        var shellRimMat = GetOrCreateMat("Mat_Bowser_ShellRim", new Color(0.94f, 0.92f, 0.86f), 0f, 0.30f);
        var spikeBaseMat = GetOrCreateMat("Mat_Bowser_SpikeBase", new Color(0.74f, 0.35f, 0.14f), 0f, 0.28f);
        var hornMat = GetOrCreateMat("Mat_Bowser_HornIvory", new Color(0.95f, 0.89f, 0.72f), 0.05f, 0.45f);
        var ironCuffMat = GetOrCreateMat("Mat_Bowser_IronCuff", new Color(0.16f, 0.16f, 0.19f), 0.35f, 0.48f);
        var silverSpikeMat = GetOrCreateMat("Mat_Bowser_SilverSpike", new Color(0.92f, 0.93f, 0.96f), 0.55f, 0.68f);

        void SetAllSlots(MeshRenderer mr, Material mat)
        {
            var count = Mathf.Max(1, mr.sharedMaterials != null ? mr.sharedMaterials.Length : 1);
            var mats = new Material[count];
            for (var i = 0; i < count; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
        }

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            var nm = mr.gameObject.name;
            if (nm.Contains("SpikedCollarRing") || nm.Contains("WristSpikedBand"))
                SetAllSlots(mr, ironCuffMat);
            else if (nm.Contains("NeckSpike") || nm.Contains("WristSpike"))
                SetAllSlots(mr, silverSpikeMat);
            else if (nm.Contains("ShellSpikeBase"))
                SetAllSlots(mr, spikeBaseMat);
            else if (nm.Contains("ShellSpikeHorn") || nm.Contains("Claw"))
                SetAllSlots(mr, hornMat);
            else if (nm.Contains("ShellCarapace"))
                SetAllSlots(mr, shellGreenMat);
            else if (nm.Contains("ShellRim"))
                SetAllSlots(mr, shellRimMat);
            else if (nm.Contains("Belly"))
                SetAllSlots(mr, bellyMat);
            else if (nm.Contains("Forearm") || nm.Contains("UpperArm") || nm.Contains("Thigh") ||
                     nm.Contains("Foot") || nm.Contains("Palm") || nm.Contains("Shin") ||
                     nm.Contains("TailSeg") || nm.Contains("TailTip") || nm.Contains("Bowser_Neck"))
                SetAllSlots(mr, skinMat);
            else if (nm.Contains("TailDorsalSpike"))
                SetAllSlots(mr, hornMat);
        }

        // Remove any old BellyPlate_* so we rebuild them cleanly on the outer surface
        var toDelete = new List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && (t.name.StartsWith("BellyPlate_") || t.name.StartsWith("PB_Shin_")))
                toDelete.Add(t.gameObject);
        }
        foreach (var go in toDelete)
        {
            Object.DestroyImmediate(go);
        }

        Transform FindPart(string exactName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == exactName) return t;
            }
            return null;
        }

        // 1. Segmented cream belly plates on the OUTER front surface of PB_Bowser_Belly
        var belly = FindPart("PB_Bowser_Belly");
        if (belly != null)
        {
            var bellyMf = belly.GetComponent<MeshFilter>();
            var b = bellyMf != null && bellyMf.sharedMesh != null
                ? bellyMf.sharedMesh.bounds
                : new Bounds(Vector3.zero, Vector3.one);

            for (var i = 0; i < 6; i++)
            {
                var tNorm = Mathf.Lerp(0.62f, -0.62f, i / 5f);
                var rib = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                rib.name = $"BellyPlate_{i}";
                var col = rib.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                rib.transform.SetParent(belly, false);

                var y = b.center.y + tNorm * b.extents.y * 0.85f;
                var profile = Mathf.Sqrt(Mathf.Max(0.18f, 1f - tNorm * tNorm * 0.72f));
                var z = b.center.z + b.extents.z * profile + 0.03f;
                var width = b.extents.x * 0.72f * profile;

                rib.transform.localPosition = new Vector3(b.center.x, y, z);
                rib.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                rib.transform.localScale = new Vector3(b.extents.y * 0.28f, width, b.extents.z * 0.34f);

                var pmr = rib.GetComponent<MeshRenderer>();
                if (pmr != null) SetAllSlots(pmr, bellyMat);
            }
        }

        // 2. Bridge floating gap between Thigh.L/R and Foot.L/R with Shin_L/R
        foreach (var side in new[] { "L", "R" })
        {
            var thighBone = FindPart($"Thigh.{side}");
            var footBone = FindPart($"Foot.{side}");
            if (thighBone == null || footBone == null) continue;

            var thighMesh = FindPart($"PB_Thigh_{side}");
            var footMesh = FindPart($"PB_Foot_{side}");
            if (thighMesh == null || footMesh == null) continue;

            var thighPosInThigh = thighMesh.localPosition;
            var footPosInThigh = thighBone.InverseTransformPoint(footMesh.position);
            var mid = Vector3.Lerp(thighPosInThigh, footPosInThigh, 0.54f);
            var delta = footPosInThigh - thighPosInThigh;
            var dist = delta.magnitude;

            var shin = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shin.name = $"PB_Shin_{side}";
            var col = shin.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            shin.transform.SetParent(thighBone, false);
            shin.transform.localPosition = mid;
            if (dist > 0.01f)
                shin.transform.localRotation = Quaternion.FromToRotation(Vector3.up, delta.normalized);
            shin.transform.localScale = new Vector3(0.36f, Mathf.Max(0.24f, dist * 0.58f), 0.36f);

            var smr = shin.GetComponent<MeshRenderer>();
            if (smr != null) SetAllSlots(smr, skinMat);
        }

        // 3. Make ShellRimCream clearly visible from side/front/back & seat ShellSpikeBase/Horn flush into ShellCarapace
        var shellCarapace = FindPart("PB_Bowser_ShellCarapace");
        var shellRim = FindPart("PB_Bowser_ShellRimCream");
        if (shellRim != null)
        {
            var s = shellRim.localScale;
            if (s.x < 1.35f)
            {
                shellRim.localScale = new Vector3(s.x * 1.14f, s.y * 1.14f, s.z * 1.25f);
                shellRim.localPosition += new Vector3(0f, 0f, 0.04f);
            }
        }
        if (shellCarapace != null)
        {
            var spine = shellCarapace.parent;
            if (spine != null)
            {
                var shellCenter = shellCarapace.localPosition;
                for (var i = 0; i < 10; i++)
                {
                    var baseT = FindPart($"PB_ShellSpikeBase_{i}");
                    var hornT = FindPart($"PB_ShellSpikeHorn_{i}");
                    if (baseT != null)
                    {
                        var dirToShell = (shellCenter - baseT.localPosition).normalized;
                        // Nudge spike base slightly inward so no floating gap from side view
                        if (Vector3.Distance(baseT.localPosition, shellCenter) > 0.72f)
                        {
                            baseT.localPosition += dirToShell * 0.07f;
                            if (hornT != null) hornT.localPosition += dirToShell * 0.05f;
                        }
                    }
                }
            }
        }

        // 4. Make wrist cuffs & claws read cleanly from side and front
        foreach (var side in new[] { "L", "R" })
        {
            var band = FindPart($"PB_WristSpikedBand_{side}");
            if (band != null && band.localScale.x < 1.05f)
                band.localScale = new Vector3(1.10f, 1.15f, 1.10f);

            for (var c = 0; c < 3; c++)
            {
                var claw = FindPart($"PB_HandClaw_{side}_{c}");
                if (claw != null && claw.localScale.x < 1.1f)
                    claw.localScale = new Vector3(1.30f, 1.35f, 1.30f);
            }
        }

        var neckRing = FindPart("PB_Neck_SpikedCollarRing");
        if (neckRing != null && neckRing.localScale.x < 1.05f)
            neckRing.localScale = new Vector3(1.06f, 1.12f, 1.06f);

        AssetDatabase.SaveAssets();
        return true;
    }

    [MenuItem("Remake/Polish King Bob-omb Visuals")]
    public static void PolishKingBobOmbPrefab()
    {
        const string kingPath = "Assets/Prefabs/Enemies/KingBobOmb.prefab";
        if (!File.Exists(kingPath)) return;
        var root = PrefabUtility.LoadPrefabContents(kingPath);
        try
        {
            // Smooth all ProBuilder meshes on KingBobOmb if not already high-poly
            foreach (var pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                if (pb == null || pb.faceCount == 0) continue;
                if (pb.faceCount < 160 && SubdivideSmooth(pb))
                {
                    pb.ToMesh();
                    pb.Refresh();
                }
            }

            ApplyKingBobOmbVisualPolish(root);
            PrefabUtility.SaveAsPrefabAsset(root, kingPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[LowPolyUpgrader] Polished KingBobOmb.prefab visuals.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool ApplyKingBobOmbVisualPolish(GameObject root)
    {
        const string matDir = "Assets/Materials/KingBobOmb";
        if (!Directory.Exists(matDir))
        {
            Directory.CreateDirectory(matDir);
            AssetDatabase.Refresh();
        }

        Material GetOrCreateMat(string fileName, Color col, float metallic = 0f, float smoothness = 0.35f)
        {
            var fullPath = $"{matDir}/{fileName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(fullPath);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, fullPath);
            }
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        var bodyMat = GetOrCreateMat("Mat_KingBobOmb_Body", new Color(0.08f, 0.09f, 0.16f), 0.15f, 0.84f);
        var limbBlueMat = GetOrCreateMat("Mat_KingBobOmb_LimbBlue", new Color(0.04f, 0.24f, 0.84f), 0.05f, 0.48f);
        var crownGoldMat = GetOrCreateMat("Mat_KingBobOmb_CrownGold", new Color(0.96f, 0.77f, 0.14f), 0.68f, 0.65f);
        var jewelRedMat = GetOrCreateMat("Mat_KingBobOmb_JewelRed", new Color(0.90f, 0.06f, 0.08f), 0.18f, 0.88f);
        var mustacheMat = GetOrCreateMat("Mat_KingBobOmb_Mustache", new Color(0.96f, 0.96f, 0.98f), 0f, 0.28f);
        var eyeWhiteMat = GetOrCreateMat("Mat_KingBobOmb_EyeWhite", new Color(0.98f, 0.98f, 1.0f), 0f, 0.45f);
        var gloveWhiteMat = GetOrCreateMat("Mat_KingBobOmb_GloveWhite", new Color(0.95f, 0.96f, 0.98f), 0f, 0.34f);
        var bootYellowMat = GetOrCreateMat("Mat_KingBobOmb_BootYellow", new Color(0.96f, 0.76f, 0.04f), 0.08f, 0.44f);
        var bootSoleMat = GetOrCreateMat("Mat_KingBobOmb_BootSole", new Color(0.68f, 0.48f, 0.06f), 0.05f, 0.25f);

        void SetAllSlots(MeshRenderer mr, Material mat)
        {
            var count = Mathf.Max(1, mr.sharedMaterials != null ? mr.sharedMaterials.Length : 1);
            var mats = new Material[count];
            for (var i = 0; i < count; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
        }

        Transform FindPart(string exactName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == exactName) return t;
            }
            return null;
        }

        // Remove any previously added procedural accents so re-running is idempotent
        var toDelete = new List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && (t.name.StartsWith("CrownJewel_") || t.name == "CrownRim" ||
                              t.name.StartsWith("MustacheTip_") || t.name.StartsWith("GloveThumb_") ||
                              t.name.StartsWith("GloveCuff_") || t.name.StartsWith("ArmTube_") ||
                              t.name.StartsWith("LegTube_") || t.name.StartsWith("BootCuff_") ||
                              t.name.StartsWith("BootToe_") || t.name.StartsWith("BootSole_")))
                toDelete.Add(t.gameObject);
        }
        foreach (var go in toDelete) Object.DestroyImmediate(go);

        // Assign materials across all existing parts
        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            var nm = mr.gameObject.name;
            if (nm == "Body") SetAllSlots(mr, bodyMat);
            else if (nm.StartsWith("CrownProng") || nm == "CrownBase" || nm.StartsWith("Key")) SetAllSlots(mr, crownGoldMat);
            else if (nm.StartsWith("CrownJewel")) SetAllSlots(mr, jewelRedMat);
            else if (nm.StartsWith("Mustache")) SetAllSlots(mr, mustacheMat);
            else if (nm.StartsWith("Eye")) SetAllSlots(mr, eyeWhiteMat);
            else if (nm.StartsWith("Glove")) SetAllSlots(mr, gloveWhiteMat);
            else if (nm.StartsWith("FootMesh")) SetAllSlots(mr, bootYellowMat);
        }

        // 1. Crown proportions + rim + 4 ruby jewels mounted on the 4 prongs
        var crownBase = FindPart("CrownBase");
        if (crownBase != null)
        {
            crownBase.localPosition = new Vector3(0f, 2.18f, 0f);
            crownBase.localScale = new Vector3(1.18f, 1.25f, 1.18f);

            var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rim.name = "CrownRim";
            var rcol = rim.GetComponent<Collider>();
            if (rcol != null) Object.DestroyImmediate(rcol);
            rim.transform.SetParent(crownBase.parent, false);
            rim.transform.localPosition = new Vector3(0f, 2.13f, 0f);
            rim.transform.localScale = new Vector3(0.76f, 0.055f, 0.76f);
            var rmr = rim.GetComponent<MeshRenderer>();
            if (rmr != null) SetAllSlots(rmr, crownGoldMat);
        }
        for (var i = 0; i < 4; i++)
        {
            var prong = FindPart($"CrownProng_{i}");
            if (prong != null)
            {
                var angle = i * 90f * Mathf.Deg2Rad;
                prong.localPosition = new Vector3(Mathf.Sin(angle) * 0.31f, 2.46f, Mathf.Cos(angle) * 0.31f);
                prong.localScale = new Vector3(1.25f, 1.42f, 1.25f);
            }
        }
        var frontJewel = FindPart("CrownJewel");
        if (frontJewel != null)
        {
            frontJewel.localPosition = new Vector3(0f, 2.38f, 0.37f);
            frontJewel.localScale = new Vector3(1.38f, 1.38f, 1.25f);
            for (var i = 1; i < 4; i++)
            {
                var angle = i * 90f * Mathf.Deg2Rad;
                var j = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                j.name = $"CrownJewel_{i}";
                var col = j.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                j.transform.SetParent(frontJewel.parent, false);
                j.transform.localPosition = new Vector3(Mathf.Sin(angle) * 0.37f, 2.38f, Mathf.Cos(angle) * 0.37f);
                j.transform.localScale = new Vector3(0.15f, 0.15f, 0.15f);
                var jmr = j.GetComponent<MeshRenderer>();
                if (jmr != null) SetAllSlots(jmr, jewelRedMat);
            }
        }

        // 2. Vertical white oval eyes
        var eyeL = FindPart("Eye_L");
        var eyeR = FindPart("Eye_R");
        if (eyeL != null)
        {
            eyeL.localPosition = new Vector3(-0.24f, 1.48f, 0.88f);
            eyeL.localScale = new Vector3(0.92f, 1.32f, 0.85f);
        }
        if (eyeR != null)
        {
            eyeR.localPosition = new Vector3(0.24f, 1.48f, 0.88f);
            eyeR.localScale = new Vector3(0.92f, 1.32f, 0.85f);
        }

        // 3. Big fluffy white handlebar mustache + curled tips
        foreach (var side in new[] { "L", "R" })
        {
            var sign = side == "L" ? -1f : 1f;
            var must = FindPart($"Mustache_{side}");
            if (must == null) continue;
            must.localPosition = new Vector3(sign * 0.36f, 1.08f, 0.86f);
            must.localRotation = Quaternion.Euler(-4f, sign * -16f, sign * 12f);
            must.localScale = new Vector3(1.55f, 1.45f, 1.25f);

            var tip = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tip.name = $"MustacheTip_{side}";
            var col = tip.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            tip.transform.SetParent(must.parent, false);
            tip.transform.localPosition = new Vector3(sign * 0.82f, 1.18f, 0.72f);
            tip.transform.localRotation = Quaternion.Euler(0f, sign * -22f, sign * -48f);
            tip.transform.localScale = new Vector3(0.32f, 0.36f, 0.28f);
            var tmr = tip.GetComponent<MeshRenderer>();
            if (tmr != null) SetAllSlots(tmr, mustacheMat);
        }

        // 4. Cobalt blue arms curving naturally forward from shoulder pivot + white wrist cuffs + chunky white fists
        foreach (var side in new[] { "L", "R" })
        {
            var sign = side == "L" ? -1f : 1f;
            var arm = FindPart($"Arm_{side}");
            if (arm == null) continue;
            arm.localPosition = new Vector3(sign * 0.86f, 1.24f, 0.04f);
            arm.localRotation = Quaternion.identity;

            var glovePos = new Vector3(sign * 0.24f, -0.16f, 0.34f);
            var armDir = glovePos.normalized;
            var armLen = glovePos.magnitude;

            var armTube = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            armTube.name = $"ArmTube_{side}";
            var acol = armTube.GetComponent<Collider>();
            if (acol != null) Object.DestroyImmediate(acol);
            armTube.transform.SetParent(arm, false);
            armTube.transform.localPosition = glovePos * 0.44f;
            armTube.transform.localRotation = Quaternion.FromToRotation(Vector3.up, armDir);
            armTube.transform.localScale = new Vector3(0.16f, armLen * 0.56f, 0.16f);
            var amr = armTube.GetComponent<MeshRenderer>();
            if (amr != null) SetAllSlots(amr, limbBlueMat);

            var cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cuff.name = $"GloveCuff_{side}";
            var ccol = cuff.GetComponent<Collider>();
            if (ccol != null) Object.DestroyImmediate(ccol);
            cuff.transform.SetParent(arm, false);
            cuff.transform.localPosition = glovePos * 0.78f;
            cuff.transform.localRotation = Quaternion.FromToRotation(Vector3.up, armDir);
            cuff.transform.localScale = new Vector3(0.28f, 0.06f, 0.28f);
            var cmr = cuff.GetComponent<MeshRenderer>();
            if (cmr != null) SetAllSlots(cmr, gloveWhiteMat);

            var glove = FindPart($"Glove_{side}");
            if (glove != null)
            {
                glove.localPosition = glovePos;
                glove.localRotation = Quaternion.LookRotation(armDir, Vector3.up);
                glove.localScale = new Vector3(1.24f, 1.18f, 1.32f);

                var thumb = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                thumb.name = $"GloveThumb_{side}";
                var col = thumb.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                thumb.transform.SetParent(glove, false);
                thumb.transform.localPosition = new Vector3(-sign * 0.20f, 0.18f, 0.08f);
                thumb.transform.localRotation = Quaternion.Euler(35f, sign * -20f, sign * 25f);
                thumb.transform.localScale = new Vector3(0.28f, 0.24f, 0.28f);
                var tmr = thumb.GetComponent<MeshRenderer>();
                if (tmr != null) SetAllSlots(tmr, gloveWhiteMat);
            }
        }

        // 5. Hip-pivoted cobalt blue legs + bright yellow curled-toe boots with ankle collar & darker sole
        foreach (var side in new[] { "L", "R" })
        {
            var sign = side == "L" ? -1f : 1f;
            var foot = FindPart($"Foot_{side}");
            if (foot == null) continue;
            foot.localPosition = new Vector3(sign * 0.40f, 0.48f, 0.02f);
            foot.localRotation = Quaternion.identity;

            var legTube = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            legTube.name = $"LegTube_{side}";
            var lcol = legTube.GetComponent<Collider>();
            if (lcol != null) Object.DestroyImmediate(lcol);
            legTube.transform.SetParent(foot, false);
            legTube.transform.localPosition = new Vector3(0f, -0.06f, -0.02f);
            legTube.transform.localScale = new Vector3(0.16f, 0.24f, 0.16f);
            var lmr = legTube.GetComponent<MeshRenderer>();
            if (lmr != null) SetAllSlots(lmr, limbBlueMat);

            var bootCuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            bootCuff.name = $"BootCuff_{side}";
            var bcol = bootCuff.GetComponent<Collider>();
            if (bcol != null) Object.DestroyImmediate(bcol);
            bootCuff.transform.SetParent(foot, false);
            bootCuff.transform.localPosition = new Vector3(0f, -0.20f, 0.02f);
            bootCuff.transform.localScale = new Vector3(0.32f, 0.06f, 0.32f);
            var bmr = bootCuff.GetComponent<MeshRenderer>();
            if (bmr != null) SetAllSlots(bmr, bootYellowMat);

            var footMesh = FindPart($"FootMesh_{side}");
            if (footMesh != null)
            {
                footMesh.localPosition = new Vector3(0f, -0.32f, 0.14f);
                footMesh.localScale = new Vector3(1.18f, 1.12f, 1.26f);
            }

            var toe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            toe.name = $"BootToe_{side}";
            var tcol = toe.GetComponent<Collider>();
            if (tcol != null) Object.DestroyImmediate(tcol);
            toe.transform.SetParent(foot, false);
            toe.transform.localPosition = new Vector3(0f, -0.24f, 0.40f);
            toe.transform.localRotation = Quaternion.Euler(35f, 0f, 0f);
            toe.transform.localScale = new Vector3(0.24f, 0.18f, 0.24f);
            var tomr = toe.GetComponent<MeshRenderer>();
            if (tomr != null) SetAllSlots(tomr, bootYellowMat);

            var sole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sole.name = $"BootSole_{side}";
            var scol = sole.GetComponent<Collider>();
            if (scol != null) Object.DestroyImmediate(scol);
            sole.transform.SetParent(foot, false);
            sole.transform.localPosition = new Vector3(0f, -0.44f, 0.16f);
            sole.transform.localScale = new Vector3(0.38f, 0.03f, 0.56f);
            var smr = sole.GetComponent<MeshRenderer>();
            if (smr != null) SetAllSlots(smr, bootSoleMat);
        }

        // 6. Seat WindupKey cleanly into back of Body
        var windup = FindPart("WindupKey");
        if (windup != null) windup.localPosition = new Vector3(0f, 1.24f, -0.84f);

        AssetDatabase.SaveAssets();
        return true;
    }

    [MenuItem("Remake/Polish Small Bob-omb Visuals")]
    public static void PolishBobOmbPrefab()
    {
        const string path = "Assets/Prefabs/Enemies/BobOmb.prefab";
        if (!File.Exists(path)) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            foreach (var pb in root.GetComponentsInChildren<ProBuilderMesh>(true))
            {
                if (pb == null || pb.faceCount == 0) continue;
                if (pb.faceCount < 160 && SubdivideSmooth(pb))
                {
                    pb.ToMesh();
                    pb.Refresh();
                }
            }

            ApplyBobOmbVisualPolish(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            AssetDatabase.SaveAssets();
            Debug.Log("[LowPolyUpgrader] Polished BobOmb.prefab visuals.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ApplyBobOmbVisualPolish(GameObject root)
    {
        const string matDir = "Assets/Materials/BobOmb";
        if (!Directory.Exists(matDir))
        {
            Directory.CreateDirectory(matDir);
            AssetDatabase.Refresh();
        }

        Material GetOrCreateMat(string fileName, Color col, float metallic = 0f, float smoothness = 0.35f, Color? emission = null)
        {
            var fullPath = $"{matDir}/{fileName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(fullPath);
            if (mat == null)
            {
                var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(sh);
                AssetDatabase.CreateAsset(mat, fullPath);
            }
            mat.color = col;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", emission.Value);
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        var bodyMat = GetOrCreateMat("Mat_BobOmb_Body", new Color(0.06f, 0.07f, 0.14f), 0.14f, 0.85f);
        var eyeMat = GetOrCreateMat("Mat_BobOmb_EyeWhite", new Color(0.98f, 0.98f, 1.0f), 0f, 0.45f);
        var keyGoldMat = GetOrCreateMat("Mat_BobOmb_KeyGold", new Color(0.96f, 0.78f, 0.14f), 0.65f, 0.65f);
        var shoeGoldMat = GetOrCreateMat("Mat_BobOmb_ShoeYellow", new Color(0.96f, 0.68f, 0.06f), 0.08f, 0.42f);
        var shoeSoleMat = GetOrCreateMat("Mat_BobOmb_ShoeSole", new Color(0.65f, 0.44f, 0.06f), 0.05f, 0.25f);
        var collarMat = GetOrCreateMat("Mat_BobOmb_FuseCollar", new Color(0.45f, 0.47f, 0.52f), 0.60f, 0.52f);
        var cordMat = GetOrCreateMat("Mat_BobOmb_FuseCord", new Color(0.92f, 0.88f, 0.78f), 0f, 0.20f);
        var sparkMat = GetOrCreateMat("Mat_BobOmb_FuseSpark", new Color(1.0f, 0.35f, 0.05f), 0f, 0.60f, new Color(1.0f, 0.42f, 0.06f) * 1.8f);

        void SetAllSlots(MeshRenderer mr, Material mat)
        {
            var count = Mathf.Max(1, mr.sharedMaterials != null ? mr.sharedMaterials.Length : 1);
            var mats = new Material[count];
            for (var i = 0; i < count; i++) mats[i] = mat;
            mr.sharedMaterials = mats;
        }

        Transform FindPart(string exactName)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == exactName) return t;
            }
            return null;
        }

        // Remove previously added shoe accents
        var toDelete = new List<GameObject>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t != null && (t.name.StartsWith("BootCuff_") || t.name.StartsWith("BootToe_") || t.name.StartsWith("BootSole_")))
                toDelete.Add(t.gameObject);
        }
        foreach (var go in toDelete) Object.DestroyImmediate(go);

        foreach (var mr in root.GetComponentsInChildren<MeshRenderer>(true))
        {
            if (mr == null) continue;
            var nm = mr.gameObject.name;
            if (nm == "Body") SetAllSlots(mr, bodyMat);
            else if (nm.StartsWith("Eye")) SetAllSlots(mr, eyeMat);
            else if (nm.StartsWith("Key")) SetAllSlots(mr, keyGoldMat);
            else if (nm.StartsWith("FootMesh")) SetAllSlots(mr, shoeGoldMat);
            else if (nm == "FuseCollar") SetAllSlots(mr, collarMat);
            else if (nm == "FuseCord") SetAllSlots(mr, cordMat);
            else if (nm == "FuseTip") SetAllSlots(mr, sparkMat);
        }

        var eyeL = FindPart("Eye_L");
        var eyeR = FindPart("Eye_R");
        if (eyeL != null)
        {
            eyeL.localPosition = new Vector3(-0.11f, 0.54f, 0.34f);
            eyeL.localScale = new Vector3(0.95f, 1.32f, 0.85f);
        }
        if (eyeR != null)
        {
            eyeR.localPosition = new Vector3(0.11f, 0.54f, 0.34f);
            eyeR.localScale = new Vector3(0.95f, 1.32f, 0.85f);
        }

        var windup = FindPart("WindupKey");
        if (windup != null)
        {
            windup.localPosition = new Vector3(0f, 0.48f, -0.34f);
            windup.localScale = new Vector3(1.18f, 1.18f, 1.18f);
        }

        // Authentic SM64 rounded golden shoes with curled toe & sole
        foreach (var side in new[] { "L", "R" })
        {
            var sign = side == "L" ? -1f : 1f;
            var foot = FindPart($"Foot_{side}");
            if (foot == null) continue;
            foot.localPosition = new Vector3(sign * 0.17f, 0.20f, 0.02f);
            foot.localRotation = Quaternion.Euler(0f, sign * 10f, 0f);

            var footMesh = FindPart($"FootMesh_{side}");
            if (footMesh != null)
            {
                footMesh.localPosition = new Vector3(0f, -0.10f, 0.06f);
                footMesh.localScale = new Vector3(1.10f, 1.05f, 1.18f);
            }

            var cuff = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cuff.name = $"BootCuff_{side}";
            var ccol = cuff.GetComponent<Collider>();
            if (ccol != null) Object.DestroyImmediate(ccol);
            cuff.transform.SetParent(foot, false);
            cuff.transform.localPosition = new Vector3(0f, -0.02f, 0f);
            cuff.transform.localScale = new Vector3(0.14f, 0.035f, 0.14f);
            var cmr = cuff.GetComponent<MeshRenderer>();
            if (cmr != null) SetAllSlots(cmr, shoeGoldMat);

            var toe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            toe.name = $"BootToe_{side}";
            var tcol = toe.GetComponent<Collider>();
            if (tcol != null) Object.DestroyImmediate(tcol);
            toe.transform.SetParent(foot, false);
            toe.transform.localPosition = new Vector3(0f, -0.08f, 0.18f);
            toe.transform.localRotation = Quaternion.Euler(32f, 0f, 0f);
            toe.transform.localScale = new Vector3(0.12f, 0.09f, 0.12f);
            var tmr = toe.GetComponent<MeshRenderer>();
            if (tmr != null) SetAllSlots(tmr, shoeGoldMat);

            var sole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            sole.name = $"BootSole_{side}";
            var scol = sole.GetComponent<Collider>();
            if (scol != null) Object.DestroyImmediate(scol);
            sole.transform.SetParent(foot, false);
            sole.transform.localPosition = new Vector3(0f, -0.17f, 0.07f);
            sole.transform.localScale = new Vector3(0.18f, 0.015f, 0.28f);
            var smr = sole.GetComponent<MeshRenderer>();
            if (smr != null) SetAllSlots(smr, shoeSoleMat);
        }

        AssetDatabase.SaveAssets();
    }
}

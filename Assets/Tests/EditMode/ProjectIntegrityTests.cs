using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Mario64.Tests.EditMode
{
    [TestFixture]
    public class ProjectIntegrityTests
    {
        private static readonly string[] CorePrefabNames =
        {
            "Mario",
            "Water",
            "BowserBoss",
            "KingBobOmb",
            "BobOmb"
        };

        [Test]
        public void CorePrefabs_Exist_AndHaveNoMissingScripts()
        {
            foreach (var prefabName in CorePrefabNames)
            {
                var guids = AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets/Prefabs" });
                var matchingPaths = guids
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Where(p => Path.GetFileNameWithoutExtension(p) == prefabName)
                    .ToArray();

                Assert.IsNotEmpty(matchingPaths, $"Core prefab '{prefabName}.prefab' was not found under Assets/Prefabs.");

                foreach (var path in matchingPaths)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    Assert.IsNotNull(prefab, $"Failed to load prefab at '{path}'.");

                    var transforms = prefab.GetComponentsInChildren<Transform>(true);
                    foreach (var t in transforms)
                    {
                        var missingCount = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject);
                        Assert.AreEqual(0, missingCount, $"Prefab '{path}' has missing script(s) on '{t.name}'.");
                    }
                }
            }
        }

        [Test]
        public void EditorBuildSettings_AllConfiguredScenesExistOnDisk()
        {
            var scenes = EditorBuildSettings.scenes;
            Assert.IsNotEmpty(scenes, "EditorBuildSettings.scenes should contain at least one scene.");

            foreach (var scene in scenes)
            {
                Assert.IsTrue(
                    File.Exists(scene.path),
                    $"Scene '{scene.path}' in EditorBuildSettings does not exist on disk.");
            }
        }

        [Test]
        public void Scripts_DoNotUseLegacyUnityInput()
        {
            var scriptFiles = Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories);
            Assert.IsNotEmpty(scriptFiles, "Expected C# scripts under Assets/Scripts.");

            var legacyInputPattern = new Regex(@"\bUnityEngine\.Input\b|\bInput\.(GetKey|GetKeyDown|GetKeyUp|GetAxis|GetAxisRaw|GetButton|GetButtonDown|GetButtonUp|GetMouseButton|GetMouseButtonDown|GetMouseButtonUp|mousePosition)\b");

            foreach (var file in scriptFiles)
            {
                var content = File.ReadAllText(file);
                Assert.IsFalse(
                    legacyInputPattern.IsMatch(content),
                    $"Legacy UnityEngine.Input usage detected in '{file}'. Use UnityEngine.InputSystem instead.");
            }
        }

        private static Assembly GetGameAssembly()
        {
#pragma warning disable UAC0005
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
#pragma warning restore UAC0005
        }

        [Test]
        public void CoreGameplayTypes_ExistInAssemblyCSharp_AndGameAudioSupportsMixerGroups()
        {
            var gameAssembly = GetGameAssembly();

            Assert.IsNotNull(gameAssembly, "Assembly-CSharp should be loaded.");

            string[] requiredTypes =
            {
                "GameAudio",
                "GameManager",
                "MarioRemakeController",
                "BowserBossController",
                "BobOmbPatrol"
            };

            foreach (var typeName in requiredTypes)
            {
                var type = gameAssembly.GetType(typeName);
                Assert.IsNotNull(type, $"Required gameplay type '{typeName}' was not found in Assembly-CSharp.");
            }

            var gameAudioType = gameAssembly.GetType("GameAudio");
            var musicGroupField = gameAudioType.GetField("musicMixerGroup", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var sfxGroupField = gameAudioType.GetField("sfxMixerGroup", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            Assert.IsNotNull(musicGroupField, "GameAudio should expose 'musicMixerGroup' for AudioMixer routing.");
            Assert.IsNotNull(sfxGroupField, "GameAudio should expose 'sfxMixerGroup' for AudioMixer routing.");
        }

        [Test]
        public void SaveManager_PersistsStarsAndCoins_Correctly()
        {
            var gameAssembly = GetGameAssembly();
            Assert.IsNotNull(gameAssembly, "Assembly-CSharp should be loaded.");

            var saveMgrType = gameAssembly.GetType("SaveManager");
            Assert.IsNotNull(saveMgrType, "SaveManager type should exist in Assembly-CSharp.");

            var tempPath = Path.Combine(Path.GetTempPath(), $"sm64_editmode_test_{Guid.NewGuid():N}.json");
            var go = new GameObject("TempSaveManagerTest");
            try
            {
                var mgr = go.AddComponent(saveMgrType);
                var setOverride = saveMgrType.GetMethod("SetSavePathOverride", BindingFlags.Instance | BindingFlags.Public);
                var recordStar = saveMgrType.GetMethod("RecordStarCollected", BindingFlags.Instance | BindingFlags.Public);
                var isStarCollected = saveMgrType.GetMethod("IsStarCollected", BindingFlags.Instance | BindingFlags.Public);
                var recordCoins = saveMgrType.GetMethod("RecordCourseCoins", BindingFlags.Instance | BindingFlags.Public);
                var getBestCoins = saveMgrType.GetMethod("GetBestCourseCoins", BindingFlags.Instance | BindingFlags.Public);
                var recordKey = saveMgrType.GetMethod("RecordBowserKey", BindingFlags.Instance | BindingFlags.Public);
                var hasKey = saveMgrType.GetMethod("HasBowserKey", BindingFlags.Instance | BindingFlags.Public);
                var saveToDisk = saveMgrType.GetMethod("SaveToDisk", BindingFlags.Instance | BindingFlags.Public);
                var loadFromDisk = saveMgrType.GetMethod("LoadFromDisk", BindingFlags.Instance | BindingFlags.Public);
                var totalStarsProp = saveMgrType.GetProperty("TotalStarsCollected", BindingFlags.Instance | BindingFlags.Public);

                setOverride.Invoke(mgr, new object[] { tempPath, false });
                var added1 = (bool)recordStar.Invoke(mgr, new object[] { "BobOmbBattlefield", 1, false });
                var addedDup = (bool)recordStar.Invoke(mgr, new object[] { "BobOmbBattlefield", 1, false });
                Assert.IsTrue(added1);
                Assert.IsFalse(addedDup);

                recordCoins.Invoke(mgr, new object[] { "BobOmbBattlefield", 42, false });
                recordCoins.Invoke(mgr, new object[] { "BobOmbBattlefield", 15, false });
                recordKey.Invoke(mgr, new object[] { "BowserDarkWorld", false });
                saveToDisk.Invoke(mgr, null);

                Assert.IsTrue(File.Exists(tempPath), "SaveManager should write save file cleanly.");
                var rawText = File.ReadAllText(tempPath);
                StringAssert.StartsWith("SM64E1:", rawText, "SaveManager should XOR+Base64 obfuscate save data.");

                // Clear in-memory override and reload from disk
                var go2 = new GameObject("TempSaveManagerReload");
                try
                {
                    var mgr2 = go2.AddComponent(saveMgrType);
                    setOverride.Invoke(mgr2, new object[] { tempPath, false });
                    loadFromDisk.Invoke(mgr2, null);

                    Assert.AreEqual(1, (int)totalStarsProp.GetValue(mgr2));
                    Assert.IsTrue((bool)isStarCollected.Invoke(mgr2, new object[] { "BobOmbBattlefield", 1 }));
                    Assert.AreEqual(42, (int)getBestCoins.Invoke(mgr2, new object[] { "BobOmbBattlefield" }));
                    Assert.IsTrue((bool)hasKey.Invoke(mgr2, new object[] { "BowserDarkWorld" }));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(go2);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
                if (File.Exists(tempPath)) File.Delete(tempPath);
                if (File.Exists(tempPath + ".bak")) File.Delete(tempPath + ".bak");
                if (File.Exists(tempPath + ".tmp")) File.Delete(tempPath + ".tmp");
            }
        }

        [Test]
        public void CoursePaintingPortal_And_PeachCastleHubScene_Exist()
        {
            Assert.IsTrue(File.Exists("Assets/Scenes/PeachCastleHub.unity"), "PeachCastleHub.unity scene should exist.");
            Assert.IsNotNull(Shader.Find("Mario64Remake/PaintingPortalWave"), "PaintingPortalWave shader should compile cleanly.");

            var gameAssembly = GetGameAssembly();
            Assert.IsNotNull(gameAssembly);

            var portalType = gameAssembly.GetType("CoursePaintingPortal");
            Assert.IsNotNull(portalType, "CoursePaintingPortal should exist in Assembly-CSharp.");

            var go = new GameObject("TempPortalCheck");
            try
            {
                go.AddComponent<BoxCollider>();
                var portal = go.AddComponent(portalType);
                var reqField = portalType.GetField("requiredStars", BindingFlags.Instance | BindingFlags.Public);
                var isUnlocked = portalType.GetMethod("IsUnlocked", BindingFlags.Instance | BindingFlags.Public);
                reqField.SetValue(portal, 3);
                Assert.IsFalse((bool)isUnlocked.Invoke(portal, new object[] { 2 }));
                Assert.IsTrue((bool)isUnlocked.Invoke(portal, new object[] { 3 }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}


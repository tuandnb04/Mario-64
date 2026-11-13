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

        [Test]
        public void CoreGameplayTypes_ExistInAssemblyCSharp_AndGameAudioSupportsMixerGroups()
        {
            var gameAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

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
    }
}


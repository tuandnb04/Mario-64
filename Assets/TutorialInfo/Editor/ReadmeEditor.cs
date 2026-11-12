using System.IO;
using Unity.Scripting.LifecycleManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace TutorialInfo.Editor
{
    [CustomEditor(typeof(Readme))]
    internal sealed partial class ReadmeEditor : UnityEditor.Editor
    {
        private const string KShowedReadmeSessionStateName = "ReadmeEditor.showedReadme";
        private const string KReadmeSourceDirectory = "Assets/TutorialInfo";

        [OnCodeLoaded]
        private static void OnCodeLoaded()
            => EditorApplication.delayCall += SelectReadmeAutomatically;

        private static void SelectReadmeAutomatically()
        {
            if (SessionState.GetBool(KShowedReadmeSessionStateName, false)) return;
            SelectReadme();
            SessionState.SetBool(KShowedReadmeSessionStateName, true);
        }

        private static Readme SelectReadme()
        {
            var ids = AssetDatabase.FindAssetGUIDs("Readme t:Readme");
            if (ids.Length != 1)
            {
                Debug.LogWarning("Couldn't find a readme");
                return null;
            }

            var readmeObject = AssetDatabase.LoadAssetByGUID<Readme>(ids[0]);
            Selection.activeObject = readmeObject;
            return readmeObject;
        }

        private static void RemoveTutorial()
        {
            if (!EditorUtility.DisplayDialog("Remove Readme Assets",
                    $"All contents under {KReadmeSourceDirectory} will be removed, are you sure you want to proceed?",
                    "Proceed",
                    "Cancel")) return;
            if (Directory.Exists(KReadmeSourceDirectory))
                AssetDatabase.DeleteAsset(KReadmeSourceDirectory);
            else
                Debug.LogWarning($"Could not find the Readme folder at {KReadmeSourceDirectory}");

            var readmeAsset = SelectReadme();
            if (readmeAsset != null)
            {
                var path = AssetDatabase.GetAssetPath(readmeAsset);
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.Refresh();
        }

        public override VisualElement CreateInspectorGUI()
        {
            var readme = (Readme)target;

            VisualElement root = new();
            root.styleSheets.Add(readme.commonStyle);
            root.styleSheets.Add(EditorGUIUtility.isProSkin ? readme.darkStyle : readme.lightStyle);

            //Header
            VisualElement title = new();
            title.AddToClassList("title");
            title.Add(ChainWithClass(new Image { image = readme.icon }, "title__icon"));
            title.Add(ChainWithClass(new Label(readme.title), "title__text"));
            root.Add(title);

            //Content
            foreach (var section in readme.sections)
            {
                VisualElement part = new();
                part.AddToClassList("section");

                if (!string.IsNullOrEmpty(section.heading))
                    part.Add(ChainWithClass(new Label(section.heading), "section__header"));

                if (!string.IsNullOrEmpty(section.text))
                    part.Add(ChainWithClass(new Label(section.text), "section__body"));

                if (!string.IsNullOrEmpty(section.linkText))
                {
                    var link = ChainWithClass(new Label(section.linkText), "section__link");
                    link.RegisterCallback<ClickEvent, string>((_, url) => Application.OpenURL(url), section.url);
                    part.Add(link);
                }

                root.Add(part);
            }

            var button = new Button(RemoveTutorial) { text = "Remove Readme Assets" };
            button.AddToClassList("remove-readme-button");
            root.Add(button);

            return root;

            VisualElement ChainWithClass(VisualElement created, string className)
            {
                created.AddToClassList(className);
                return created;
            }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace TutorialInfo
{
    public class Readme : ScriptableObject
    {
        public StyleSheet commonStyle;
        public StyleSheet darkStyle;
        public StyleSheet lightStyle;
        public Texture2D icon;
        public string title;
        public Section[] sections;

        [Serializable]
        public class Section
        {
            public string heading, text, linkText, url;
        }
    }
}

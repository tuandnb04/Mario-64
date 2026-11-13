using UnityEditor;
using UnityEngine;

public static class RemakeMaterialRestore
{
    [MenuItem("Remake/Restore All Materials to Clean Untextured URP Lit")]
    public static void RestoreAll()
    {
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null) return;

        string[] guids = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials" });
        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat.shader == null) continue;

            if (mat.shader.name.Contains("StylizedTriplanarPBR") ||
                (mat.shader.name.Contains("Universal Render Pipeline/Lit") && mat.GetTexture("_BaseMap") == null))
            {
                Color baseCol = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                Color emisCol = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;

                mat.shader = urpLit;
                mat.SetTexture("_BaseMap", null);
                mat.SetTexture("_BumpMap", null);
                mat.DisableKeyword("_NORMALMAP");
                mat.SetColor("_BaseColor", baseCol);
                mat.SetFloat("_Smoothness", 0.22f);

                if (emisCol.maxColorComponent > 0.01f)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", emisCol);
                }
                EditorUtility.SetDirty(mat);
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[RemakeMaterialRestore] Restored {count} materials to clean untextured URP/Lit!");
    }
}


using Mario64Remake;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BowserDarkWorldElement))]
public class BowserDarkWorldElementEditor : Editor
{
    public override void OnInspectorGUI()
    {
        var elem = (BowserDarkWorldElement)target;

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool changed = EditorGUI.EndChangeCheck();

        if (elem.hazardType != DarkWorldHazardType.PurpleSwitchCrystal)
            return;

        // If user already generated steps in the scene, keep them synced when count/size/spacing changes.
        if (changed && !Application.isPlaying && elem.gameObject.scene.IsValid() && elem.generatedStepsContainer != null)
        {
            elem.GenerateSteps(runtimeSpawn: false);
            EditorUtility.SetDirty(elem);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("Purple Switch - Tạo & Sắp Xếp Ô 1x1x1", EditorStyles.boldLabel);

        if (GUILayout.Button("Tạo & Sắp Xếp Ô 1x1x1 Theo Thứ Tự", GUILayout.Height(28)))
        {
            Undo.RegisterCompleteObjectUndo(elem, "Generate Purple Switch Steps");
            elem.GenerateSteps(runtimeSpawn: false);
            EditorUtility.SetDirty(elem);
        }

        if (GUILayout.Button("Xóa Các Ô Đã Tạo"))
        {
            Undo.RegisterCompleteObjectUndo(elem, "Clear Purple Switch Steps");
            elem.ClearGeneratedSteps();
            EditorUtility.SetDirty(elem);
        }

        EditorGUILayout.EndVertical();
    }

    private void OnSceneGUI()
    {
        var elem = (BowserDarkWorldElement)target;
        if (elem.hazardType != DarkWorldHazardType.PurpleSwitchCrystal)
            return;

        if (elem.linkedSwitchSteps != null && elem.linkedSwitchSteps.Length > 0)
        {
            for (var i = 0; i < elem.linkedSwitchSteps.Length; i++)
            {
                var step = elem.linkedSwitchSteps[i];
                if (step == null) continue;
                Handles.Label(step.transform.position + Vector3.up * 0.8f, $"#{i + 1}");
            }
            return;
        }

        for (var i = 0; i < elem.autoStepCount; i++)
        {
            var worldPos = elem.transform.TransformPoint(elem.firstStepLocalOffset + elem.stepSpacing * i);
            Handles.Label(worldPos + Vector3.up * 0.8f, $"#{i + 1}");
        }
    }
}

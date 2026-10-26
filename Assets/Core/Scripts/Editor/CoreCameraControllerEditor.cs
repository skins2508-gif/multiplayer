using UnityEditor;
using UnityEngine;

namespace Blocks.Gameplay.Core
{
    [CustomEditor(typeof(CoreCameraController))]
    [CanEditMultipleObjects]
    public class CoreCameraControllerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.LabelField("마우스 감도", EditorStyles.boldLabel);
            var useOverride = serializedObject.FindProperty("overrideCameraSensitivity");
            EditorGUILayout.PropertyField(useOverride, new GUIContent("플레이어 감도 사용", "카메라 프리팹의 감도보다 이 값을 우선합니다."));
            using (new EditorGUI.DisabledScope(!useOverride.hasMultipleDifferentValues && !useOverride.boolValue))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("mouseLookSensitivity"),
                    new GUIContent("기본 감도", "낮을수록 천천히 회전합니다. 기본값 0.08."));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("aimSensitivityMultiplier"),
                    new GUIContent("조준 감도 배율", "우클릭 조준 감도 = 기본 감도 × 배율. 기본값 0.65."));
            }
            EditorGUILayout.PropertyField(serializedObject.FindProperty("invertVerticalLook"), new GUIContent("상하 반전"));
            EditorGUILayout.HelpBox("Play 중에도 바로 반영됩니다. 영구 저장하려면 Play를 종료한 뒤 플레이어 프리팹에서 수정하세요.", MessageType.Info);
            EditorGUILayout.Space();
            DrawPropertiesExcluding(serializedObject, "m_Script", "overrideCameraSensitivity", "mouseLookSensitivity",
                "aimSensitivityMultiplier", "invertVerticalLook");
            serializedObject.ApplyModifiedProperties();
        }
    }
}

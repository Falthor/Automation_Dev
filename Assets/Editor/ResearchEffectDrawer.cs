using Game.Data;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// Draws a <see cref="ResearchEffect"/> on one line: its kind, then the one field that kind reads
    /// (a building, a recipe or a figure). The two others are not shown, and are cleared when the
    /// kind changes so an asset never carries a reference nothing reads. An empty building or recipe
    /// is tinted, since the effect then does nothing.
    /// </summary>
    [CustomPropertyDrawer(typeof(ResearchEffect))]
    public class ResearchEffectDrawer : PropertyDrawer
    {
        const float KindWidthRatio = 0.4f;
        const float Gap = 4f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty kind = property.FindPropertyRelative("kind");
            SerializedProperty building = property.FindPropertyRelative("building");
            SerializedProperty recipe = property.FindPropertyRelative("recipe");
            SerializedProperty value = property.FindPropertyRelative("value");

            EditorGUI.BeginProperty(position, label, property);
            Rect content = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var kindRect = new Rect(content.x, content.y, content.width * KindWidthRatio, content.height);
            var fieldRect = new Rect(kindRect.xMax + Gap, content.y, content.width - kindRect.width - Gap, content.height);

            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(kindRect, kind, GUIContent.none);
            if (EditorGUI.EndChangeCheck()) ClearUnread((ResearchEffectKind)kind.intValue, building, recipe, value);

            if (!kind.hasMultipleDifferentValues)
            {
                switch ((ResearchEffectKind)kind.intValue)
                {
                    case ResearchEffectKind.UnlockBuilding: DrawReference(fieldRect, building); break;
                    case ResearchEffectKind.UnlockRecipe: DrawReference(fieldRect, recipe); break;
                    default: EditorGUI.PropertyField(fieldRect, value, GUIContent.none); break;
                }
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        static void DrawReference(Rect rect, SerializedProperty reference)
        {
            Color previous = GUI.backgroundColor;
            if (!reference.hasMultipleDifferentValues && reference.objectReferenceValue == null) GUI.backgroundColor = new Color(1f, 0.45f, 0.45f);
            EditorGUI.PropertyField(rect, reference, GUIContent.none);
            GUI.backgroundColor = previous;
        }

        /// <summary>Empties the fields the new kind does not read. A figure survives a switch between the three figure kinds.</summary>
        static void ClearUnread(ResearchEffectKind kind, SerializedProperty building, SerializedProperty recipe, SerializedProperty value)
        {
            if (kind != ResearchEffectKind.UnlockBuilding) building.objectReferenceValue = null;
            if (kind != ResearchEffectKind.UnlockRecipe) recipe.objectReferenceValue = null;
            if (kind == ResearchEffectKind.UnlockBuilding || kind == ResearchEffectKind.UnlockRecipe) value.intValue = 0;
        }
    }
}

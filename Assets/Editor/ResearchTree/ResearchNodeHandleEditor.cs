using Game.Data;
using Game.Tools;
using UnityEditor;

namespace Game.EditorTools
{
    /// <summary>
    /// A selected node's inspector is its research's: the asset's own fields, drawn by the asset's
    /// own editor, under whatever ResearchTreeDiagnosis finds wrong with it. Editing here edits the
    /// asset - tier and angle included, and the handle follows.
    /// </summary>
    [CustomEditor(typeof(ResearchNodeHandle))]
    public sealed class ResearchNodeHandleEditor : Editor
    {
        Editor _researchEditor;

        public override void OnInspectorGUI()
        {
            ResearchDefinition research = ((ResearchNodeHandle)target).Research;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Asset", research, typeof(ResearchDefinition), false);
            }
            if (research == null) return;

            foreach (string problem in ResearchTreeDiagnosis.ProblemsOf(research)) EditorGUILayout.HelpBox(problem, MessageType.Warning);

            EditorGUILayout.Space();
            CreateCachedEditor(research, null, ref _researchEditor);
            _researchEditor.OnInspectorGUI();
        }

        void OnDisable()
        {
            if (_researchEditor != null) DestroyImmediate(_researchEditor);
        }
    }
}

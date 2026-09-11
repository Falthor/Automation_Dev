using Game.Data;
using Game.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>The research tree editor's entries, under Tools > Research Tree.</summary>
    public static class ResearchTreeMenu
    {
        const string NewResearchId = "new_research";
        const float NewResearchCost = 2000f;
        const float NewResearchAbsorption = 40f;

        [MenuItem("Tools/Research Tree/Open Editor Scene")]
        static void OpenScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ResearchTreeScene.ScenePath) == null) CreateScene();
            else EditorSceneManager.OpenScene(ResearchTreeScene.ScenePath, OpenSceneMode.Single);

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) return;
            view.in2DMode = true;
            view.LookAt(Vector3.zero, Quaternion.identity, (ResearchTreeDiagnosis.MaxTier + 1) * ResearchTreeScene.RingStep);
        }

        /// <summary>An empty scene saved at its path, then filled from the database - only if the scene file is missing.</summary>
        public static void CreateScene()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes/Tools")) AssetDatabase.CreateFolder("Assets/Scenes", "Tools");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ResearchTreeScene.ScenePath);
            ResearchTreeScene.Rebuild();
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// A new research asset, added to the database, with a handle selected for filling in. Placed
        /// one ring beyond the selected node, at its angle and with it as prerequisite; with nothing
        /// selected, on the second ring at 0 degrees and on no branch.
        /// </summary>
        [MenuItem("Tools/Research Tree/New Research")]
        static void NewResearch()
        {
            ResearchDatabase database = ResearchTreeScene.Database;
            ResearchNodeHandle selected = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponent<ResearchNodeHandle>() : null;
            ResearchDefinition parent = selected != null ? selected.Research : null;

            string id = UniqueId(database);
            var research = ScriptableObject.CreateInstance<ResearchDefinition>();
            AssetDatabase.CreateAsset(research, $"{ResearchTreeScene.ResearchFolder}/{id}.asset");

            var serialized = new SerializedObject(research);
            serialized.FindProperty("id").stringValue = id;
            serialized.FindProperty("displayName").stringValue = "Nouvelle recherche";
            serialized.FindProperty("cuCost").floatValue = NewResearchCost;
            serialized.FindProperty("absorptionRatePerSecond").floatValue = NewResearchAbsorption;
            serialized.FindProperty("tier").floatValue = parent != null ? parent.Tier + 1f : 2f;
            serialized.FindProperty("angle").floatValue = parent != null ? parent.Angle : 0f;
            if (parent != null)
            {
                SerializedProperty prerequisites = serialized.FindProperty("prerequisites");
                prerequisites.arraySize = 1;
                prerequisites.GetArrayElementAtIndex(0).objectReferenceValue = parent;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Undo.SetCurrentGroupName("New Research");
            int group = Undo.GetCurrentGroup();

            var databaseSerialized = new SerializedObject(database);
            SerializedProperty researches = databaseSerialized.FindProperty("researches");
            researches.arraySize++;
            researches.GetArrayElementAtIndex(researches.arraySize - 1).objectReferenceValue = research;
            databaseSerialized.ApplyModifiedProperties();

            ResearchNodeHandle handle = ResearchTreeScene.CreateHandle(research);
            Undo.RegisterCreatedObjectUndo(handle.gameObject, "New Research");
            Undo.CollapseUndoOperations(group);

            AssetDatabase.SaveAssets();
            Selection.activeGameObject = handle.gameObject;
        }

        [MenuItem("Tools/Research Tree/New Research", true)]
        static bool CanCreateResearch() => ResearchTreeScene.IsOpen && ResearchTreeScene.Database != null;

        [MenuItem("Tools/Research Tree/Rebuild Scene From Assets")]
        static void RebuildScene() => ResearchTreeScene.Rebuild();

        [MenuItem("Tools/Research Tree/Rebuild Scene From Assets", true)]
        static bool CanRebuildScene() => ResearchTreeScene.IsOpen;

        /// <summary>new_research, then new_research_2, _3... - the first id no asset file and no research already uses.</summary>
        static string UniqueId(ResearchDatabase database)
        {
            string id = NewResearchId;
            for (int n = 2; AssetDatabase.LoadAssetAtPath<Object>($"{ResearchTreeScene.ResearchFolder}/{id}.asset") != null || database.Get(id) != null; n++)
            {
                id = $"{NewResearchId}_{n}";
            }
            return id;
        }
    }
}

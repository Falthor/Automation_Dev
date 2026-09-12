using System.Collections.Generic;
using Game.Data;
using Game.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    /// <summary>
    /// The research tree editor's scene (Tools > Research Tree): one handle per research of the
    /// database, where the research's asset places it - its distance in rings and its angle, through
    /// ResearchNetworkPlacement - and nothing else.
    ///
    /// <b>The assets are the only source of truth.</b> Letting go of a moved handle writes its distance
    /// and angle to its asset - pulled onto a ring when close to one (Settle); any other change to the asset -
    /// the inspector, an undo, a merge - moves the handle to match. The scene is rebuilt from the
    /// database on opening and whenever its hierarchy changes: a missing handle is created, one whose
    /// research is gone or already has a handle is removed. What the scene file holds is disposable.
    /// </summary>
    [InitializeOnLoad]
    public static class ResearchTreeScene
    {
        public const string ScenePath = "Assets/Scenes/Tools/ResearchTree.unity";
        public const string DatabasePath = "Assets/Data/Research/ResearchDatabase.asset";
        public const string ResearchFolder = "Assets/Data/Research";

        /// <summary>World units between two rings - the scene's own scale, unrelated to the panel's.</summary>
        public const float RingStep = 2f;

        /// <summary>How close to a ring, in rings, a dropped node is pulled onto it.</summary>
        const float MagnetRings = 0.15f;

        static readonly List<ResearchNodeHandle> _handles = new List<ResearchNodeHandle>();
        static bool _handlesStale = true;

        /// <summary>The name each handle was last given, so a rename in the hierarchy can be told apart from a new display name arriving from the asset. Never cleared on a rebuild: renaming an object is itself a hierarchy change, and clearing here would forget the rename before it is read.</summary>
        static readonly Dictionary<ResearchNodeHandle, string> _shownNames = new Dictionary<ResearchNodeHandle, string>();

        /// <summary>How long an id must stay unchanged before the file takes its name - so typing an id renames the file once, not at every keystroke.</summary>
        const double IdSettleSeconds = 1.0;

        /// <summary>An id waiting to become its research's file name, and since when it has been that id.</summary>
        static readonly Dictionary<ResearchDefinition, (string id, double since)> _pendingFileNames = new Dictionary<ResearchDefinition, (string id, double since)>();

        /// <summary>File renames already refused, as path and id - reported once, not every tick.</summary>
        static readonly HashSet<string> _refusedFileNames = new HashSet<string>();

        /// <summary>Set by anything that may have put the scene out of step with the database; honoured on the next editor tick, never in the middle of another event.</summary>
        static bool _rebuildPending = true;

        static ResearchTreeScene()
        {
            EditorApplication.update += KeepInStep;
            EditorApplication.hierarchyChanged += RequestRebuild;
            EditorApplication.projectChanged += RequestRebuild;
            Undo.undoRedoPerformed += RequestRebuild;
            EditorSceneManager.sceneOpened += (scene, _) => { if (IsTreeScene(scene)) Rebuild(); };
        }

        public static bool IsOpen => IsTreeScene(SceneManager.GetActiveScene());

        static bool IsTreeScene(Scene scene) => scene.IsValid() && scene.path == ScenePath;

        public static ResearchDatabase Database => AssetDatabase.LoadAssetAtPath<ResearchDatabase>(DatabasePath);

        static void RequestRebuild()
        {
            _handlesStale = true;
            _rebuildPending = true;
            ResearchTreeDiagnosis.Invalidate();
        }

        /// <summary>The handles of the open tree scene - empty when it is not the active scene. May hold a destroyed one until the next rebuild.</summary>
        public static IReadOnlyList<ResearchNodeHandle> Handles
        {
            get
            {
                if (_handlesStale) CollectHandles();
                return _handles;
            }
        }

        public static ResearchNodeHandle HandleOf(ResearchDefinition research)
        {
            foreach (ResearchNodeHandle handle in Handles)
            {
                if (handle != null && ReferenceEquals(handle.Research, research)) return handle;
            }
            return null;
        }

        /// <summary>Where a research sits in the scene: its stored distance and angle, on the XY plane.</summary>
        public static Vector3 PositionOf(ResearchDefinition research)
        {
            Vector2 offset = ResearchNetworkPlacement.Offset(research.Tier, research.Angle, RingStep);
            return new Vector3(offset.x, offset.y, 0f);
        }

        /// <summary>Every research of the database gets exactly one handle, where its asset places it.</summary>
        public static void Rebuild()
        {
            _rebuildPending = false;
            if (!IsOpen) return;

            ResearchDatabase database = Database;
            if (database == null)
            {
                Debug.LogError($"Research tree: no database at {DatabasePath}.");
                return;
            }

            var wanted = new List<ResearchDefinition>();
            foreach (ResearchDefinition core in database.GetCores())
            {
                if (core != null && !wanted.Contains(core)) wanted.Add(core);
            }
            foreach (ResearchDefinition research in database.GetAll())
            {
                if (research != null && !wanted.Contains(research)) wanted.Add(research);
            }

            CollectHandles();
            var kept = new HashSet<ResearchDefinition>();
            foreach (ResearchNodeHandle handle in _handles)
            {
                if (handle.Research == null || !wanted.Contains(handle.Research) || !kept.Add(handle.Research)) Object.DestroyImmediate(handle.gameObject);
            }
            foreach (ResearchDefinition research in wanted)
            {
                if (!kept.Contains(research)) CreateHandle(research);
            }

            CollectHandles();
            foreach (ResearchNodeHandle handle in _handles) Place(handle);
            ResearchTreeDiagnosis.Invalidate();
        }

        public static ResearchNodeHandle CreateHandle(ResearchDefinition research)
        {
            var gameObject = new GameObject(Label(research));
            ResearchNodeHandle handle = gameObject.AddComponent<ResearchNodeHandle>();
            handle.Research = research;
            Place(handle);
            _handlesStale = true;
            return handle;
        }

        /// <summary>Adds the link if it is not there, removes it if it is. Undoable. Returns whether the link now exists.</summary>
        public static bool ToggleLink(ResearchDefinition prerequisite, ResearchDefinition research)
        {
            var serialized = new SerializedObject(research);
            SerializedProperty list = serialized.FindProperty("prerequisites");

            bool linked = true;
            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                if (!ReferenceEquals(element.objectReferenceValue, prerequisite)) continue;

                element.objectReferenceValue = null;
                list.DeleteArrayElementAtIndex(i);
                linked = false;
                break;
            }
            if (linked)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = prerequisite;
            }

            serialized.ApplyModifiedProperties();
            ResearchTreeDiagnosis.Invalidate();
            return linked;
        }

        static void CollectHandles()
        {
            _handlesStale = false;
            _handles.Clear();
            if (!IsOpen) return;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.TryGetComponent(out ResearchNodeHandle handle)) _handles.Add(handle);
            }
        }

        /// <summary>
        /// Every editor tick while the scene is open: a handle that was moved writes its distance and
        /// angle to the asset, and every handle is then put back where its asset says - which is what
        /// settles a dropped node, and what makes a handle follow a change made anywhere else.
        ///
        /// Nothing happens while a control is held: settling under the cursor would tug the node about
        /// while it is dragged. It follows the cursor freely and settles when it is let go.
        /// </summary>
        static void KeepInStep()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !IsOpen) return;
            if (GUIUtility.hotControl != 0) return;
            if (_rebuildPending)
            {
                Rebuild();
                return;
            }

            foreach (ResearchNodeHandle handle in Handles)
            {
                if (handle == null || handle.Research == null) continue;

                if (handle.transform.hasChanged)
                {
                    ResearchNetworkPlacement.FromOffset(handle.transform.position, RingStep, out float tier, out float angle);
                    WritePlacement(handle.Research, Settle(tier), Mathf.Round(angle * 10f) / 10f);
                }
                Place(handle);
                NameFileAfterId(handle.Research);
            }
        }

        /// <summary>
        /// Keeps a research's asset file named after its id, as every shipped research is - a new one
        /// starts as new_research and follows its id once it has one. Waits until the id has been left
        /// alone for IdSettleSeconds and no text field is being edited. An id that cannot be a file
        /// name, or is taken by another file, is reported once and the file keeps its name. Renaming
        /// breaks no reference: the database and the prerequisites point at the asset, not its name.
        /// </summary>
        static void NameFileAfterId(ResearchDefinition research)
        {
            string id = research.Id;
            if (string.IsNullOrWhiteSpace(id) || research.name == id)
            {
                _pendingFileNames.Remove(research);
                return;
            }

            string path = AssetDatabase.GetAssetPath(research);
            if (string.IsNullOrEmpty(path) || _refusedFileNames.Contains(path + ">" + id)) return;

            double now = EditorApplication.timeSinceStartup;
            if (!_pendingFileNames.TryGetValue(research, out (string id, double since) pending) || pending.id != id)
            {
                _pendingFileNames[research] = (id, now);
                return;
            }
            if (now - pending.since < IdSettleSeconds || EditorGUIUtility.editingTextField) return;

            _pendingFileNames.Remove(research);
            string error = AssetDatabase.RenameAsset(path, id);
            if (string.IsNullOrEmpty(error)) return;

            _refusedFileNames.Add(path + ">" + id);
            Debug.LogWarning($"Research tree: {research.name}.asset keeps its name - its id '{id}' cannot be the file name ({error}).", research);
        }

        /// <summary>
        /// Where a dropped node's distance lands: onto a ring when within MagnetRings of it, so a ring is
        /// easy to hit, and otherwise where it was let go, to a tenth of a ring.
        /// </summary>
        static float Settle(float tier)
        {
            float ring = Mathf.Round(tier);
            return Mathf.Abs(tier - ring) < MagnetRings ? ring : Mathf.Round(tier * 10f) / 10f;
        }

        static void WritePlacement(ResearchDefinition research, float tier, float angle)
        {
            if (Mathf.Approximately(research.Tier, tier) && Mathf.Approximately(research.Angle, angle)) return;

            var serialized = new SerializedObject(research);
            serialized.FindProperty("tier").floatValue = tier;
            serialized.FindProperty("angle").floatValue = angle;
            serialized.ApplyModifiedProperties();
            ResearchTreeDiagnosis.Invalidate();
        }

        /// <summary>
        /// Puts a handle where its asset says, under the research's display name, without that counting
        /// as a move. A handle renamed in the hierarchy since it was last named gives the research that
        /// name - so renaming a node there renames the research, rather than being undone a tick later.
        /// </summary>
        static void Place(ResearchNodeHandle handle)
        {
            Vector3 position = PositionOf(handle.Research);
            if (handle.transform.position != position) handle.transform.position = position;

            string label = Label(handle.Research);
            if (_shownNames.TryGetValue(handle, out string shown) && handle.name != shown && handle.name != label && !string.IsNullOrWhiteSpace(handle.name))
            {
                WriteDisplayName(handle.Research, handle.name);
                label = handle.name;
            }
            if (handle.name != label) handle.name = label;
            _shownNames[handle] = label;
            handle.transform.hasChanged = false;
        }

        /// <summary>What a node is called in the hierarchy and the scene: its display name, or its asset's name while it has none.</summary>
        public static string Label(ResearchDefinition research) => string.IsNullOrEmpty(research.DisplayName) ? research.name : research.DisplayName;

        static void WriteDisplayName(ResearchDefinition research, string displayName)
        {
            var serialized = new SerializedObject(research);
            serialized.FindProperty("displayName").stringValue = displayName;
            serialized.ApplyModifiedProperties();
        }
    }
}

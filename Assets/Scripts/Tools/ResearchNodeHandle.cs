using Game.Data;
using UnityEngine;

namespace Game.Tools
{
    /// <summary>
    /// One research on the research tree editor's scene, and nothing more than a way to grab it: the
    /// research it stands for. Where it sits is read from that research's asset and written back to
    /// it by the editor (Game.EditorTools.ResearchTreeScene); the scene holds nothing the assets could
    /// not rebuild.
    ///
    /// In a runtime assembly only because Unity attaches no component from an editor one, and that
    /// assembly compiles in the editor alone (its UNITY_EDITOR constraint), so no build carries it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ResearchNodeHandle : MonoBehaviour
    {
        [SerializeField] ResearchDefinition research;

        public ResearchDefinition Research
        {
            get => research;
            set => research = value;
        }
    }
}

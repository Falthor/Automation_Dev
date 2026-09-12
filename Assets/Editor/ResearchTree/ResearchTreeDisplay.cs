using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    /// <summary>
    /// How big the research tree's balls are drawn. A view preference, and the only one the tool has.
    ///
    /// <b>In EditorPrefs, not in the scene and not in an asset.</b> The scene is rebuilt from the
    /// database and deliberately holds nothing (see ResearchTreeScene), so a radius stored there would
    /// be erased at the next rebuild. An asset would be worse: it would put how one person likes to
    /// look at the tree into the tree's own data, and into everyone else's commits. This stays on the
    /// machine that set it.
    /// </summary>
    public static class ResearchTreeDisplay
    {
        const string CoreRadiusKey = "Game.ResearchTree.CoreRadius";
        const string NodeRadiusKey = "Game.ResearchTree.NodeRadius";

        /// <summary>What the tree was drawn at while these were constants, so a machine that has never touched the sliders opens on exactly the picture this tool has always shown.</summary>
        public const float DefaultCoreRadius = 0.45f;
        public const float DefaultNodeRadius = 0.3f;

        public const float MinRadius = 0.05f;
        public const float MaxRadius = 2f;

        public static float CoreRadius
        {
            get => EditorPrefs.GetFloat(CoreRadiusKey, DefaultCoreRadius);
            set => EditorPrefs.SetFloat(CoreRadiusKey, Mathf.Clamp(value, MinRadius, MaxRadius));
        }

        public static float NodeRadius
        {
            get => EditorPrefs.GetFloat(NodeRadiusKey, DefaultNodeRadius);
            set => EditorPrefs.SetFloat(NodeRadiusKey, Mathf.Clamp(value, MinRadius, MaxRadius));
        }

        /// <summary>The radius a node is drawn at - the one question every drawing path asks, so neither the gizmo, the icon nor the hover test can size a ball differently from the others.</summary>
        public static float RadiusOf(bool isCore) => isCore ? CoreRadius : NodeRadius;

        /// <summary>
        /// The side of the largest square that fits inside a ball of this radius, which is how big an
        /// icon is drawn: derived rather than given a slider of its own, so there is one size to set
        /// and an icon can never outgrow the ball holding it.
        /// </summary>
        public static float IconSideFor(float radius) => radius * Mathf.Sqrt(2f);

        public static void ResetToDefaults()
        {
            CoreRadius = DefaultCoreRadius;
            NodeRadius = DefaultNodeRadius;
        }
    }
}

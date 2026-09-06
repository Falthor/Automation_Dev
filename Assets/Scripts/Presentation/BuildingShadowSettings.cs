using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The single global source for every DropShadow in the game: one sun direction, one opacity,
    /// one depth. Changing the offset here turns every building's shadow at once - no per-building
    /// override exists, deliberately, because a scene lit by two suns reads as a bug.
    ///
    /// A definition asset, not runtime state (DEVELOPMENT_RULES.md §1): the fields are authored in
    /// the inspector and only ever read at runtime, so they stay private with getters rather than
    /// public fields. That also keeps them safe under this project's disabled Domain Reload, where
    /// a runtime write to an asset would survive into the next Play session.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildingShadowSettings", menuName = "Game/Presentation/Building Shadow Settings")]
    public sealed class BuildingShadowSettings : ScriptableObject
    {
        [SerializeField, Range(0f, 1f)] float alpha = 0.45f;

        /// <summary>World-unit displacement from the caster to its shadow - i.e. the direction the sun comes from, negated. World units, never local: the shadow must not turn when the building it belongs to does.</summary>
        [SerializeField] Vector2 offset = new Vector2(0.25f, -0.25f);

        /// <summary>Size of the shadow relative to the building casting it - 1 is the exact silhouette, above 1 grows it around its own centre (which reads as the building standing taller off the ground), below 1 shrinks it.</summary>
        [SerializeField, Min(0f)] float scale = 1f;

        public float Alpha => alpha;
        public Vector2 Offset => offset;
        public float Scale => scale;

        /// <summary>Black at the configured opacity - the shadow renderer's tint over the caster's own silhouette.</summary>
        public Color ShadowColor => new Color(0f, 0f, 0f, alpha);
    }
}

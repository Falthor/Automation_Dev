using UnityEngine;

namespace Game.Data
{
    /// <summary>Static, immutable definition of a buildable building. Not runtime state.</summary>
    public abstract class BuildingDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Vector2Int footprintSize = new Vector2Int(1, 1);
        [SerializeField] Color placeholderColor = Color.white;

        public string Id => id;
        public string DisplayName => displayName;
        public Vector2Int FootprintSize => footprintSize;
        public Color PlaceholderColor => placeholderColor;

        /// <summary>
        /// Whether this building has a single fixed output side (drawn as an arrow, both on the
        /// construction ghost and the built view). False by default - most buildings have no
        /// directional output (e.g. Storage accepts input from any side and has none).
        /// </summary>
        public virtual bool HasOutputArrow => false;

        /// <summary>
        /// Whether this building has a single fixed input side. False by default - no current
        /// building has one (Storage accepts from every adjacent side instead), but the hook
        /// exists so a future belt-fed single-input building can opt in without touching the
        /// ghost/view code that draws it.
        /// </summary>
        public virtual bool HasInputArrow => false;
    }
}

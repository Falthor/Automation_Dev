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
    }
}

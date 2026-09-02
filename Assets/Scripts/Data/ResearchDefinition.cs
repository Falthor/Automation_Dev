using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of one research (CONTRACTS.md §11). Referenced directly by
    /// BuildingDefinition/RecipeDefinition where a gate applies - no separate id-keyed registry
    /// is needed since Unity assets already hold direct references.
    /// </summary>
    [CreateAssetMenu(fileName = "ResearchDefinition", menuName = "Game/Research/Research Definition")]
    public sealed class ResearchDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField, Min(0f)] float cost;

        public string Id => id;
        public string DisplayName => displayName;
        public float Cost => cost;
    }
}

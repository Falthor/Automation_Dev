using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of one research (CONTRACTS.md §11). Enumerated as a whole by
    /// ResearchDatabase, which is what the UI reads to build the tree - this asset itself is
    /// still what BuildingDefinition.UnlockResearch/RecipeDefinition.UnlockResearch reference
    /// directly for a gate check, exactly as before.
    /// </summary>
    [CreateAssetMenu(fileName = "ResearchDefinition", menuName = "Game/Research/Research Definition")]
    public sealed class ResearchDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField, TextArea] string description;
        [SerializeField] Sprite icon;
        [SerializeField, Min(0f)] float cuCost;
        [SerializeField] ResearchDefinition[] prerequisites = System.Array.Empty<ResearchDefinition>();

        /// <summary>Ceiling on how many CU per second this research can absorb, even when the reserve holds far more - the runtime rate is min(this, whatever the reserve can currently give).</summary>
        [SerializeField, Min(0f)] float absorptionRatePerSecond;

        /// <summary>Progression tier, for the future radial menu's rayon placement (GDD §5.4) - not used by the linear introduction menu.</summary>
        [SerializeField] int tier;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public float CuCost => cuCost;

        /// <summary>Every research that must already be completed before this one may be started. Empty means available from the start.</summary>
        public IReadOnlyList<ResearchDefinition> Prerequisites => prerequisites;

        public float AbsorptionRatePerSecond => absorptionRatePerSecond;
        public int Tier => tier;
    }
}

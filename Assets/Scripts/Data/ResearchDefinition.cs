using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of one research (CONTRACTS.md §11): its cost, its prerequisites and its
    /// effects. Enumerated by ResearchDatabase for the tree, and by ResearchCatalog for everything
    /// the game asks about researches.
    ///
    /// <b>The effects live here and nowhere else.</b> A building or a recipe does not name the
    /// research that opens it - the research names it - and no system keys anything on the id.
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

        /// <summary>What completing this research does - see ResearchEffect.</summary>
        [SerializeField] ResearchEffect[] effects = System.Array.Empty<ResearchEffect>();

        /// <summary>Ceiling on how many CU per second this research can absorb, even when the reserve holds far more - the runtime rate is min(this, whatever the reserve can currently give).</summary>
        [SerializeField, Min(0f)] float absorptionRatePerSecond;

        /// <summary>Progression tier: how far from the centre this research sits on the research network, counted in rings (GDD §5.4) - a whole number is on a ring, anything else between two. With Angle, its whole position there - see ResearchNetworkPlacement.</summary>
        [SerializeField] float tier;

        /// <summary>Where on its ring, in degrees counter-clockwise from the right. Placed by hand in the research tree editor; nothing computes or corrects it.</summary>
        [SerializeField] float angle;

        public string Id => id;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public float CuCost => cuCost;

        /// <summary>Every research that must already be completed before this one may be started. Empty means available from the start.</summary>
        public IReadOnlyList<ResearchDefinition> Prerequisites => prerequisites;

        /// <summary>What completing this research does. Empty for a research that only opens the way to others.</summary>
        public IReadOnlyList<ResearchEffect> Effects => effects ?? (IReadOnlyList<ResearchEffect>)System.Array.Empty<ResearchEffect>();

        public float AbsorptionRatePerSecond => absorptionRatePerSecond;
        public float Tier => tier;
        public float Angle => angle;
    }
}

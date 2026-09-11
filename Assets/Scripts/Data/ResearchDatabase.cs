using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Single source of truth for the research tree, on the same model as ItemDatabase and
    /// RecipeDatabase - one asset assigned on GameRuntime, not a scene-serialized array
    /// (TASK_02_REFONTE_RECHERCHE.md §4). The tree is data the UI reads, it no longer defines it.
    /// </summary>
    [CreateAssetMenu(fileName = "ResearchDatabase", menuName = "Game/Research/Research Database")]
    public sealed class ResearchDatabase : ScriptableObject
    {
        [SerializeField] ResearchDefinition[] researches;

        /// <summary>
        /// The roots of the research network: Research, Buildings, Armament (GDD §5.4). Where each
        /// sits is its own Tier and Angle, not its place in this list.
        ///
        /// Kept apart from the researches because a core is never bought: it is an unlock id granted
        /// by whatever powers it (ResearchSystem.Grant), and a research joins a branch by naming its
        /// core as a prerequisite. Out of GetAll, so nothing lists or counts a core as a research;
        /// still found by Get.
        /// </summary>
        [SerializeField] ResearchDefinition[] cores;

        Dictionary<string, ResearchDefinition> _byId;

        /// <summary>Returns the research for researchId, or null if it isn't registered.</summary>
        public ResearchDefinition Get(string researchId)
        {
            if (_byId == null) BuildLookup();
            return researchId != null && _byId.TryGetValue(researchId, out var research) ? research : null;
        }

        /// <summary>Every research in the tree, in serialization order - the UI enumerates this to build the whole menu rather than looking up one id at a time.</summary>
        public IReadOnlyList<ResearchDefinition> GetAll()
        {
            return researches ?? System.Array.Empty<ResearchDefinition>();
        }

        /// <summary>The network's cores, in layout order. Empty when none are assigned.</summary>
        public IReadOnlyList<ResearchDefinition> GetCores()
        {
            return cores ?? System.Array.Empty<ResearchDefinition>();
        }

        void BuildLookup()
        {
            _byId = new Dictionary<string, ResearchDefinition>();
            Index(researches);
            Index(cores);
        }

        void Index(ResearchDefinition[] definitions)
        {
            if (definitions == null) return;

            foreach (ResearchDefinition research in definitions)
            {
                if (research != null && !string.IsNullOrEmpty(research.Id)) _byId[research.Id] = research;
            }
        }
    }
}

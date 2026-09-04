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

        void BuildLookup()
        {
            _byId = new Dictionary<string, ResearchDefinition>();
            if (researches == null) return;

            foreach (ResearchDefinition research in researches)
            {
                if (research != null && !string.IsNullOrEmpty(research.Id)) _byId[research.Id] = research;
            }
        }
    }
}

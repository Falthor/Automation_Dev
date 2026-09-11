using System.Collections.Generic;

namespace Game.Data
{
    /// <summary>
    /// Every research the game knows, indexed the two ways the game asks about them (CONTRACTS.md
    /// §11): by unlock id, to read a completed research's effects, and by what it unlocks, to answer
    /// a gate. Built once from definitions and holding no state of its own.
    ///
    /// <b>The reverse index is what lets an effect live on the research alone.</b> A building does not
    /// name the research that opens it - the research names the building - so a gate asks here which
    /// researches name it. A building no research names is not gated at all.
    /// </summary>
    public sealed class ResearchCatalog
    {
        static readonly ResearchDefinition[] None = System.Array.Empty<ResearchDefinition>();

        readonly List<ResearchDefinition> _all = new List<ResearchDefinition>();
        readonly Dictionary<string, ResearchDefinition> _byId = new Dictionary<string, ResearchDefinition>();
        readonly Dictionary<BuildingDefinition, List<ResearchDefinition>> _unlockingBuilding = new Dictionary<BuildingDefinition, List<ResearchDefinition>>();
        readonly Dictionary<RecipeDefinition, List<ResearchDefinition>> _unlockingRecipe = new Dictionary<RecipeDefinition, List<ResearchDefinition>>();

        /// <summary>Indexes each research once, by its first appearance; null entries and empty ids are skipped.</summary>
        public ResearchCatalog(IEnumerable<ResearchDefinition> researches)
        {
            if (researches == null) return;

            foreach (ResearchDefinition research in researches)
            {
                if (research == null || string.IsNullOrEmpty(research.Id) || _byId.ContainsKey(research.Id)) continue;

                _byId.Add(research.Id, research);
                _all.Add(research);

                IReadOnlyList<ResearchEffect> effects = research.Effects;
                for (int i = 0; i < effects.Count; i++)
                {
                    ResearchEffect effect = effects[i];
                    if (effect.Kind == ResearchEffectKind.UnlockBuilding && effect.Building != null) Index(_unlockingBuilding, effect.Building, research);
                    else if (effect.Kind == ResearchEffectKind.UnlockRecipe && effect.Recipe != null) Index(_unlockingRecipe, effect.Recipe, research);
                }
            }
        }

        /// <summary>Every research indexed, each once, in the order given.</summary>
        public IReadOnlyList<ResearchDefinition> All => _all;

        /// <summary>The research behind an unlock id, or null when none is known by it.</summary>
        public ResearchDefinition Get(string researchId)
            => researchId != null && _byId.TryGetValue(researchId, out ResearchDefinition research) ? research : null;

        /// <summary>Every research whose effects unlock this building - empty when none does, which means it is not gated.</summary>
        public IReadOnlyList<ResearchDefinition> UnlockersOf(BuildingDefinition building)
            => building != null && _unlockingBuilding.TryGetValue(building, out List<ResearchDefinition> list) ? list : (IReadOnlyList<ResearchDefinition>)None;

        /// <summary>Every research whose effects unlock this recipe - empty when none does, which means it is not gated.</summary>
        public IReadOnlyList<ResearchDefinition> UnlockersOf(RecipeDefinition recipe)
            => recipe != null && _unlockingRecipe.TryGetValue(recipe, out List<ResearchDefinition> list) ? list : (IReadOnlyList<ResearchDefinition>)None;

        /// <summary>
        /// The Core's furthest reach: the highest ActionRadius target any research here carries, or
        /// <paramref name="startingRadius"/> when none goes further. Derived, never written down a
        /// second time - world generation keeps derived ore out of it and the ground coverage sizes
        /// its texture on it, so a research with a bigger radius moves both with it.
        /// </summary>
        public int HighestActionRadius(int startingRadius)
        {
            int highest = startingRadius;
            for (int r = 0; r < _all.Count; r++)
            {
                IReadOnlyList<ResearchEffect> effects = _all[r].Effects;
                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i].Kind == ResearchEffectKind.ActionRadius && effects[i].Value > highest) highest = effects[i].Value;
                }
            }
            return highest;
        }

        static void Index<TKey>(Dictionary<TKey, List<ResearchDefinition>> index, TKey key, ResearchDefinition research)
        {
            if (!index.TryGetValue(key, out List<ResearchDefinition> list)) index[key] = list = new List<ResearchDefinition>();
            if (!list.Contains(research)) list.Add(research);
        }
    }
}

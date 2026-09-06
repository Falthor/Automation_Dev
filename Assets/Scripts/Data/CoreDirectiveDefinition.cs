using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// One thing the Core asks the player for: a bill of materials to hand over, and what handing it
    /// over opens up.
    ///
    /// Deliberately not a research and not a recipe. A research is bought with CU and chosen from a
    /// tree; a directive is a delivery the Core requests, validated by the player and physically
    /// carried by the builder robots. What it grants, though, is an ordinary unlock id
    /// (ResearchSystem.Grant), so recipe and building gates need no second notion of "available".
    ///
    /// Directives are ordered by CoreDirectiveDatabase and consumed one at a time - the Core panel
    /// shows the current one and nothing at all once they run out, which is what "son contenu
    /// devra evoluer" means: adding the next chapter is adding an asset, not writing a panel.
    /// </summary>
    [CreateAssetMenu(fileName = "CoreDirectiveDefinition", menuName = "Game/Core/Directive Definition")]
    public sealed class CoreDirectiveDefinition : ScriptableObject
    {
        [SerializeField] string id;

        [Tooltip("What the Core asks for. Each entry is shown as a big icon with stock/target underneath.")]
        [SerializeField] RecipeIngredient[] requirements = System.Array.Empty<RecipeIngredient>();

        [Tooltip("Shown on the REWARD line - what the player gets out of it, in one icon.")]
        [SerializeField] ItemDefinition rewardItem;

        [Tooltip("The unlock granted on completion, through ResearchSystem.Grant. Referenced by whatever it opens (e.g. Gear_Recipe.unlockResearch) exactly like a real research.")]
        [SerializeField] ResearchDefinition grants;

        [Tooltip("Also hands the player the Research menu itself (Top Bar card + Bottom Nav icon). Not a research id: what it opens is a menu, and no research can be its own prerequisite.")]
        [SerializeField] bool unlocksResearchMenu;

        [Tooltip("What the REWARD line says when there is no reward item - e.g. \"Deverrouille Datacenter\". Left empty, a directive that grants an unlock says so in general terms.")]
        [SerializeField] string rewardLabel;

        public string Id => id;
        public RecipeIngredient[] Requirements => requirements;
        public ItemDefinition RewardItem => rewardItem;
        public ResearchDefinition Grants => grants;

        /// <summary>
        /// Whether completing this directive opens the Research menu. Research is not a thing the
        /// player starts the run with - it is the first directive's own reward, alongside the item
        /// on the REWARD line - so the menu has to be gated by something, and a research id cannot
        /// gate the menu that buys research ids.
        /// </summary>
        public bool UnlocksResearchMenu => unlocksResearchMenu;

        /// <summary>
        /// How to word this directive's reward when no item stands for it, or empty to let the panel
        /// word it generically.
        ///
        /// Written here rather than taken from the granted unlock's own name, because the two are not
        /// the same sentence: one directive opens three researches at once and can only be described
        /// in general, another opens exactly one and should name it. The unlock's name has its own
        /// job - it is what the research tree prints as a missing prerequisite.
        /// </summary>
        public string RewardLabel => rewardLabel;
    }
}

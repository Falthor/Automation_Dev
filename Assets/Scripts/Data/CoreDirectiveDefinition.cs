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

        [Tooltip("The unlock granted on completion, through ResearchSystem.Grant. What it opens is that research's own effects (ResearchDefinition.Effects), exactly like a research in the tree.")]
        [SerializeField] ResearchDefinition grants;

        [Tooltip("What this directive opens, one entry per line of the REWARD block. Left empty, a directive that grants an unlock says so in general terms.")]
        [SerializeField] string[] rewardLabels = System.Array.Empty<string>();

        public string Id => id;
        public RecipeIngredient[] Requirements => requirements;
        public ItemDefinition RewardItem => rewardItem;
        public ResearchDefinition Grants => grants;

        /// <summary>
        /// What this directive opens, one entry per line, or empty to let the panel word it
        /// generically.
        ///
        /// <b>A list rather than a sentence.</b> A directive grants one unlock id but that id can
        /// open several things at once, and naming them in one string ran off the edge of the panel
        /// as soon as it opened more than two. Written here rather than taken from the granted
        /// unlock's own name: the unlock's name has its own job - it is what the research tree
        /// prints as a missing prerequisite - and it names the directive, not its contents.
        /// </summary>
        public string[] RewardLabels => rewardLabels;
    }
}

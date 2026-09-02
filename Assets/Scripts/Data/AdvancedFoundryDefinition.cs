using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Advanced Foundry: smelts Steel via one fixed recipe (no player choice).</summary>
    [CreateAssetMenu(fileName = "AdvancedFoundryDefinition", menuName = "Game/Buildings/Advanced Foundry Definition")]
    public sealed class AdvancedFoundryDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int maxStackPerItem = 100;
        [SerializeField, Min(0f)] float powerDemandKw = 4f;
        [SerializeField] string[] recipeIds = { "Steel" };
        [SerializeField] string[] acceptedItemIds = { "iron_ore", "Coal_ore" };

        public int MaxStackPerItem => maxStackPerItem;
        public override float PowerDemandKw => powerDemandKw;
        public string[] RecipeIds => recipeIds;
        public string[] AcceptedItemIds => acceptedItemIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
    }
}

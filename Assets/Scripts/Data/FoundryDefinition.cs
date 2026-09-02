using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Foundry: smelts ore into ingots via the shared production
    /// contract (CONTRACTS.md §6). One fixed output side; the other sides accept ore deliveries
    /// subject to an intake cooldown between deliveries (matches the source project exactly).
    /// </summary>
    [CreateAssetMenu(fileName = "FoundryDefinition", menuName = "Game/Buildings/Foundry Definition")]
    public sealed class FoundryDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int maxStackPerItem = 20;
        [SerializeField, Min(0f)] float powerDemandKw = 2f;
        [SerializeField, Min(0f)] float intakeIntervalSeconds = 2f;
        [SerializeField] string[] recipeIds = { "Iron_Ingot", "copper_Ingot" };

        public int MaxStackPerItem => maxStackPerItem;
        public override float PowerDemandKw => powerDemandKw;
        public float IntakeIntervalSeconds => intakeIntervalSeconds;
        public string[] RecipeIds => recipeIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
    }
}

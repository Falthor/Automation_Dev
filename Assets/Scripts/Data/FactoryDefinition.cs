using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Factory: crafts tier-1/tier-2 intermediate components via the
    /// shared production contract (CONTRACTS.md §6), player-selected among its recipe list.
    /// </summary>
    [CreateAssetMenu(fileName = "FactoryDefinition", menuName = "Game/Buildings/Factory Definition")]
    public sealed class FactoryDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int maxStackPerItem = 100;
        [SerializeField, Min(0f)] float powerDemandKw = 3f;
        [SerializeField] string[] recipeIds = { "copper_wire", "Gear", "Screw", "Iron_Plate", "Printed_Circuit_Board", "Data_Card", "Memory_MK1" };
        [SerializeField] string[] acceptedItemIds = { "Iron_Ingot", "copper_Ingot", "Iron_Plate", "copper_wire" };

        public int MaxStackPerItem => maxStackPerItem;
        public override float PowerDemandKw => powerDemandKw;
        public string[] RecipeIds => recipeIds;
        public string[] AcceptedItemIds => acceptedItemIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
    }
}

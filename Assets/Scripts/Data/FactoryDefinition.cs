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
        [SerializeField, Min(0f)] float powerDemandKw = 3f;
        [SerializeField] string[] recipeIds = { "magnet", "electrical_mechanism", "electric_motor", "control_unit", "cpu_mkI", "Memory_MK1" };

        // No ingots: the Factory makes no first stage at all, so an ingot would be an input nothing
        // here can spend - a belt of them would fill the buffer and stall the building. Everything
        // listed is consumed by one of the four recipes above.
        [SerializeField] string[] acceptedItemIds = { "iron_rod", "copper_wire", "Gear", "copper_coil", "magnet", "electrical_mechanism", "control_unit" };

        public override float PowerDemandKw => powerDemandKw;
        public string[] RecipeIds => recipeIds;
        public string[] AcceptedItemIds => acceptedItemIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
    }
}

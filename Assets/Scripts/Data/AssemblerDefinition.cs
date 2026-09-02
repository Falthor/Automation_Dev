using UnityEngine;

namespace Game.Data
{
    /// <summary>Static definition of the Assembler: crafts cpu_mkI/mechanical_component. Placement itself is gated by UnlockResearch (base class), not a recipe-level gate.</summary>
    [CreateAssetMenu(fileName = "AssemblerDefinition", menuName = "Game/Buildings/Assembler Definition")]
    public sealed class AssemblerDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int maxStackPerItem = 100;
        [SerializeField, Min(0f)] float powerDemandKw = 4f;
        [SerializeField] string[] recipeIds = { "cpu_mkI", "mechanical_component" };
        [SerializeField] string[] acceptedItemIds = { "copper_Ingot", "Gear", "Printed_Circuit_Board", "cpu_mkI", "Memory_MK1", "Iron_Plate" };

        public int MaxStackPerItem => maxStackPerItem;
        public override float PowerDemandKw => powerDemandKw;
        public string[] RecipeIds => recipeIds;
        public string[] AcceptedItemIds => acceptedItemIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
    }
}

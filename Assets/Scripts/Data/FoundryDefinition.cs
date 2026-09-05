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
        [SerializeField, Min(0f)] float intakeIntervalSeconds = 1f;
        [SerializeField] string[] recipeIds = { "Iron_Ingot", "copper_Ingot" };

        public int MaxStackPerItem => maxStackPerItem;
        public override float PowerDemandKw => powerDemandKw;
        public float IntakeIntervalSeconds => intakeIntervalSeconds;
        public string[] RecipeIds => recipeIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;

        // Compensates for the transparent margin around the art, so what is DRAWN fills the
        // footprint's cell edges rather than falling short of them. It is therefore a property of
        // the current art file and has to be re-measured whenever that file changes: it is
        // 1 / (opaque width as a fraction of the frame).
        //
        // Building_foundry_Spirite_v4.png: the opaque box is 504 of 512 px, so 512/504 = 1.0159.
        // The previous 1.09 was measured the same way against v3, whose margins were 21 px a side;
        // left in place over v4 it drew the building 7% wider than its own footprint.
        //
        // Not the same rationale as ConveyorDefinition/Splitter/Crossroad, whose overscan
        // deliberately pushes their arms INTO the neighbouring cell to close a seam - theirs is not
        // a margin measurement and must not be "corrected" to one.
        public override float RenderOverscan => 1.016f;
    }
}

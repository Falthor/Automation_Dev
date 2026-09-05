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
        // Building_foundry_Spirite_v4.png (in use): the opaque box is 397 of 512 px wide, so
        // 512/397 = 1.2896. Earlier values, each measured against the sheet of the day, for whenever
        // one of them comes back: Building_Foundry_v3.png 470/512 = 1.0894, and the first
        // 512x640 cut of the v4 sheet 504/512 = 1.016.
        //
        // This sheet's art is portrait - 397 x 494 opaque - on a square footprint. Fitting it is
        // fitting the WIDTH: the uniform fit then draws it 3.73 cells tall, overhanging the row
        // above and below by about a third of a cell each. That is the art's own aspect ratio, not a
        // sizing error, and squashing it to the footprint is exactly what FitSpriteUniform exists to
        // refuse.
        //
        // Not the same rationale as ConveyorDefinition/Splitter/Crossroad, whose overscan
        // deliberately pushes their arms INTO the neighbouring cell to close a seam - theirs is not
        // a margin measurement and must not be "corrected" to one.
        public override float RenderOverscan => 1.2896f;
    }
}

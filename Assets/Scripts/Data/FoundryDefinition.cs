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
        [SerializeField, Min(0f)] float powerDemandKw = 2f;
        [SerializeField, Min(0f)] float intakeIntervalSeconds = 1f;
        [SerializeField] string[] recipeIds = { "Iron_Ingot", "copper_Ingot" };

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
        // Building_Foundry_v3.png (in use): the opaque box is 470 of 512 px wide, so 512/470 = 1.0894.
        // Every value measured so far, so whichever sheet comes back brings its constant with it:
        //   Building_Foundry_v3.png            470/512 -> 1.0894
        //   Building_foundry_Spirite_v4.png    397/512 -> 1.2896  (the 2048x1536 export, portrait art)
        //   the first 512x640 cut of that v4   504/512 -> 1.016
        //
        // Getting this wrong is not subtle and has happened three times: leaving v3's 1.09 over the
        // 504/512 sheet drew the building 7% wider than its own footprint.
        //
        // Not the same rationale as ConveyorDefinition/Splitter/Crossroad, whose overscan
        // deliberately pushes their arms INTO the neighbouring cell to close a seam - theirs is not
        // a margin measurement and must not be "corrected" to one.
        public override float RenderOverscan => 1.0894f;
    }
}

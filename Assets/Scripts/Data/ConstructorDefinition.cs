using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Constructor: crafts the same intermediate components the Factory
    /// does, through the shared production contract (CONTRACTS.md §6), on a 2x2 footprint.
    ///
    /// <b>Two arrows and no more</b> - one output side, and one input side chosen at placement with
    /// <c>T</c> (see <see cref="HasSingleInputArrow"/>). It accepts on that one side and nowhere
    /// else.
    ///
    /// Its art is taller than its ground, so it carries an <c>artCellSize</c> rather than being
    /// fitted to the footprint - see BuildingDefinition.ArtCellSize.
    ///
    /// <b>And it is the one building drawn at that box rather than fitted into it</b>
    /// (<see cref="StretchArtToBox"/>): 2x3 cells is 64x96 px, while a 512x640 frame at 64 wide is
    /// 80 tall. The 20% the art gains in height is a deliberate choice about how the building should
    /// read, not a property of the file.
    /// </summary>
    [CreateAssetMenu(fileName = "ConstructorDefinition", menuName = "Game/Buildings/Constructor Definition")]
    public sealed class ConstructorDefinition : BuildingDefinition
    {
        [SerializeField, Min(0f)] float powerDemandKw = 3f;
        [SerializeField] string[] recipeIds = { "Iron_Plate", "copper_wire", "Screw" };
        [SerializeField] string[] acceptedItemIds = { "Iron_Ingot", "copper_Ingot" };

        public override float PowerDemandKw => powerDemandKw;
        public string[] RecipeIds => recipeIds;
        public string[] AcceptedItemIds => acceptedItemIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
        public override bool HasSingleInputArrow => true;

        public override bool StretchArtToBox => true;
    }
}

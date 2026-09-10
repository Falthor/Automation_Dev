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
    /// Its frames are square (512x512), so its art needs no <c>artCellSize</c> of its own: the 2x2
    /// footprint already carries the right proportion and the sprite lands exactly on it.
    /// </summary>
    [CreateAssetMenu(fileName = "ConstructorDefinition", menuName = "Game/Buildings/Constructor Definition")]
    public sealed class ConstructorDefinition : BuildingDefinition
    {
        [SerializeField, Min(0f)] float powerDemandKw = 3f;
        [SerializeField] string[] recipeIds = { "Iron_Plate", "copper_wire", "Screw", "copper_plate", "Cable" };

        // Plates and wire as well as ingots: a Screw is made from a plate and a Cable from wire, so
        // the Constructor takes its own output back as an ingredient.
        [SerializeField] string[] acceptedItemIds = { "Iron_Ingot", "copper_Ingot", "Iron_Plate", "copper_wire" };

        public override float PowerDemandKw => powerDemandKw;
        public string[] RecipeIds => recipeIds;
        public string[] AcceptedItemIds => acceptedItemIds;

        public override bool HasOutputArrow => true;
        public override bool HasInputArrows => true;
        public override bool HasSingleInputArrow => true;
    }
}

using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Static definition of the Core building: automatically placed once by world generation
    /// at the start of a game, never by the player. Its action radius is gameplay data (drives
    /// where world generation may place resource deposits), not just a visual.
    /// </summary>
    [CreateAssetMenu(fileName = "CoreDefinition", menuName = "Game/World/Core Definition")]
    public sealed class CoreDefinition : BuildingDefinition
    {
        [SerializeField, Min(1)] int actionRadiusCells = 50;
        [SerializeField, Min(0f)] float cuOutput = 3000f;
        [SerializeField, Min(0f)] float powerOutputKw = 20f;
        [SerializeField] RecipeIngredient[] startingStock = System.Array.Empty<RecipeIngredient>();

        public int ActionRadiusCells => actionRadiusCells;

        /// <summary>Permanent CU/s supply, no cable/network needed - reported unconditionally every tick.</summary>
        public float CuOutput => cuOutput;

        /// <summary>Permanent Power supply, same unconditional per-tick report as CuOutput.</summary>
        public float PowerOutputKw => powerOutputKw;

        /// <summary>Bootstraps the first constructions - added to Core's own inventory once at world generation.</summary>
        public RecipeIngredient[] StartingStock => startingStock;
    }
}

using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Deterministic world-content generation parameters: which Core and ore deposit
    /// definitions to place at game start, the seed driving deposit placement within the
    /// Core's action radius, and the items the player starts the game owning. Separate from
    /// TerrainGenerationSettings, which only governs the ground terrain type per cell.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Game/World/Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [SerializeField] CoreDefinition coreDefinition;
        [SerializeField] OreDepositDefinition ironOreDefinition;
        [SerializeField] OreDepositDefinition copperOreDefinition;
        [SerializeField] OreDepositDefinition coalOreDefinition;
        [SerializeField] int resourceSeed;

        [Header("Player starting stock (global, not held by any building)")]
        [SerializeField] RecipeIngredient[] startingStock = System.Array.Empty<RecipeIngredient>();

        public CoreDefinition CoreDefinition => coreDefinition;
        public OreDepositDefinition IronOreDefinition => ironOreDefinition;
        public OreDepositDefinition CopperOreDefinition => copperOreDefinition;
        public OreDepositDefinition CoalOreDefinition => coalOreDefinition;
        public int ResourceSeed => resourceSeed;

        /// <summary>Items the player owns at game start, seeded into GameRuntime's global stock - not into any building's inventory.</summary>
        public RecipeIngredient[] StartingStock => startingStock;
    }
}

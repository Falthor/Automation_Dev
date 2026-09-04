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

        [Header("Player starting stock - held physically in the Core Storage fixture below, not a building-less pool")]
        [SerializeField] RecipeIngredient[] startingStock = System.Array.Empty<RecipeIngredient>();

        [Header("Core Storage - a fixture placed one cell south of the Core at world generation, holding StartingStock")]
        [SerializeField] StorageDefinition coreStorageDefinition;

        public CoreDefinition CoreDefinition => coreDefinition;
        public OreDepositDefinition IronOreDefinition => ironOreDefinition;
        public OreDepositDefinition CopperOreDefinition => copperOreDefinition;
        public OreDepositDefinition CoalOreDefinition => coalOreDefinition;
        public int ResourceSeed => resourceSeed;

        /// <summary>Items the player owns at game start, seeded into the Core Storage fixture (CoreStorageDefinition), not into a building-less pool - the Core itself never accepts anything.</summary>
        public RecipeIngredient[] StartingStock => startingStock;

        /// <summary>Definition for the fixture WorldGenerator places one cell south of the Core and seeds with StartingStock. Null skips creating it (e.g. an older settings asset, or a test that doesn't need it).</summary>
        public StorageDefinition CoreStorageDefinition => coreStorageDefinition;
    }
}

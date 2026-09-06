using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// World-content generation parameters: which Core and ore deposit definitions to place at
    /// game start, how deposit placement is seeded, and the items the player starts the game
    /// owning. Separate from TerrainGenerationSettings, which only governs the ground terrain
    /// type per cell.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldGenerationSettings", menuName = "Game/World/Generation Settings")]
    public sealed class WorldGenerationSettings : ScriptableObject
    {
        [SerializeField] CoreDefinition coreDefinition;
        [SerializeField] OreDepositDefinition ironOreDefinition;
        [SerializeField] OreDepositDefinition copperOreDefinition;
        [SerializeField] OreDepositDefinition coalOreDefinition;

        /// <summary>
        /// On: every new game draws its own seed, so the ore lands somewhere else each time. This is
        /// the shipping behaviour - placement was always random in shape and fixed in fact, which
        /// reads as no randomness at all to anyone who starts a second game.
        ///
        /// Off pins the layout to ResourceSeed below, for reproducing a bug or judging a placement
        /// rule twice over the same world. A loaded save is unaffected either way: deposits are
        /// saved cell by cell and restored from the file, never regenerated.
        /// </summary>
        [Header("Deposit placement")]
        [SerializeField] bool randomizeResourceSeed = true;

        /// <summary>The pinned seed, used only while RandomizeResourceSeed is off.</summary>
        [SerializeField] int resourceSeed;

        [Header("Player starting stock - held physically in the Core Storage fixture below, not a building-less pool")]
        [SerializeField] RecipeIngredient[] startingStock = System.Array.Empty<RecipeIngredient>();

        [Header("Core Storage - a fixture placed one cell south of the Core at world generation, holding StartingStock")]
        [SerializeField] StorageDefinition coreStorageDefinition;

        public CoreDefinition CoreDefinition => coreDefinition;
        public OreDepositDefinition IronOreDefinition => ironOreDefinition;
        public OreDepositDefinition CopperOreDefinition => copperOreDefinition;
        public OreDepositDefinition CoalOreDefinition => coalOreDefinition;

        public bool RandomizeResourceSeed => randomizeResourceSeed;

        /// <summary>The pinned seed. Read by WorldGenerator only when RandomizeResourceSeed is off - ask WorldGenerator.ResourceSeed for the one a given world was actually built from.</summary>
        public int ResourceSeed => resourceSeed;

        /// <summary>Items the player owns at game start, seeded into the Core Storage fixture (CoreStorageDefinition), not into a building-less pool - the Core itself never accepts anything.</summary>
        public RecipeIngredient[] StartingStock => startingStock;

        /// <summary>Definition for the fixture WorldGenerator places one cell south of the Core and seeds with StartingStock. Null skips creating it (e.g. an older settings asset, or a test that doesn't need it).</summary>
        public StorageDefinition CoreStorageDefinition => coreStorageDefinition;
    }
}

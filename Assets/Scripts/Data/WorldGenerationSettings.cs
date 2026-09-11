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

        [Header("Débris")]

        /// <summary>
        /// The three wreck sprites. Drawn from freely - the same one may appear more than once, which
        /// is what keeps eight wrecks from reading as a set of eight different things.
        /// </summary>
        [SerializeField] Sprite[] wreckSprites = System.Array.Empty<Sprite>();

        /// <summary>
        /// The rings the wrecks are spread over, innermost first - see <see cref="WreckRingProfile"/>
        /// for why rings rather than a density.
        ///
        /// The innermost is deliberately tight: a robot always starts there, so it crosses one almost
        /// at once. The outer band is wide enough that its four are a long-term prospect.
        /// </summary>
        [SerializeField] WreckRing[] wreckRings =
        {
            new WreckRing(40f, 75f, 2),
            new WreckRing(75f, 160f, 2),
            new WreckRing(160f, 330f, 4)
        };

        /// <summary>
        /// <b>A development instrument, and it is meant to be deleted.</b> Finds every wreck the
        /// moment the world starts, so the spread of eight points over a 330-cell disc can be judged
        /// on the map without playing the three quarters of an hour the outer ring is tuned for.
        ///
        /// Off by default. Delete this field, the line that reads it in GameRuntime, and
        /// WreckField.DiscoverEverything together.
        /// </summary>
        [SerializeField] bool discoverEveryWreckAtStart;

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

        public Sprite[] WreckSprites => wreckSprites;
        public WreckRing[] WreckRings => wreckRings;
        public bool DiscoverEveryWreckAtStart => discoverEveryWreckAtStart;

        /// <summary>The rings as one value, with the derived angular separation they imply.</summary>
        public WreckRingProfile WreckProfile => new WreckRingProfile(wreckRings);
    }
}

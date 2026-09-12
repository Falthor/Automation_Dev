using Game.Core;

namespace Game.Grid
{
    /// <summary>
    /// Authoritative per-cell terrain type, a pure function of the seed and the coordinate
    /// (same seed + same parameters = same result, per DEVELOPMENT_RULES.md).
    ///
    /// Nothing is materialised: a cell's type is computed when asked. That is what makes the map's
    /// size cost nothing to hold, and what makes the order cells are asked about irrelevant.
    /// Takes plain parameters rather than the settings asset they come from, so a world is built
    /// from the numbers the save carries rather than from whatever the asset holds now - editing it
    /// between two sessions must not regenerate the ground under a base already placed.
    /// </summary>
    public sealed class TerrainRuntime
    {
        /// <summary>Distinct salts so the two offsets are independent draws rather than the same number twice.</summary>
        const uint OffsetXSalt = 0x9E3779B9;
        const uint OffsetYSalt = 0x85EBCA6B;

        readonly float _offsetX;
        readonly float _offsetY;

        public int Size { get; }

        /// <summary>
        /// The world's seed, kept rather than only consumed.
        ///
        /// It is the one seed that survives a save (SaveData.TerrainSeed), which makes it the only
        /// honest source for anything that must derive the same result in a loaded game as in the
        /// one it was saved from - the sectors' names, risk and contents (SectorCatalog) read it for
        /// exactly that reason. WorldGenerator.ResourceSeed cannot serve: it is not persisted, and
        /// its own summary says it is meaningless after a restore.
        /// </summary>
        public int Seed { get; }

        public float TerrainScale { get; }
        public float Proportion { get; }

        public TerrainRuntime(int size, int seed, float terrainScale, float proportion)
        {
            Size = size;
            Seed = seed;
            TerrainScale = terrainScale;
            Proportion = proportion;

            // NOT System.Random. It has no guarantee of stability across runtime versions, and the
            // terrain is re-derived at every load rather than saved: the day a Unity upgrade changed
            // its sequence, every existing world would recompose underneath buildings that ARE saved,
            // and a player would find their base on ground they do not recognise. That was harmless
            // while the terrain was a stored array; making the terrain derived is what made it
            // payable, so it is paid here.
            //
            // Two independent draws, from two salts rather than two successive calls - there is no
            // sequence to advance.
            _offsetX = (float)(DeterministicHash.Unit(seed, 0, OffsetXSalt) * 1000.0);
            _offsetY = (float)(DeterministicHash.Unit(seed, 0, OffsetYSalt) * 1000.0);

        }

        /// <summary>
        /// Authoritative terrain type of a cell, computed on demand. Out-of-bounds cells are Base.
        ///
        /// <b>Nothing is stored</b>, which makes "the same region gives the same result whatever
        /// the order" unbreakable rather than merely respected: there is no order left to vary, and
        /// the three ways of losing that - consulting neighbours, placing something larger than a
        /// cell incrementally, advancing a generator's state - have nowhere to live.
        ///
        /// The cost is three Perlin samples per query instead of a lookup, and nothing queries this
        /// today. A per-chunk cache added later must be filled <i>from</i> this function, so purity
        /// survives the optimisation.
        /// </summary>
        public TerrainType GetTerrainType(GridCoord cell)
        {
            if (cell.X < 0 || cell.X >= Size || cell.Y < 0 || cell.Y >= Size)
            {
                return TerrainType.Base;
            }

            return SampleContinuous(cell.X, cell.Y) < Proportion ? TerrainType.Top : TerrainType.Base;
        }

        /// <summary>
        /// Raw fBm value (3 octaves, weights 0.6/0.3/0.1 at frequencies 1/2.1/4.3) at a
        /// fractional cell-space coordinate. Exposed so Presentation can rebuild a
        /// higher-resolution mask for a smooth visual border while staying derived from the
        /// exact same deterministic function that produced the authoritative per-cell type.
        /// </summary>
        public float SampleContinuous(float cellX, float cellY)
        {
            float x = cellX + _offsetX;
            float y = cellY + _offsetY;

            float n1 = UnityEngine.Mathf.PerlinNoise(x / TerrainScale * 1f, y / TerrainScale * 1f);
            float n2 = UnityEngine.Mathf.PerlinNoise(x / TerrainScale * 2.1f, y / TerrainScale * 2.1f);
            float n3 = UnityEngine.Mathf.PerlinNoise(x / TerrainScale * 4.3f, y / TerrainScale * 4.3f);
            return 0.6f * n1 + 0.3f * n2 + 0.1f * n3;
        }
    }
}

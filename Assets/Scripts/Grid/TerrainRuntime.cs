using Game.Core;

namespace Game.Grid
{
    /// <summary>
    /// Authoritative per-cell terrain type, a pure function of the seed and the coordinate
    /// (same seed + same parameters = same result, per DEVELOPMENT_RULES.md §7).
    ///
    /// Nothing is materialised: a cell's type is computed when asked. That is what makes the map's
    /// size cost nothing to hold, and what makes the order cells are asked about irrelevant.
    /// Takes plain parameters rather than a Game.Data settings asset - Game.Grid must not
    /// depend on Game.Data (both are peers under Game.Core in the assembly graph); the caller
    /// (Game.Presentation, which already depends on Data) unpacks the settings asset.
    /// </summary>
    public sealed class TerrainRuntime
    {
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

            var rng = new System.Random(seed);
            _offsetX = (float)(rng.NextDouble() * 1000.0);
            _offsetY = (float)(rng.NextDouble() * 1000.0);

        }

        /// <summary>
        /// Authoritative terrain type of a cell, computed on demand. Out-of-bounds cells are Base.
        ///
        /// <b>Nothing is stored.</b> This used to fill a TerrainType[size, size] at construction -
        /// 90 000 entries on the current map, 100 million on the 10 000-cell one it is heading
        /// towards, none of which any gameplay rule reads yet (see TERRAIN.md §1).
        ///
        /// Removing the array is not only cheaper, it makes the property the large-map directive
        /// asks for <b>unbreakable rather than merely respected</b>: "the same region generated in
        /// any order gives the same result" is true because there is no order left to vary. The
        /// three ways of losing that property - looking at neighbours, placing something larger
        /// than a cell incrementally, advancing a generator's internal state - are not disciplines
        /// to keep here, they have nowhere to live.
        ///
        /// The cost moved from memory to arithmetic: three Perlin samples per query instead of a
        /// lookup. Nothing queries this today. If a hot per-frame consumer ever appears, cache it
        /// per chunk the way DiscoveryRuntime does - and keep this function as the thing the cache
        /// is filled from, so purity survives the optimisation.
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

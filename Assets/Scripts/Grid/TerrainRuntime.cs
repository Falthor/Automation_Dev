using Game.Core;

namespace Game.Grid
{
    /// <summary>
    /// Authoritative per-cell terrain type, generated once and deterministically from a seed
    /// (same seed + same parameters = same result, per DEVELOPMENT_RULES.md §7).
    /// Takes plain parameters rather than a Game.Data settings asset - Game.Grid must not
    /// depend on Game.Data (both are peers under Game.Core in the assembly graph); the caller
    /// (Game.Presentation, which already depends on Data) unpacks the settings asset.
    /// </summary>
    public sealed class TerrainRuntime
    {
        readonly TerrainType[,] _cells;
        readonly float _offsetX;
        readonly float _offsetY;

        public int Size { get; }
        public float TerrainScale { get; }
        public float Proportion { get; }

        public TerrainRuntime(int size, int seed, float terrainScale, float proportion)
        {
            Size = size;
            TerrainScale = terrainScale;
            Proportion = proportion;

            var rng = new System.Random(seed);
            _offsetX = (float)(rng.NextDouble() * 1000.0);
            _offsetY = (float)(rng.NextDouble() * 1000.0);

            _cells = new TerrainType[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float value = SampleContinuous(x, y);
                    _cells[x, y] = value < proportion ? TerrainType.Top : TerrainType.Base;
                }
            }
        }

        /// <summary>Authoritative terrain type of a cell. Out-of-bounds cells are treated as Base.</summary>
        public TerrainType GetTerrainType(GridCoord cell)
        {
            if (cell.X < 0 || cell.X >= Size || cell.Y < 0 || cell.Y >= Size)
            {
                return TerrainType.Base;
            }

            return _cells[cell.X, cell.Y];
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

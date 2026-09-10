using System.Collections.Generic;
using Game.Core;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The revealed terrain the zoomed-out map draws: <b>one tile per discovered chunk, one texel per
    /// cell, and nothing at all anywhere else.</b>
    ///
    /// <b>The chunk is the unit because it is the unit everywhere else.</b> Discovery storage already
    /// creates a chunk on first write and treats an absent one as unknown; this does exactly the same
    /// with pixels. A tile is 64x64 cells - 16 KB - so the introduction's handful of chunks costs a few
    /// tens of kilobytes, and fifty chunks of a well-explored run cost 800 KB.
    ///
    /// <b>The chunk is the unit rather than the sector, and memory is not why.</b> One texture for a
    /// 10 000-cell world is 400 MB per cell against 1.5 MB per sector, so the sector wins on
    /// arithmetic - and loses on everything else. A sector is 16 cells and a robot reveals a disc of
    /// radius 6: the revelation is smaller than the texel it would be painted into, so a whole
    /// 16-cell square takes one flat colour and the map reads as a grid of blocks. That is precisely
    /// what the design wants gone, and removing the drawn gridlines would have left the blocks
    /// without even the lines that explained them. Tiling by chunk keeps the memory bounded *and*
    /// draws the irregular patches a revelation actually has.
    ///
    /// <b>Unknown is not a colour.</b> A cell nobody has seen is transparent, so nothing is drawn there
    /// and the black behind is the absence of a map rather than a shape painted on one. An earlier
    /// version tinted it, because a sector had to be visible to be aimed at; missions are aimed at sites
    /// now, and sites are drawn on top - see the carnet.
    ///
    /// Rebuilding is guarded on <see cref="DiscoveryRuntime.Version"/> and then per chunk on its own
    /// stamp, so a frame that discovered nothing costs one integer comparison and a revelation costs the
    /// one chunk it landed in.
    /// </summary>
    public sealed class SectorMapImage
    {
        /// <summary>Revealed ground. One flat tone: this layer says "seen", and what is there is what the sites and the markers above it draw.</summary>
        static readonly Color32 DiscoveredColour = new Color32(116, 97, 74, 255);

        /// <summary>Not a colour at all. Nothing is drawn where nothing is known.</summary>
        static readonly Color32 AbsentColour = new Color32(0, 0, 0, 0);

        /// <summary>One discovered chunk's pixels, and where they belong in cell space.</summary>
        public sealed class Tile
        {
            public int ChunkIndex { get; }

            /// <summary>The chunk's lowest-left cell. Row 0 of the texture is this cell's row, so the texture is the right way up in a world where Y grows north.</summary>
            public GridCoord OriginCell { get; }

            public int SizeCells { get; }

            public Texture2D Texture { get; }

            internal Tile(int chunkIndex, GridCoord originCell, int sizeCells)
            {
                ChunkIndex = chunkIndex;
                OriginCell = originCell;
                SizeCells = sizeCells;

                Texture = new Texture2D(sizeCells, sizeCells, TextureFormat.RGBA32, false)
                {
                    name = $"SectorMapChunk{chunkIndex}",

                    // Point, so a cell stays a crisp square when the map is zoomed in. This is data
                    // being shown, not a field being sampled against a threshold - the fog's bilinear
                    // exists to turn a binary field into a boundary, and there is no boundary to cut here.
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
        }

        readonly SectorGrid _grid;
        readonly DiscoveryRuntime _discovery;

        readonly List<Tile> _tiles = new List<Tile>();
        readonly Dictionary<int, Tile> _tilesByChunk = new Dictionary<int, Tile>();

        /// <summary>The version each chunk's tile was last drawn at. As sparse as the discovery storage itself.</summary>
        readonly Dictionary<int, int> _builtChunkVersions = new Dictionary<int, int>();

        readonly List<int> _chunkScratch = new List<int>();

        /// <summary>One tile's worth of pixels, reused across every tile so a rebuild allocates nothing.</summary>
        readonly Color32[] _pixelScratch;

        /// <summary>The discovery version the tiles were last built from. Negative until the first build, which no real version equals.</summary>
        int _builtVersion = -1;

        public int ChunkSizeCells { get; }

        /// <summary>The discovered chunks, each with its own texture. The map draws one quad per entry and nothing between them.</summary>
        public IReadOnlyList<Tile> Tiles => _tiles;

        /// <summary>How many times a tile has actually been re-uploaded. Watched by a test: a refresh that discovered nothing must not upload.</summary>
        public int UploadCount { get; private set; }

        /// <summary>How many cells the last build looked at. The measure of whether "walk only what changed" is holding.</summary>
        public int LastVisitedCellCount { get; private set; }

        public SectorMapImage(SectorGrid grid, DiscoveryRuntime discovery)
        {
            _grid = grid;
            _discovery = discovery;

            ChunkSizeCells = Mathf.Max(1, discovery?.ChunkSizeCells ?? 64);
            _pixelScratch = new Color32[ChunkSizeCells * ChunkSizeCells];
        }

        /// <summary>
        /// Rebuilds the tiles whose chunk has moved since the last one, and answers whether anything
        /// changed. Safe to call every frame.
        /// </summary>
        public bool Refresh()
        {
            if (_grid == null || _discovery == null) return false;
            if (_discovery.Version == _builtVersion) return false;

            _builtVersion = _discovery.Version;
            return Build();
        }

        bool Build()
        {
            _chunkScratch.Clear();
            _discovery.CollectMaterialisedChunks(_chunkScratch);

            int visited = 0;
            bool uploaded = false;

            foreach (int chunkIndex in _chunkScratch)
            {
                int chunkVersion = _discovery.ChunkVersion(chunkIndex);
                if (_builtChunkVersions.TryGetValue(chunkIndex, out int built) && built == chunkVersion) continue;

                _builtChunkVersions[chunkIndex] = chunkVersion;

                Tile tile = TileFor(chunkIndex);
                visited += Paint(tile);
                uploaded = true;
            }

            LastVisitedCellCount = visited;
            if (uploaded) UploadCount++;
            return uploaded;
        }

        /// <summary>The chunk's tile, created on first sight of it - the same "materialise on first write" the discovery storage underneath already does.</summary>
        Tile TileFor(int chunkIndex)
        {
            if (_tilesByChunk.TryGetValue(chunkIndex, out Tile existing)) return existing;

            var origin = new GridCoord(
                chunkIndex % _discovery.ChunksPerAxis * ChunkSizeCells,
                chunkIndex / _discovery.ChunksPerAxis * ChunkSizeCells);

            var tile = new Tile(chunkIndex, origin, ChunkSizeCells);
            _tilesByChunk[chunkIndex] = tile;
            _tiles.Add(tile);
            return tile;
        }

        /// <summary>Writes one tile from the discovery state under it. Returns how many cells were looked at.</summary>
        int Paint(Tile tile)
        {
            int visited = 0;

            for (int y = 0; y < ChunkSizeCells; y++)
            {
                int row = y * ChunkSizeCells;
                for (int x = 0; x < ChunkSizeCells; x++)
                {
                    var cell = new GridCoord(tile.OriginCell.X + x, tile.OriginCell.Y + y);

                    // Cells past the map's edge stay absent: the tile is square even where the world
                    // is not, and there is nothing out there to have discovered.
                    _pixelScratch[row + x] = _discovery.IsDiscovered(cell) ? DiscoveredColour : AbsentColour;
                    visited++;
                }
            }

            tile.Texture.SetPixels32(_pixelScratch);
            tile.Texture.Apply(false, false);
            return visited;
        }

        /// <summary>What the image says about one cell: whether it draws anything there. For tests, and for anything reading the map without sampling a texture.</summary>
        public bool IsDrawnAt(GridCoord cell)
        {
            if (_discovery == null || !_discovery.Contains(cell)) return false;

            int chunkIndex = cell.Y / ChunkSizeCells * _discovery.ChunksPerAxis + cell.X / ChunkSizeCells;
            if (!_tilesByChunk.TryGetValue(chunkIndex, out Tile tile)) return false;

            Color32 pixel = tile.Texture.GetPixels32()[
                (cell.Y - tile.OriginCell.Y) * ChunkSizeCells + (cell.X - tile.OriginCell.X)];

            return pixel.a > 0;
        }

        public void Dispose()
        {
            foreach (Tile tile in _tiles)
            {
                if (tile.Texture == null) continue;

                if (Application.isPlaying) Object.Destroy(tile.Texture);
                else Object.DestroyImmediate(tile.Texture);
            }

            _tiles.Clear();
            _tilesByChunk.Clear();
            _builtChunkVersions.Clear();
        }
    }
}

using System.Collections.Generic;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// One texel per sector, for the whole map: what the zoomed-out map draws.
    ///
    /// <b>The whole map fits because a sector is the unit, not a cell.</b> A 10 000-cell map holds
    /// 390 625 sectors, so the image is 625 x 625 - 1.5 MB, against 400 MB for
    /// a texel per cell. It therefore does not need the camera-following window the fog texture
    /// needed; the panel showing it zooms into a rectangle of it instead, which costs nothing because
    /// the data is already all there.
    ///
    /// <b>Only materialised chunks are walked.</b> Discovery storage is sparse, and a chunk that has
    /// never been written to is unknown by construction - so walking the chunks that exist has walked
    /// everything there is to see. During the introduction that is a handful of chunks out of 24 649.
    /// Rebuilding is guarded on <see cref="DiscoveryRuntime.Version"/>, so a frame that discovered
    /// nothing costs one integer comparison.
    ///
    /// Sectors tile chunks exactly (SectorSettings pins it), so every sector belongs to exactly one
    /// chunk and no sector can be missed by walking chunks.
    /// </summary>
    public sealed class SectorMapImage
    {
        /// <summary>Texel values. Spread apart rather than 0/1/2 so the image is readable as a greyscale when inspected by hand.</summary>
        public const byte UnknownTexel = 0;
        public const byte PartialTexel = 128;
        public const byte DiscoveredTexel = 255;

        /// <summary>
        /// What each state is painted. **Colours rather than data, and deliberately.**
        ///
        /// This started as an R8 texture — one byte per sector, the shape data wants. But nothing
        /// samples it in a shader: its only consumer is a UI element that displays it directly, and a
        /// single-channel texture shown directly is bright red. Producing the colours here costs
        /// 1.5 MB instead of 390 KB on the shipped map, which is nothing, and removes the shader that
        /// would otherwise exist only to recolour three values.
        ///
        /// Undiscovered used to match the panel's own background exactly, so fog read as the absence
        /// of a map rather than as a black shape drawn on one. That was right while the map was only
        /// read, and wrong the moment it became the thing missions are aimed at: a sector you cannot
        /// see is a sector you cannot send anyone to, and the whole world outside the Core's disc
        /// simply was not there. It is now a shade above the background - present, plainly unvisited,
        /// and pointable at.
        /// </summary>
        static readonly Color32 UnknownColour = new Color32(30, 36, 44, 255);

        static readonly Color32 PartialColour = new Color32(74, 62, 48, 255);

        static readonly Color32 DiscoveredColour = new Color32(116, 97, 74, 255);

        readonly SectorGrid _grid;
        readonly DiscoveryRuntime _discovery;

        readonly byte[] _texels;

        /// <summary>What is actually uploaded. Parallel to the state array above, which stays the answer to "what does this sector read as".</summary>
        readonly Color32[] _pixels;
        readonly List<int> _chunkScratch = new List<int>();

        /// <summary>The version each chunk's sectors were last drawn at. As sparse as the discovery storage itself.</summary>
        readonly Dictionary<int, int> _builtChunkVersions = new Dictionary<int, int>();

        /// <summary>The discovery version the texture was last built from. Negative until the first build, which no real version equals.</summary>
        int _builtVersion = -1;

        /// <summary>Sectors along one axis - the image's own side, in texels.</summary>
        public int SizeSectors { get; }

        public Texture2D Texture { get; }

        /// <summary>How many times the texture has actually been re-uploaded. Watched by a test: a refresh that discovered nothing must not upload.</summary>
        public int UploadCount { get; private set; }

        /// <summary>How many sectors the last build looked at. The measure of whether "walk only what exists" is holding.</summary>
        public int LastVisitedSectorCount { get; private set; }

        public SectorMapImage(SectorGrid grid, DiscoveryRuntime discovery)
        {
            _grid = grid;
            _discovery = discovery;

            SizeSectors = Mathf.Max(1, grid?.Columns ?? 1);
            _texels = new byte[SizeSectors * SizeSectors];

            _pixels = new Color32[SizeSectors * SizeSectors];
            for (int i = 0; i < _pixels.Length; i++) _pixels[i] = UnknownColour;

            // RGBA32 and sRGB, unlike the fog's R8: this one is displayed, not sampled against a
            // threshold, so it *is* colour and wants the colour pipeline.
            Texture = new Texture2D(SizeSectors, SizeSectors, TextureFormat.RGBA32, false)
            {
                name = "SectorMap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        /// <summary>
        /// Rebuilds the image if discovery has moved since the last one, and answers whether it did.
        /// Safe to call every frame.
        /// </summary>
        public bool Refresh()
        {
            if (_grid == null || _discovery == null) return false;
            if (_discovery.Version == _builtVersion) return false;

            _builtVersion = _discovery.Version;
            Build();
            return true;
        }

        /// <summary>
        /// Rewrites the sectors of the chunks that have moved since the last build, and only those.
        ///
        /// <b>The cost must follow what changed, not what exists.</b> Walking every materialised chunk
        /// was measured at 18 ms on a modestly explored map and 154 ms on a well-explored one - a
        /// dropped frame every time a mission reveals a sector, growing with everything the player has
        /// ever seen. Each chunk carries the version it was last written at, so a rebuild touches the
        /// one chunk a revelation landed in.
        ///
        /// Texels are never cleared, because discovery is never taken away: a sector can only move
        /// from unknown towards discovered. A chunk's stamp is unique in time, so a restored save
        /// cannot be mistaken for the state already drawn.
        /// </summary>
        void Build()
        {
            _chunkScratch.Clear();
            _discovery.CollectMaterialisedChunks(_chunkScratch);

            int sectorsPerChunkAxis = Mathf.Max(1, _discovery.ChunkSizeCells / _grid.SectorSizeCells);
            int visited = 0;

            foreach (int chunkIndex in _chunkScratch)
            {
                int chunkVersion = _discovery.ChunkVersion(chunkIndex);
                if (_builtChunkVersions.TryGetValue(chunkIndex, out int built) && built == chunkVersion) continue;

                _builtChunkVersions[chunkIndex] = chunkVersion;

                int firstColumn = chunkIndex % _discovery.ChunksPerAxis * sectorsPerChunkAxis;
                int firstRow = chunkIndex / _discovery.ChunksPerAxis * sectorsPerChunkAxis;

                for (int row = firstRow; row < firstRow + sectorsPerChunkAxis; row++)
                {
                    for (int column = firstColumn; column < firstColumn + sectorsPerChunkAxis; column++)
                    {
                        int index = _grid.IndexAt(column, row);
                        if (index < 0) continue;   // the map's edge clips the last chunks

                        visited++;
                        SectorDiscovery state = _grid.DiscoveryOf(index, _discovery);
                        int texel = row * SizeSectors + column;

                        _texels[texel] = TexelFor(state);
                        _pixels[texel] = ColourFor(state);
                    }
                }
            }

            LastVisitedSectorCount = visited;
            if (visited == 0) return;   // the version moved somewhere this image does not draw

            Texture.SetPixels32(_pixels);
            Texture.Apply(false, false);
            UploadCount++;
        }

        static Color32 ColourFor(SectorDiscovery discovery)
        {
            switch (discovery)
            {
                case SectorDiscovery.Discovered: return DiscoveredColour;
                case SectorDiscovery.Partial: return PartialColour;
                default: return UnknownColour;
            }
        }

        static byte TexelFor(SectorDiscovery discovery)
        {
            switch (discovery)
            {
                case SectorDiscovery.Discovered: return DiscoveredTexel;
                case SectorDiscovery.Partial: return PartialTexel;
                default: return UnknownTexel;
            }
        }

        /// <summary>What the image currently says about one sector. For tests, and for anything reading the map without sampling a texture.</summary>
        public byte TexelAt(int column, int row)
        {
            if (column < 0 || row < 0 || column >= SizeSectors || row >= SizeSectors) return UnknownTexel;
            return _texels[row * SizeSectors + column];
        }

        public void Dispose()
        {
            if (Texture == null) return;

            if (Application.isPlaying) Object.Destroy(Texture);
            else Object.DestroyImmediate(Texture);
        }
    }
}

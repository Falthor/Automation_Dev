using System.Collections.Generic;
using System.Text;
using Game.Core;
using UnityEngine;

namespace Game.Grid
{
    /// <summary>
    /// What the player has discovered, one state per cell, authoritative.
    ///
    /// <b>Per cell, not per region.</b> A revelation of any shape can write here - a region-shaped
    /// one simply writes the cells of that region. The reverse would not hold: a per-region state
    /// would forbid every free-form revelation, and the shapes are not known yet.
    ///
    /// <b>This state is written, never derived.</b> The Core's action radius is one writer among
    /// others (a mission will be another), and once a cell is written it stays written - discovery is
    /// something the player has acquired, not a consequence of what the Core currently reaches. Read
    /// the other way round - recomputing a distance to the Core at read time - the fog would be a
    /// disc again and a mission would have nothing left to reveal. Nothing in here knows where the
    /// Core is, which is what keeps that mistake from being possible.
    ///
    /// <b>Stored per chunk, created on first write.</b> A chunk nobody has ever revealed a cell in
    /// does not exist, and answering "unknown" for its cells costs nothing - which is what keeps the
    /// memory proportional to what the player has seen instead of to the size of the map. That is an
    /// implementation detail and nothing else: every method below behaves exactly as it did over a
    /// flat array, the captured string is byte-for-byte the same, and no caller can tell.
    ///
    /// Lives in Game.Grid beside <see cref="TerrainRuntime"/>: it is per-cell world state of exactly
    /// the same shape, read by Presentation and written by Gameplay, so it belongs under both
    /// (PROJECT_ARCHITECTURE.md §7).
    /// </summary>
    public sealed class DiscoveryRuntime
    {
        /// <summary>Separates one run from the next in the captured form.</summary>
        const char RunSeparator = ',';

        /// <summary>Separates a run's state from its length.</summary>
        const char RunFieldSeparator = ':';

        /// <summary>
        /// One array per chunk that has ever been written to, and nothing at all for the rest.
        ///
        /// A flat array is full the moment it is allocated: 100 MB on a 10 000-cell map, for a map
        /// that will stay 99 % unknown for the whole run. Chunks make the cost follow what the player
        /// has actually seen instead of the size of the world.
        ///
        /// <b>An absent chunk is unknown, never discovered.</b> That is the one thing that must not
        /// be got backwards - the opposite default would reveal the entire map at once - and it is
        /// what every read below falls back to. A test pins it.
        ///
        /// This is storage, not contract: nothing outside this class can tell the difference, and the
        /// captured form is byte-for-byte what the flat array produced.
        /// </summary>
        readonly Dictionary<int, DiscoveryState[]> _chunks = new Dictionary<int, DiscoveryState[]>();

        /// <summary>Cells along one side of a chunk. Comes from SectorSettings, passed as a plain int - Game.Grid must not depend on Game.Data (see TerrainRuntime for the same reason).</summary>
        public int ChunkSizeCells { get; }

        /// <summary>Chunks along one axis, rounded up so a map that is not a whole number of chunks keeps its edge.</summary>
        readonly int _chunksPerAxis;

        public int Size { get; }

        /// <summary>
        /// Bumped once per call that actually changed something, and never otherwise.
        ///
        /// It exists for the renderer: a texture of one texel per cell must be re-uploaded only when
        /// the state has moved, and comparing this against the last uploaded value is the whole test.
        /// A call that reveals nothing new leaves it alone, so re-revealing the same disc every tick
        /// costs one comparison and no upload.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>How many chunks currently hold storage. For tests and reporting - it is the whole point of the sparse form, so it is worth being able to assert.</summary>
        public int MaterialisedChunkCount => _chunks.Count;

        /// <summary>Chunks along one axis. Needed by anything that turns a chunk index back into coordinates.</summary>
        public int ChunksPerAxis => _chunksPerAxis;

        /// <summary>
        /// The value <see cref="Version"/> held when a chunk was last written to, or 0 for a chunk
        /// never touched.
        ///
        /// <b>What lets a reader rebuild only what moved.</b> The global version says something
        /// changed somewhere; this says where. Without it, a renderer that walks the materialised
        /// chunks pays for everything the player has ever seen every time one cell is revealed -
        /// measured at 154 ms on a well-explored map, which is a dropped frame per revelation.
        ///
        /// Stamped with the global version rather than counted per chunk, so a stamp is unique in
        /// time: a cached value can never coincidentally match a later state, including after a
        /// RestoreState has replaced every chunk.
        /// </summary>
        public int ChunkVersion(int chunkIndex)
            => _chunkVersions.TryGetValue(chunkIndex, out int version) ? version : 0;

        /// <summary>When each chunk was last written to. Parallel to <see cref="_chunks"/> and just as sparse.</summary>
        readonly Dictionary<int, int> _chunkVersions = new Dictionary<int, int>();

        /// <summary>
        /// The chunks that hold storage, appended to a caller-owned list.
        ///
        /// <b>What makes drawing the whole map affordable.</b> Everything outside these is unknown by
        /// construction, so a renderer that walks them has walked everything there is to see - which
        /// on a 10 000-cell map is a handful of chunks rather than 24 649. A list rather than an
        /// enumerator so a refresh allocates nothing.
        /// </summary>
        public void CollectMaterialisedChunks(List<int> into)
        {
            if (into == null) return;
            foreach (var chunk in _chunks) into.Add(chunk.Key);
        }

        public DiscoveryRuntime(int size, int chunkSizeCells)
        {
            Size = Mathf.Max(0, size);
            ChunkSizeCells = Mathf.Max(1, chunkSizeCells);
            _chunksPerAxis = Mathf.CeilToInt(Size / (float)ChunkSizeCells);
        }

        /// <summary>The chunk a cell belongs to. Callers must have checked <see cref="Contains"/> first.</summary>
        int ChunkIndexOf(GridCoord cell) => cell.Y / ChunkSizeCells * _chunksPerAxis + cell.X / ChunkSizeCells;

        /// <summary>Where a cell sits inside its own chunk's array.</summary>
        int OffsetInChunk(GridCoord cell) => cell.Y % ChunkSizeCells * ChunkSizeCells + cell.X % ChunkSizeCells;

        /// <summary>The chunk's storage, or null when it has never been written to - which reads as wholly unknown.</summary>
        DiscoveryState[] ExistingChunk(GridCoord cell)
            => _chunks.TryGetValue(ChunkIndexOf(cell), out DiscoveryState[] chunk) ? chunk : null;

        /// <summary>
        /// The chunk's storage, allocating it if this is the first write there. Allocation happens
        /// only on a first write, so it is not a per-frame cost: re-revealing a disc already seen
        /// allocates nothing.
        /// </summary>
        DiscoveryState[] ChunkForWriting(GridCoord cell)
        {
            int index = ChunkIndexOf(cell);
            if (_chunks.TryGetValue(index, out DiscoveryState[] chunk)) return chunk;

            chunk = new DiscoveryState[ChunkSizeCells * ChunkSizeCells];
            _chunks[index] = chunk;
            return chunk;
        }

        /// <summary>
        /// The <b>stored</b> state of one cell: <see cref="DiscoveryState.Unknown"/> or
        /// <see cref="DiscoveryState.Remembered"/>, and never
        /// <see cref="DiscoveryState.Observed"/> - whether something is looking at this cell right
        /// now is not a fact about storage. <see cref="ObservationRuntime.StateOf"/> is what answers
        /// with all three.
        ///
        /// Out of bounds reads as Unknown - there is nothing out there to have discovered.
        /// </summary>
        public DiscoveryState GetState(GridCoord cell)
        {
            if (!Contains(cell)) return DiscoveryState.Unknown;

            DiscoveryState[] chunk = ExistingChunk(cell);
            return chunk == null ? DiscoveryState.Unknown : chunk[OffsetInChunk(cell)];
        }

        /// <summary>Whether this cell has ever been seen - which is what "discovered" means, observed or not. Asked as "not Unknown" rather than "is Remembered" so it stays the right question if another stored value is ever added.</summary>
        public bool IsDiscovered(GridCoord cell) => GetState(cell) != DiscoveryState.Unknown;

        public bool Contains(GridCoord cell)
            => cell.X >= 0 && cell.X < Size && cell.Y >= 0 && cell.Y < Size;

        /// <summary>
        /// The one place a cell is ever written, and therefore the one place a chunk can be recorded
        /// as having changed.
        ///
        /// <b>Single, because it was not.</b> Four paths wrote cells and each bumped
        /// <see cref="Version"/> on its own; when per-chunk stamps were added, three of them silently
        /// did not stamp - so the zoomed-out map bumped its version, found no chunk changed, and drew
        /// nothing at all. A reader cannot tell that apart from "nothing happened".
        ///
        /// Returns whether this changed anything, and remembers the chunk for <see cref="CommitWrites"/>.
        /// </summary>
        bool WriteDiscovered(GridCoord cell)
        {
            DiscoveryState[] chunk = ChunkForWriting(cell);
            int offset = OffsetInChunk(cell);
            if (chunk[offset] == DiscoveryState.Remembered) return false;

            chunk[offset] = DiscoveryState.Remembered;
            _touchedChunks.Add(ChunkIndexOf(cell));
            return true;
        }

        /// <summary>Closes a batch of writes: one version bump for the call, and a stamp on every chunk it touched. Doing both here is what keeps them from drifting apart.</summary>
        void CommitWrites(int revealed)
        {
            if (revealed > 0)
            {
                Version++;
                foreach (int chunkIndex in _touchedChunks) _chunkVersions[chunkIndex] = Version;
            }

            _touchedChunks.Clear();
        }

        /// <summary>Chunks written during the current call, waiting to be stamped. Reused, so a revelation allocates nothing.</summary>
        readonly HashSet<int> _touchedChunks = new HashSet<int>();

        /// <summary>Marks one cell discovered. Returns true only if that changed something, so a caller can tell a real revelation from a repeat.</summary>
        public bool Reveal(GridCoord cell)
        {
            if (!Contains(cell)) return false;

            bool revealed = WriteDiscovered(cell);
            CommitWrites(revealed ? 1 : 0);
            return revealed;
        }

        /// <summary>
        /// Marks every cell whose centre falls within <paramref name="radiusCells"/> of
        /// <paramref name="centerCells"/> discovered, and returns how many were not already.
        ///
        /// Takes a centre and a radius rather than the Core, because it must not know what a Core is:
        /// this is the shape being written, not who is writing it. Walks the disc's bounding box
        /// clamped to the map, so its cost follows the radius and not the size of the world, and
        /// allocates nothing - it is called from the simulation tick.
        /// </summary>
        public int RevealDisc(Vector2 centerCells, float radiusCells)
        {
            if (radiusCells <= 0f || Size == 0) return 0;

            int minX = Mathf.Max(0, Mathf.FloorToInt(centerCells.x - radiusCells));
            int maxX = Mathf.Min(Size - 1, Mathf.CeilToInt(centerCells.x + radiusCells));
            int minY = Mathf.Max(0, Mathf.FloorToInt(centerCells.y - radiusCells));
            int maxY = Mathf.Min(Size - 1, Mathf.CeilToInt(centerCells.y + radiusCells));

            float radiusSquared = radiusCells * radiusCells;
            int revealed = 0;

            for (int y = minY; y <= maxY; y++)
            {
                // The cell's centre, not its corner: a cell counts as reached when the disc covers
                // the middle of it, which is what makes the edge symmetric around the centre.
                float dy = y + 0.5f - centerCells.y;
                float dySquared = dy * dy;

                for (int x = minX; x <= maxX; x++)
                {
                    float dx = x + 0.5f - centerCells.x;
                    if (dx * dx + dySquared > radiusSquared) continue;

                    if (WriteDiscovered(new GridCoord(x, y))) revealed++;
                }
            }

            CommitWrites(revealed);
            return revealed;
        }

        /// <summary>Marks an arbitrary set of cells discovered - what a region-shaped revelation uses. Returns how many were not already discovered.</summary>
        public int RevealCells(IEnumerable<GridCoord> cells)
        {
            if (cells == null) return 0;

            int revealed = 0;
            foreach (GridCoord cell in cells)
            {
                if (!Contains(cell)) continue;
                if (WriteDiscovered(cell)) revealed++;
            }

            CommitWrites(revealed);
            return revealed;
        }

        /// <summary>How many cells are discovered. Walks the array - for tests and reporting, not for a per-frame read.</summary>
        public int DiscoveredCount()
        {
            int count = 0;
            foreach (DiscoveryState[] chunk in _chunks.Values)
            {
                for (int i = 0; i < chunk.Length; i++)
                {
                    if (chunk[i] == DiscoveryState.Remembered) count++;
                }
            }
            return count;
        }

        // ---- Save / Restore (CONTRACTS.md §14) ----

        /// <summary>
        /// The whole map as run-length pairs, `state:length` separated by commas, in row-major order.
        ///
        /// A plain string rather than a JObject so Game.Grid keeps its two references (Game.Core and
        /// Game.Data) and gains no dependency on the JSON library - the same shape as the other
        /// simple round-trips the save layer already uses (ComputeSystem.RestoreReserve,
        /// PlayClock.Restore).
        ///
        /// Run-length rather than one entry per cell because the file is written with
        /// Formatting.Indented: 90 000 cells written out one per line is on the order of a megabyte
        /// of JSON, while discovery is a handful of large runs - a growing disc on an otherwise
        /// untouched map is under a hundred of them.
        /// </summary>
        public string CaptureState()
        {
            if (Size == 0) return string.Empty;

            var builder = new StringBuilder();
            DiscoveryState runState = GetState(new GridCoord(0, 0));
            int runLength = 0;

            for (int y = 0; y < Size; y++)
            {
                int x = 0;
                while (x < Size)
                {
                    var cell = new GridCoord(x, y);
                    DiscoveryState[] chunk = ExistingChunk(cell);

                    // How much of this row the chunk under it covers, clipped to the map.
                    int spanEnd = Mathf.Min(Size, (x / ChunkSizeCells + 1) * ChunkSizeCells);

                    if (chunk == null)
                    {
                        // A whole span of a chunk that does not exist: wholly unknown, and skipped
                        // without touching memory. This is what keeps capturing a mostly-unknown map
                        // proportional to what was explored rather than to the map.
                        int span = spanEnd - x;
                        if (runState == DiscoveryState.Unknown)
                        {
                            runLength += span;
                        }
                        else
                        {
                            AppendRun(builder, runState, runLength);
                            runState = DiscoveryState.Unknown;
                            runLength = span;
                        }

                        x = spanEnd;
                        continue;
                    }

                    int rowBase = y % ChunkSizeCells * ChunkSizeCells;
                    for (; x < spanEnd; x++)
                    {
                        DiscoveryState state = chunk[rowBase + x % ChunkSizeCells];
                        if (state == runState)
                        {
                            runLength++;
                            continue;
                        }

                        AppendRun(builder, runState, runLength);
                        runState = state;
                        runLength = 1;
                    }
                }
            }

            AppendRun(builder, runState, runLength);
            return builder.ToString();
        }

        static void AppendRun(StringBuilder builder, DiscoveryState state, int length)
        {
            if (builder.Length > 0) builder.Append(RunSeparator);
            builder.Append((int)state).Append(RunFieldSeparator).Append(length);
        }

        /// <summary>
        /// Restores a previously captured map.
        ///
        /// Tolerant by design, like every other Restore here: a null, an empty string or a malformed
        /// run leaves the rest of the map Unknown rather than throwing. A save predating this field
        /// therefore loads as a wholly undiscovered map, and the Core's radius writes its own disc
        /// back on the first tick - the player loses nothing but what they had explored beyond it.
        /// Runs past the end of the map are ignored rather than overflowing.
        /// </summary>
        public void RestoreState(string encoded)
        {
            // Dropped rather than zeroed: a restore starts from a map where nothing has been written,
            // which for the sparse form means no chunks at all. The stamps go with them - a chunk that
            // no longer exists has no version, and every chunk the restore writes is stamped afresh
            // below with a version strictly higher than anything a reader has already drawn.
            _chunks.Clear();
            _chunkVersions.Clear();
            Version++;

            if (string.IsNullOrEmpty(encoded) || Size == 0) return;

            int cellCount = Size * Size;
            int index = 0;

            foreach (string run in encoded.Split(RunSeparator))
            {
                int separator = run.IndexOf(RunFieldSeparator);
                if (separator <= 0) continue;

                if (!int.TryParse(run.Substring(0, separator), out int state)) continue;
                if (!int.TryParse(run.Substring(separator + 1), out int length)) continue;
                if (length <= 0) continue;

                int end = Mathf.Min(index + length, cellCount);

                // An Unknown run writes nothing and allocates nothing - it is simply the chunks that
                // never come into existence. That is the whole saving on a map that is mostly unknown.
                if (state != (int)DiscoveryState.Unknown)
                {
                    for (int i = index; i < end; i++)
                    {
                        var cell = new GridCoord(i % Size, i / Size);
                        ChunkForWriting(cell)[OffsetInChunk(cell)] = (DiscoveryState)state;
                        _chunkVersions[ChunkIndexOf(cell)] = Version;
                    }
                }

                index = end;
                if (index >= cellCount) break;
            }
        }
    }
}

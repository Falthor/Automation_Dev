using System.Collections.Generic;
using System.Text;
using Game.Core;
using UnityEngine;

namespace Game.Grid
{
    /// <summary>
    /// What grows on the ground, derived per chunk, minus what the player has cleared.
    ///
    /// <b>Nothing is stored but the removals.</b> Outside the camera's window a rock is not an absent
    /// object - it is a function nobody has evaluated. Asking a chunk what it holds is a pure
    /// function of the world seed and the chunk's coordinates, so the same chunk answers the same
    /// thing whether it is asked first, last, or twice.
    ///
    /// The three ways of losing that, which the large-map directive names, have nowhere to live here:
    /// no neighbouring chunk is consulted, nothing larger than a cell is placed step by step (a
    /// cluster would have to derive from a deterministic anchor), and there is no sequential state to
    /// advance. Randomness comes from <see cref="DeterministicHash"/> and never from System.Random -
    /// DEVELOPMENT_RULES.md §7, and for the same reason as the terrain: this is re-derived at every
    /// load while the player's edits to it are saved.
    ///
    /// <b>The removals are the exception, and they have to be.</b> A rock cleared to make room for a
    /// building must not come back when the camera leaves and returns - the derivation knows nothing
    /// of what happened. So the world is derived, the player's changes are stored, and the derivation
    /// is filtered through them. Same shape as a mined-out deposit.
    /// </summary>
    public sealed class DecorRuntime
    {
        // Distinct salts so a chunk's count, its items' positions and their kinds are independent
        // draws rather than several views of one number.
        const uint CountSalt = 0x1B873593;
        const uint PlacementSalt = 0xCC9E2D51;
        const uint KindSalt = 0x85EBCA77;

        /// <summary>Separates one cleared cell from the next in the captured form.</summary>
        const char RemovalSeparator = ',';

        readonly int _seed;
        readonly BiomeField _biome;
        readonly float[][] _bandWeightsPerKind;
        readonly float _itemsPerChunk;
        readonly float _bandEdgeExclusion;

        /// <summary>Cells the player has cleared. Sparse and small: it holds what a player has actually removed, not a mask of the map.</summary>
        readonly HashSet<int> _removed = new HashSet<int>();

        /// <summary>Scratch for the derivation memo, so asking whether a cell holds anything allocates nothing once its chunk is known.</summary>
        readonly List<DecorItem> _probe = new List<DecorItem>();

        /// <summary>How many chunks' derivations GrowsAt remembers at once. A base's footprint spans a few; past that the cache is dropped rather than evicted one by one, because nothing here is a working set worth managing.</summary>
        const int DerivedChunkCacheSize = 32;

        /// <summary>Which cells each derived chunk lands on, by chunk index. Seed-only, so it never needs invalidating.</summary>
        readonly Dictionary<int, HashSet<int>> _derivedCells = new Dictionary<int, HashSet<int>>();

        public int MapSizeCells { get; }
        public int ChunkSizeCells { get; }

        /// <summary>Chunks along one axis, rounded up so a map that is not a whole number of chunks keeps its edge.</summary>
        public int ChunksPerAxis { get; }

        public int KindCount => _bandWeightsPerKind.Length;

        /// <summary>How many cells the player has cleared. For tests and reporting.</summary>
        public int RemovedCount => _removed.Count;

        /// <summary>
        /// Built from plain values rather than from the settings asset: Game.Grid must not depend on
        /// Game.Data. GameRuntime unpacks DecorSettings, exactly as it unpacks the terrain settings.
        /// </summary>
        public DecorRuntime(int mapSizeCells, int chunkSizeCells, int seed, BiomeField biome,
            float[][] bandWeightsPerKind, float itemsPerChunk, float bandEdgeExclusion)
        {
            MapSizeCells = Mathf.Max(0, mapSizeCells);
            ChunkSizeCells = Mathf.Max(1, chunkSizeCells);
            ChunksPerAxis = Mathf.CeilToInt(MapSizeCells / (float)ChunkSizeCells);

            _seed = seed;
            _biome = biome;
            _bandWeightsPerKind = bandWeightsPerKind ?? System.Array.Empty<float[]>();
            _itemsPerChunk = Mathf.Max(0f, itemsPerChunk);
            _bandEdgeExclusion = Mathf.Max(0f, bandEdgeExclusion);
        }

        /// <summary>
        /// Ground that is taken by something the player did not clear - today, an ore deposit.
        ///
        /// <b>Live, not stored</b>, and that is the whole point of it being separate from the removal
        /// set. A deposit is not the player's doing: recording its footprint as cleared would write
        /// hundreds of cells into every save to say something the seed already knows, and would leave
        /// them cleared forever once the deposit is mined out. Consulted at collect time instead, so
        /// the ground simply comes back when the deposit goes.
        ///
        /// Optional: null means nothing is taken, which is what a headless test wants.
        /// </summary>
        public System.Func<GridCoord, bool> GroundIsTaken { get; set; }

        public bool ContainsChunk(int chunkX, int chunkY)
            => chunkX >= 0 && chunkX < ChunksPerAxis && chunkY >= 0 && chunkY < ChunksPerAxis;

        /// <summary>
        /// Everything growing in one chunk, appended to a caller-owned list so a window refresh
        /// allocates nothing. Cleared and taken cells are filtered out here, so no caller has to
        /// remember to.
        /// </summary>
        public void CollectChunk(int chunkX, int chunkY, List<DecorItem> into)
        {
            if (into == null) return;

            int first = into.Count;
            DeriveChunk(chunkX, chunkY, into);

            // The two live filters, applied after the derivation rather than inside it. What a chunk
            // holds must depend on nothing but the seed and the coordinates - that is what lets
            // GrowsAt memoise it. What the player cleared and what a deposit sits on both change
            // during a game, so they are removed from the answer, never from the derivation.
            for (int i = into.Count - 1; i >= first; i--)
            {
                GridCoord cell = into[i].Cell;
                if (IsRemoved(cell) || (GroundIsTaken != null && GroundIsTaken(cell))) into.RemoveAt(i);
            }
        }

        /// <summary>What the seed alone puts in a chunk, before anything that has happened since. Pure in the sense the large-map directive asks for: same seed, same coordinates, same answer, whatever order chunks are asked in and whatever the player has done.</summary>
        void DeriveChunk(int chunkX, int chunkY, List<DecorItem> into)
        {
            if (!ContainsChunk(chunkX, chunkY) || KindCount == 0) return;

            int chunkIndex = chunkY * ChunksPerAxis + chunkX;
            int originX = chunkX * ChunkSizeCells;
            int originY = chunkY * ChunkSizeCells;

            // The count varies around the configured density rather than being exactly it, so chunks
            // do not all hold the same number - which would read as a grid.
            uint countDraw = DeterministicHash.Mix(_seed, chunkIndex, CountSalt);
            int count = Mathf.RoundToInt(_itemsPerChunk * (0.6f + 0.8f * (countDraw % 1000u) / 1000f));

            for (int i = 0; i < count; i++)
            {
                uint place = DeterministicHash.Mix(_seed, chunkIndex, PlacementSalt + (uint)i * 0x9E3779B9u);

                int x = originX + (int)(place % (uint)ChunkSizeCells);
                int y = originY + (int)(place / 65536u % (uint)ChunkSizeCells);
                if (x >= MapSizeCells || y >= MapSizeCells) continue;   // the map's edge clips the last chunks

                var cell = new GridCoord(x, y);
                var world = new Vector2(x + 0.5f, y + 0.5f);

                // Too close to a band boundary is where the CPU port and the shader can disagree.
                // Growing nothing there costs a few spots and removes the only case where a rock
                // could take the wrong biome's art.
                if (_bandEdgeExclusion > 0f && _biome != null && _biome.DistanceToBandEdge(world) < _bandEdgeExclusion) continue;

                int band = _biome != null ? _biome.BandAt(world) : 0;
                int kind = PickKind(band, chunkIndex, i);
                if (kind < 0) continue;

                float scale01 = (place / 4096u % 1000u) / 1000f;
                into.Add(new DecorItem(cell, kind, band, scale01, place));
            }
        }

        /// <summary>A weighted draw among the kinds that grow in this band. -1 when none does.</summary>
        int PickKind(int band, int chunkIndex, int itemIndex)
        {
            float total = 0f;
            for (int k = 0; k < KindCount; k++) total += WeightOf(k, band);
            if (total <= 0f) return -1;

            uint draw = DeterministicHash.Mix(_seed, chunkIndex * 397 + itemIndex, KindSalt);
            float pick = (draw % 100000u) / 100000f * total;

            float cumulative = 0f;
            for (int k = 0; k < KindCount; k++)
            {
                cumulative += WeightOf(k, band);
                if (pick < cumulative) return k;
            }

            return KindCount - 1;
        }

        float WeightOf(int kind, int band)
        {
            float[] weights = _bandWeightsPerKind[kind];
            if (weights == null || band < 0 || band >= weights.Length) return 0f;
            return Mathf.Max(0f, weights[band]);
        }

        // ---- The player's edits ----

        public bool Contains(GridCoord cell)
            => cell.X >= 0 && cell.X < MapSizeCells && cell.Y >= 0 && cell.Y < MapSizeCells;

        public bool IsRemoved(GridCoord cell) => Contains(cell) && _removed.Contains(Index(cell));

        /// <summary>
        /// Whether anything actually grows at a cell right now.
        ///
        /// <b>The chunk's derivation is memoised</b>, and that is not a micro-optimisation. Asked
        /// once per cell it costs a full chunk derivation each time: a 200-building base sweeping its
        /// own footprint at load measured 231 ms - a visible hitch, growing with the size of the base.
        /// Footprints are contiguous, so a handful of remembered chunks turns that back into a few
        /// derivations. Only what the seed decides is remembered; what the player cleared and what a
        /// deposit covers are asked live, so nothing here can go stale.
        /// </summary>
        public bool GrowsAt(GridCoord cell)
        {
            if (!Contains(cell)) return false;
            if (IsRemoved(cell)) return false;
            if (GroundIsTaken != null && GroundIsTaken(cell)) return false;

            return DerivedCellsOf(cell.X / ChunkSizeCells, cell.Y / ChunkSizeCells).Contains(Index(cell));
        }

        /// <summary>The cells one chunk's derivation lands on. Cached, and dropped wholesale once the cache is bigger than a base's worth of chunks - a bound rather than a policy, since the access pattern is a footprint sweep and never a map walk.</summary>
        HashSet<int> DerivedCellsOf(int chunkX, int chunkY)
        {
            int chunkIndex = chunkY * ChunksPerAxis + chunkX;
            if (_derivedCells.TryGetValue(chunkIndex, out HashSet<int> cached)) return cached;

            if (_derivedCells.Count >= DerivedChunkCacheSize) _derivedCells.Clear();

            _probe.Clear();
            DeriveChunk(chunkX, chunkY, _probe);

            var cells = new HashSet<int>();
            foreach (DecorItem item in _probe) cells.Add(Index(item.Cell));

            _derivedCells[chunkIndex] = cells;
            return cells;
        }

        /// <summary>
        /// Records that whatever grew here has been cleared. Returns true only if that changed
        /// something, so a caller can tell a real clearing from a repeat.
        ///
        /// <b>Nothing is recorded where nothing grows</b>, and the difference is not small. A
        /// building's footprint is cells, decor is roughly one item per hundred cells: recording the
        /// whole footprint would put about a hundred entries in the save for every rock actually
        /// cleared, and would make the delta set grow with the area a player has built on rather than
        /// with what they have removed. The set is meant to be the player's clearing history, and
        /// this is what keeps it that.
        /// </summary>
        public bool Remove(GridCoord cell) => GrowsAt(cell) && _removed.Add(Index(cell));

        int Index(GridCoord cell) => cell.Y * MapSizeCells + cell.X;

        // ---- Save / Restore (CONTRACTS.md §14) ----

        /// <summary>
        /// The cleared cells, as a comma-separated list of indices.
        ///
        /// Only the removals: what grows is re-derived, so storing it would be storing something the
        /// seed already says. The list is as long as the player's own clearing history - a few dozen
        /// entries for a base, not a mask of the map - so a plain list beats any encoding here.
        ///
        /// A string rather than a JObject, like the discovery state and for the same reason:
        /// Game.Grid references only Game.Core and Game.Data, and would otherwise gain a JSON
        /// dependency it has no other use for.
        /// </summary>
        public string CaptureState()
        {
            if (_removed.Count == 0) return string.Empty;

            var builder = new StringBuilder();
            foreach (int index in _removed)
            {
                if (builder.Length > 0) builder.Append(RemovalSeparator);
                builder.Append(index);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Tolerant like every other Restore: null, empty or malformed entries leave the rest intact
        /// rather than throwing. A save predating this field restores as a world nobody has cleared
        /// anything in - the decor simply all grows back, which is right, since it never recorded that
        /// any had been removed.
        /// </summary>
        public void RestoreState(string encoded)
        {
            _removed.Clear();
            if (string.IsNullOrEmpty(encoded)) return;

            int cellCount = MapSizeCells * MapSizeCells;
            foreach (string entry in encoded.Split(RemovalSeparator))
            {
                if (!int.TryParse(entry, out int index)) continue;
                if (index < 0 || index >= cellCount) continue;
                _removed.Add(index);
            }
        }
    }

    /// <summary>
    /// One thing growing at one cell. A value, produced on demand and thrown away - decor is never an
    /// object that lives somewhere until the view instantiates it.
    /// </summary>
    public readonly struct DecorItem
    {
        public readonly GridCoord Cell;
        public readonly int Kind;

        /// <summary>The ground band under it. Carried so the view can tint to match without asking the biome again.</summary>
        public readonly int Band;

        /// <summary>Where in the kind's scale range this one falls, 0 to 1.</summary>
        public readonly float Scale01;

        /// <summary>The raw draw it came from - a caller wanting one more independent-looking choice (which sprite of the kind, a mirror, a rotation) can take more bits from it rather than hashing again.</summary>
        public readonly uint Draw;

        public DecorItem(GridCoord cell, int kind, int band, float scale01, uint draw)
        {
            Cell = cell;
            Kind = kind;
            Band = band;
            Scale01 = scale01;
            Draw = draw;
        }
    }
}

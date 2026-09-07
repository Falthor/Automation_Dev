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
        // Distinct salts so a chunk's count, its anchors' positions, their kinds and the shape of a
        // clump are independent draws rather than several views of one number.
        const uint CountSalt = 0x1B873593;
        const uint PlacementSalt = 0xCC9E2D51;
        const uint KindSalt = 0x85EBCA77;
        const uint ClumpSalt = 0x27D4EB2F;
        const uint MemberSalt = 0x165667B1;
        const uint LookSalt = 0x9E3779B1;

        /// <summary>Separates one cleared cell from the next in the captured form.</summary>
        const char RemovalSeparator = ',';

        readonly int _seed;
        readonly BiomeField _biome;
        readonly float[][] _bandWeightsPerKind;
        readonly DecorClustering[] _clusteringPerKind;
        readonly float _spotsPerChunk;
        readonly float _bandEdgeExclusion;

        /// <summary>The widest clump any kind can make. What decides how far outside a chunk an anchor may sit and still reach into it - and it is why one ring of neighbours is enough.</summary>
        readonly float _maxClusterRadius;

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
        ///
        /// <paramref name="clusteringPerKind"/> may be null or short, in which case the kinds it does
        /// not cover grow one at a time.
        /// </summary>
        public DecorRuntime(int mapSizeCells, int chunkSizeCells, int seed, BiomeField biome,
            float[][] bandWeightsPerKind, float spotsPerChunk, float bandEdgeExclusion,
            DecorClustering[] clusteringPerKind = null)
        {
            MapSizeCells = Mathf.Max(0, mapSizeCells);
            ChunkSizeCells = Mathf.Max(1, chunkSizeCells);
            ChunksPerAxis = Mathf.CeilToInt(MapSizeCells / (float)ChunkSizeCells);

            _seed = seed;
            _biome = biome;
            _bandWeightsPerKind = bandWeightsPerKind ?? System.Array.Empty<float[]>();
            _spotsPerChunk = Mathf.Max(0f, spotsPerChunk);
            _bandEdgeExclusion = Mathf.Max(0f, bandEdgeExclusion);

            _clusteringPerKind = new DecorClustering[KindCount];
            for (int k = 0; k < KindCount; k++)
            {
                DecorClustering shape = clusteringPerKind != null && k < clusteringPerKind.Length
                    ? clusteringPerKind[k]
                    : DecorClustering.Solitary;

                // A clump must not reach past one chunk, or a further ring of neighbours would have
                // to be derived for every chunk. Nothing wants a clump that wide anyway - the widest
                // the old whole-map scatter made was five cells.
                shape = shape.WithRadiusAtMost(ChunkSizeCells);
                _clusteringPerKind[k] = shape;

                if (shape.Radius > _maxClusterRadius) _maxClusterRadius = shape.Radius;
            }
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

        /// <summary>
        /// What the seed alone puts in a chunk, before anything that has happened since. Pure in the
        /// sense the large-map directive asks for: same seed, same coordinates, same answer, whatever
        /// order chunks are asked in and whatever the player has done.
        ///
        /// <b>The neighbours' anchors are derived too, and that is not the prohibition it looks
        /// like.</b> A clump anchored just outside a chunk has members inside it, so a chunk that
        /// only looked at its own anchors would cut every clump straight along the chunk lines - the
        /// grid made visible, which is the whole thing this scatter exists to avoid. What is
        /// forbidden is <i>consulting a neighbour's state</i>: an answer that depends on whether the
        /// neighbour has been asked yet, or on what it decided to keep. Here the neighbour's anchors
        /// are re-derived from the same pure function, so the answer still depends on nothing but the
        /// seed and the coordinates. One ring is enough because no clump is wider than a chunk.
        /// </summary>
        void DeriveChunk(int chunkX, int chunkY, List<DecorItem> into)
        {
            if (!ContainsChunk(chunkX, chunkY) || KindCount == 0) return;

            int ring = _maxClusterRadius > 0f ? 1 : 0;
            for (int ny = chunkY - ring; ny <= chunkY + ring; ny++)
            {
                for (int nx = chunkX - ring; nx <= chunkX + ring; nx++)
                {
                    if (ContainsChunk(nx, ny)) DeriveAnchorsOf(nx, ny, chunkX, chunkY, into);
                }
            }
        }

        /// <summary>
        /// The clumps anchored in one chunk, keeping only the members that land inside another. The
        /// two are the same chunk for all but the outer ring, where an anchor's clump spills across
        /// the boundary.
        /// </summary>
        void DeriveAnchorsOf(int anchorChunkX, int anchorChunkY, int intoChunkX, int intoChunkY, List<DecorItem> into)
        {
            int anchorChunkIndex = anchorChunkY * ChunksPerAxis + anchorChunkX;
            int originX = anchorChunkX * ChunkSizeCells;
            int originY = anchorChunkY * ChunkSizeCells;

            int keepMinX = intoChunkX * ChunkSizeCells;
            int keepMinY = intoChunkY * ChunkSizeCells;
            int keepMaxX = keepMinX + ChunkSizeCells - 1;
            int keepMaxY = keepMinY + ChunkSizeCells - 1;

            // The count varies around the configured density rather than being exactly it, so chunks
            // do not all hold the same number - which would read as a grid.
            uint countDraw = DeterministicHash.Mix(_seed, anchorChunkIndex, CountSalt);
            int spots = Mathf.RoundToInt(_spotsPerChunk * (0.6f + 0.8f * (countDraw % 1000u) / 1000f));

            for (int i = 0; i < spots; i++)
            {
                uint place = DeterministicHash.Mix(_seed, anchorChunkIndex, PlacementSalt + (uint)i * 0x9E3779B9u);

                int ax = originX + (int)(place % (uint)ChunkSizeCells);
                int ay = originY + (int)(place / 65536u % (uint)ChunkSizeCells);
                if (ax >= MapSizeCells || ay >= MapSizeCells) continue;   // the map's edge clips the last chunks

                // Cheap reject before the biome sample, which is the expensive part: an anchor that
                // cannot reach the chunk being derived contributes nothing to it. This is what keeps
                // the extra ring from costing nine times the work - for the eight neighbours only a
                // thin margin of anchors survives it.
                if (DistanceToBox(ax, ay, keepMinX, keepMinY, keepMaxX, keepMaxY) > _maxClusterRadius) continue;

                var anchorWorld = new Vector2(ax + 0.5f, ay + 0.5f);

                // Too close to a band boundary is where the CPU port and the shader can disagree.
                // Growing nothing there costs a few spots and removes the only case where a clump
                // could take the wrong biome's art.
                if (_bandEdgeExclusion > 0f && _biome != null && _biome.DistanceToBandEdge(anchorWorld) < _bandEdgeExclusion) continue;

                // Band and kind are decided at the anchor, not per member: a clump is one species
                // growing in one place, which is what makes it read as a clump rather than as a
                // denser patch of the same uniform mixture.
                int band = _biome != null ? _biome.BandAt(anchorWorld) : 0;
                int kind = PickKind(band, anchorChunkIndex, i);
                if (kind < 0) continue;

                EmitSpot(anchorChunkIndex, i, ax, ay, kind, band,
                    keepMinX, keepMinY, keepMaxX, keepMaxY, into);
            }
        }

        /// <summary>One anchor's worth of decor: a single item, or a clump scattered in a disc around it.</summary>
        void EmitSpot(int anchorChunkIndex, int anchorIndex, int ax, int ay, int kind, int band,
            int keepMinX, int keepMinY, int keepMaxX, int keepMaxY, List<DecorItem> into)
        {
            DecorClustering shape = _clusteringPerKind[kind];

            uint clumpDraw = DeterministicHash.Mix(_seed, anchorChunkIndex * 397 + anchorIndex, ClumpSalt);
            bool clumped = shape.Radius > 0f && (clumpDraw % 1000u) / 1000f < shape.Chance;

            int members = clumped
                ? shape.MinMembers + (int)(clumpDraw / 1000u % (uint)(shape.MaxMembers - shape.MinMembers + 1))
                : 1;

            for (int m = 0; m < members; m++)
            {
                int x = ax, y = ay;

                if (clumped)
                {
                    // Injective in (chunk, anchor, member) for any plausible count, so no two members
                    // anywhere on the map share a draw.
                    uint spread = DeterministicHash.Mix(_seed, (anchorChunkIndex * 397 + anchorIndex) * 31 + m, MemberSalt);

                    float angle = (spread % 10000u) / 10000f * Mathf.PI * 2f;
                    float distance = Mathf.Sqrt((spread / 10000u % 10000u) / 10000f) * shape.Radius;

                    x = ax + Mathf.RoundToInt(Mathf.Cos(angle) * distance);
                    y = ay + Mathf.RoundToInt(Mathf.Sin(angle) * distance);
                }

                if (x < keepMinX || x > keepMaxX || y < keepMinY || y > keepMaxY) continue;
                if (x < 0 || y < 0 || x >= MapSizeCells || y >= MapSizeCells) continue;

                uint look = DeterministicHash.Mix(_seed, (anchorChunkIndex * 397 + anchorIndex) * 31 + m, LookSalt);
                float scale01 = (look % 1000u) / 1000f;

                into.Add(new DecorItem(new GridCoord(x, y), kind, band, scale01, look));
            }
        }

        /// <summary>Distance from a cell to a box of cells, zero inside it. What tells an anchor whether its widest possible clump could reach the chunk being derived.</summary>
        static float DistanceToBox(int x, int y, int minX, int minY, int maxX, int maxY)
        {
            int dx = x < minX ? minX - x : (x > maxX ? x - maxX : 0);
            int dy = y < minY ? minY - y : (y > maxY ? y - maxY : 0);
            return Mathf.Sqrt(dx * dx + dy * dy);
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
    /// How one kind of decor clumps: bushes grow in thickets, a lone tree does not.
    ///
    /// <b>Why it exists at all.</b> Items placed one per draw give an even scatter, and an even
    /// scatter reads as regularity just as much as a grid does - it is the same defect the ground
    /// noise and the sector names were shaped to avoid, in another form. Clumping is what breaks it.
    ///
    /// The old whole-map scatter got clumps from a sequential pass: pick an anchor, walk outward,
    /// place members. That cannot survive a chunked derivation, which has no sequence and no memory.
    /// The replacement is an anchor that is itself derived - hashed from the chunk and the spot index
    /// - so a clump is a pure function like everything else, and a chunk can be asked about its own
    /// clumps without anybody having walked there first.
    /// </summary>
    public readonly struct DecorClustering
    {
        /// <summary>How often an anchor grows a whole clump rather than a single item, 0 to 1.</summary>
        public readonly float Chance;

        public readonly int MinMembers;
        public readonly int MaxMembers;

        /// <summary>How far members scatter from the anchor, in cells. Zero means this kind never clumps, whatever the chance says.</summary>
        public readonly float Radius;

        public DecorClustering(float chance, int minMembers, int maxMembers, float radius)
        {
            Chance = Mathf.Clamp01(chance);
            MinMembers = Mathf.Max(1, minMembers);
            MaxMembers = Mathf.Max(MinMembers, maxMembers);
            Radius = Mathf.Max(0f, radius);
        }

        /// <summary>One at a time, which is what a kind with no clustering configured gets.</summary>
        public static DecorClustering Solitary => new DecorClustering(0f, 1, 1, 0f);

        /// <summary>Narrows a clump that would reach past one chunk, so a single ring of neighbouring anchors stays sufficient.</summary>
        public DecorClustering WithRadiusAtMost(float maxRadius)
            => Radius <= maxRadius ? this : new DecorClustering(Chance, MinMembers, MaxMembers, maxRadius);
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

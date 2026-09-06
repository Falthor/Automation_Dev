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

        readonly DiscoveryState[] _cells;

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

        public DiscoveryRuntime(int size)
        {
            Size = Mathf.Max(0, size);

            // Allocated once, for the life of the run: 90 000 bytes on the current 300-cell map.
            _cells = new DiscoveryState[Size * Size];
        }

        /// <summary>The state of one cell. Out of bounds reads as Unknown - there is nothing out there to have discovered.</summary>
        public DiscoveryState GetState(GridCoord cell)
        {
            return Contains(cell) ? _cells[cell.Y * Size + cell.X] : DiscoveryState.Unknown;
        }

        public bool IsDiscovered(GridCoord cell) => GetState(cell) == DiscoveryState.Discovered;

        public bool Contains(GridCoord cell)
            => cell.X >= 0 && cell.X < Size && cell.Y >= 0 && cell.Y < Size;

        /// <summary>Marks one cell discovered. Returns true only if that changed something, so a caller can tell a real revelation from a repeat.</summary>
        public bool Reveal(GridCoord cell)
        {
            if (!Contains(cell)) return false;

            int index = cell.Y * Size + cell.X;
            if (_cells[index] == DiscoveryState.Discovered) return false;

            _cells[index] = DiscoveryState.Discovered;
            Version++;
            return true;
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

                    int index = y * Size + x;
                    if (_cells[index] == DiscoveryState.Discovered) continue;

                    _cells[index] = DiscoveryState.Discovered;
                    revealed++;
                }
            }

            if (revealed > 0) Version++;
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

                int index = cell.Y * Size + cell.X;
                if (_cells[index] == DiscoveryState.Discovered) continue;

                _cells[index] = DiscoveryState.Discovered;
                revealed++;
            }

            if (revealed > 0) Version++;
            return revealed;
        }

        /// <summary>How many cells are discovered. Walks the array - for tests and reporting, not for a per-frame read.</summary>
        public int DiscoveredCount()
        {
            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i] == DiscoveryState.Discovered) count++;
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
            if (_cells.Length == 0) return string.Empty;

            var builder = new StringBuilder();
            DiscoveryState runState = _cells[0];
            int runLength = 1;

            for (int i = 1; i < _cells.Length; i++)
            {
                if (_cells[i] == runState)
                {
                    runLength++;
                    continue;
                }

                AppendRun(builder, runState, runLength);
                runState = _cells[i];
                runLength = 1;
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
            System.Array.Clear(_cells, 0, _cells.Length);
            Version++;

            if (string.IsNullOrEmpty(encoded)) return;

            int index = 0;
            foreach (string run in encoded.Split(RunSeparator))
            {
                int separator = run.IndexOf(RunFieldSeparator);
                if (separator <= 0) continue;

                if (!int.TryParse(run.Substring(0, separator), out int state)) continue;
                if (!int.TryParse(run.Substring(separator + 1), out int length)) continue;
                if (length <= 0) continue;

                int end = Mathf.Min(index + length, _cells.Length);
                if (state != (int)DiscoveryState.Unknown)
                {
                    for (int i = index; i < end; i++) _cells[i] = (DiscoveryState)state;
                }

                index = end;
                if (index >= _cells.Length) break;
            }
        }
    }
}

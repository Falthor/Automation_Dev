using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Grid
{
    /// <summary>
    /// The map cut into square sectors - the unit a mission is aimed at.
    ///
    /// <b>Called a sector, not a zone.</b> "Zone" is already taken by the Core and AI-agent signal
    /// zones, which are a different thing entirely (see the directive, §4).
    ///
    /// <b>A regular tiling, and it is meant to be.</b> Index, origin, centre and cells are plain
    /// arithmetic on a coordinate: nothing is walked, nothing is stored, and there is no list of
    /// sectors anywhere. That is what makes the lazy generation the directive asks for fall out for
    /// free rather than being something to engineer - 625 sectors on the current map, and a run only
    /// ever asks about the handful it can reach.
    ///
    /// The regularity is never visible, because a mission does not reveal the square: it reveals the
    /// disc inscribed in it (<see cref="RevealInscribedDisc"/>). The player sees round patches
    /// joining up, never a grid.
    ///
    /// Pure geometry - it holds no identity, no risk and no contents. Those are derived from the
    /// world seed in Game.Gameplay, which is where the game's own meaning lives.
    /// </summary>
    public sealed class SectorGrid
    {
        public int MapSizeCells { get; }
        public int SectorSizeCells { get; }

        /// <summary>Sectors along one axis. Rounded up: a map that is not a whole number of sectors gets a partial last row and column rather than losing its edge.</summary>
        public int Columns { get; }

        public int Count => Columns * Columns;

        /// <summary>Half a sector - the radius of the inscribed disc, which touches the middle of each side.</summary>
        public float InscribedRadiusCells => SectorSizeCells * 0.5f;

        /// <summary>
        /// The sector size has no default on purpose. It is a setting (SectorSettings), and a default
        /// here would be a second copy of it - the exact way a value re-freezes in code. Required, so
        /// a caller that forgets it fails to compile rather than silently disagreeing with the asset.
        /// </summary>
        public SectorGrid(int mapSizeCells, int sectorSizeCells)
        {
            MapSizeCells = Mathf.Max(0, mapSizeCells);
            SectorSizeCells = Mathf.Max(1, sectorSizeCells);
            Columns = Mathf.CeilToInt(MapSizeCells / (float)SectorSizeCells);
        }

        public bool ContainsIndex(int index) => index >= 0 && index < Count;

        /// <summary>The sector a cell belongs to, or -1 for a cell off the map.</summary>
        public int IndexAt(GridCoord cell)
        {
            if (cell.X < 0 || cell.X >= MapSizeCells || cell.Y < 0 || cell.Y >= MapSizeCells) return -1;
            return cell.Y / SectorSizeCells * Columns + cell.X / SectorSizeCells;
        }

        public int IndexAt(int column, int row)
            => column < 0 || column >= Columns || row < 0 || row >= Columns ? -1 : row * Columns + column;

        public int ColumnOf(int index) => ContainsIndex(index) ? index % Columns : -1;

        public int RowOf(int index) => ContainsIndex(index) ? index / Columns : -1;

        /// <summary>The sector's lowest-left cell.</summary>
        public GridCoord OriginOf(int index)
        {
            if (!ContainsIndex(index)) return new GridCoord(0, 0);
            return new GridCoord(index % Columns * SectorSizeCells, index / Columns * SectorSizeCells);
        }

        /// <summary>
        /// The middle of the sector, in cell space - the same space
        /// <see cref="DiscoveryRuntime.RevealDisc"/> and WorldGenerator.CoreCenterCells use.
        ///
        /// Always the middle of the full square, even where the map's edge clips the sector: the
        /// centre is what mission range is measured against, and one that moved with the clipping
        /// would make the last row of sectors closer than it looks.
        /// </summary>
        public Vector2 CenterCells(int index)
        {
            GridCoord origin = OriginOf(index);
            return new Vector2(origin.X + InscribedRadiusCells, origin.Y + InscribedRadiusCells);
        }

        /// <summary>
        /// Every cell of the sector, clipped to the map.
        ///
        /// Allocates an iterator, so it is for tests, for scattering contents and for the zoomed map
        /// - never for a per-frame read. Nothing in the reveal path calls it: revealing goes through
        /// the disc below, which walks a bounding box and allocates nothing.
        /// </summary>
        public IEnumerable<GridCoord> CellsOf(int index)
        {
            if (!ContainsIndex(index)) yield break;

            GridCoord origin = OriginOf(index);
            int maxX = Mathf.Min(origin.X + SectorSizeCells, MapSizeCells);
            int maxY = Mathf.Min(origin.Y + SectorSizeCells, MapSizeCells);

            for (int y = origin.Y; y < maxY; y++)
            {
                for (int x = origin.X; x < maxX; x++) yield return new GridCoord(x, y);
            }
        }

        /// <summary>
        /// What a mission reveals: the disc inscribed in the sector, not the sector.
        ///
        /// The four corners stay hidden, so two revealed neighbours leave an undiscovered fringe
        /// between them that only exploring fills in. The map opens as round patches that join up,
        /// and the tiling underneath never shows - which is the whole reason the partition is allowed
        /// to be a plain grid.
        ///
        /// Returns how many cells this actually changed, so a caller can tell a real revelation from
        /// re-revealing a sector already seen.
        /// </summary>
        public int RevealInscribedDisc(int index, DiscoveryRuntime discovery)
        {
            if (discovery == null || !ContainsIndex(index)) return 0;
            return discovery.RevealDisc(CenterCells(index), InscribedRadiusCells);
        }

        /// <summary>
        /// Whether nothing in the sector has been seen - what "is this still a mission destination?"
        /// actually asks.
        ///
        /// Separate from <see cref="DiscoveryOf"/> rather than derived from it because it can stop
        /// at the first discovered cell instead of counting all 144, and because it walks the cells
        /// with plain loops rather than the iterator: the mission range asks this of every sector in
        /// a ring, and an enumerator per sector would be an allocation per query.
        /// </summary>
        public bool IsWhollyUnknown(int index, DiscoveryRuntime discovery)
        {
            if (!ContainsIndex(index)) return false;
            if (discovery == null) return true;

            GridCoord origin = OriginOf(index);
            int maxX = Mathf.Min(origin.X + SectorSizeCells, MapSizeCells);
            int maxY = Mathf.Min(origin.Y + SectorSizeCells, MapSizeCells);

            for (int y = origin.Y; y < maxY; y++)
            {
                for (int x = origin.X; x < maxX; x++)
                {
                    if (discovery.IsDiscovered(new GridCoord(x, y))) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// How much of the sector the player has seen, derived from the cells and stored nowhere -
        /// the cells are the authority, exactly as the directive requires.
        ///
        /// <see cref="SectorDiscovery.Partial"/> is the normal resting state of a sector revealed by
        /// a mission, not a transient: the inscribed disc can never cover the corners. It is the
        /// state that says "there is something here and you have not seen all of it".
        /// </summary>
        public SectorDiscovery DiscoveryOf(int index, DiscoveryRuntime discovery)
        {
            if (discovery == null || !ContainsIndex(index)) return SectorDiscovery.Unknown;

            int total = 0;
            int discovered = 0;

            foreach (GridCoord cell in CellsOf(index))
            {
                total++;
                if (discovery.IsDiscovered(cell)) discovered++;
            }

            if (total == 0 || discovered == 0) return SectorDiscovery.Unknown;
            return discovered == total ? SectorDiscovery.Discovered : SectorDiscovery.Partial;
        }
    }
}

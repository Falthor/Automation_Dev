using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Grid
{
    /// <summary>
    /// The map cut into square sectors - the internal unit sector contents and materialisation work
    /// in. Nothing is aimed at a sector and the player never points at one.
    ///
    /// <b>Called a sector, not a zone.</b> "Zone" is already taken by the Core and AI-agent signal
    /// zones, which are a different thing entirely.
    ///
    /// <b>A regular tiling, and it is meant to be.</b> Index, origin, centre and cells are plain
    /// arithmetic on a coordinate: nothing is walked, nothing is stored, and there is no list of
    /// sectors anywhere. That is what makes the generation lazy by construction rather than by
    /// bookkeeping - a run only ever asks about the handful of sectors a robot has reached.
    ///
    /// The regularity is never visible, because nothing ever reveals the square. Discovery is
    /// written as discs (<see cref="DiscoveryRuntime.RevealDisc"/>), whose overlapping patches leave
    /// no straight edge for the tiling to show through.
    ///
    /// Pure geometry - it holds no identity and no contents. Those are derived from the world seed
    /// in Game.Gameplay, which is where the game's own meaning lives.
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
        /// Always the middle of the full square, even where the map's edge clips the sector. A centre
        /// that moved with the clipping would put the last row's contents off-centre against every
        /// other row's, for no reason the arithmetic anywhere else knows about.
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
        /// <see cref="DiscoveryRuntime.RevealDisc"/>, which walks a bounding box and allocates
        /// nothing.
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
    }
}

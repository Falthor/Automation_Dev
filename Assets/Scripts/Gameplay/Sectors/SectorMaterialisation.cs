using Game.Core;
using Game.Data;
using Game.Grid;

namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// Turns a sector's derived contents into real deposits, once, when a mission opens it.
    ///
    /// <b>Placed content wins, and that is the whole rule.</b> A sector already carrying something put
    /// there deliberately keeps it, and the derivation only fills sectors that carry nothing. Stated
    /// about the data rather than about geometry, which is what makes it durable: there is no starting
    /// perimeter to maintain, and it covers in advance anything else placed by hand - a scripted
    /// wreck, a particular nest, a secondary Core's guaranteed clusters.
    ///
    /// <b>The ordering constraint is the real risk.</b> Placed content must exist before the
    /// derivation reaches those sectors; reverse the two and the starting area is overwritten by
    /// random ore, and the introduction stops being playable. Today the order holds by construction -
    /// `WorldGenerator` places its clusters during `Awake`, and nothing materialises until a mission
    /// lands or the Core's own disc is walked, both of which happen later. This class does not rely on
    /// that: it checks the grid at the moment it writes.
    ///
    /// <b>Idempotent without bookkeeping.</b> Nothing records which sectors have been materialised,
    /// because nothing needs to: an occupied cell is skipped, and deposits are saved, so a reloaded
    /// world finds its own deposits already standing and writes nothing. A set of materialised sectors
    /// would be a second source of truth that could disagree with the grid.
    /// </summary>
    public sealed class SectorMaterialisation
    {
        readonly SectorGrid _grid;
        readonly GridRuntime _cells;
        readonly SectorCatalog _catalog;
        readonly OreDepositDefinition[] _resources;

        /// <summary>How many sectors have actually written something. For tests and reporting.</summary>
        public int MaterialisedSectorCount { get; private set; }

        /// <summary>How many sectors were left alone because they already carried placed content.</summary>
        public int SkippedForPlacedContentCount { get; private set; }

        /// <summary>
        /// The ore definitions, in the order `SectorContents.ResourceIndex` indexes them. Handed in
        /// rather than read from a settings asset, so the catalog never has to know what an ore is.
        /// </summary>
        public SectorMaterialisation(SectorGrid grid, GridRuntime cells, SectorCatalog catalog,
            OreDepositDefinition[] resources)
        {
            _grid = grid;
            _cells = cells;
            _catalog = catalog;
            _resources = resources ?? System.Array.Empty<OreDepositDefinition>();
        }

        /// <summary>
        /// Writes a sector's derived deposits into the grid, and answers how many it placed.
        ///
        /// Returns 0 for a sector that was already occupied, that derives nothing, or that has been
        /// materialised before - the three cases are deliberately indistinguishable to the caller,
        /// because none of them is a failure.
        /// </summary>
        public int Materialise(int sectorIndex)
        {
            if (_grid == null || _cells == null || _catalog == null) return 0;
            if (!_grid.ContainsIndex(sectorIndex)) return 0;

            SectorContents contents = _catalog.ContentsOf(sectorIndex);
            if (contents.DepositCells.Length == 0) return 0;

            OreDepositDefinition definition = ResourceFor(contents.ResourceIndex);
            if (definition == null) return 0;

            // The whole sector, not cell by cell: a sector that already carries placed content keeps
            // all of it, rather than having derived ore grow in the gaps between hand-placed clusters.
            if (CarriesPlacedContent(sectorIndex))
            {
                SkippedForPlacedContentCount++;
                return 0;
            }

            int placed = 0;
            foreach (GridCoord cell in contents.DepositCells)
            {
                // ContentsOf already clips to the sector's real extent, so a cell outside the map
                // should be impossible; checked anyway because a deposit written off-map would be
                // invisible and unreachable rather than loudly wrong.
                if (cell.X < 0 || cell.Y < 0 || cell.X >= _grid.MapSizeCells || cell.Y >= _grid.MapSizeCells) continue;
                if (_cells.GetOccupant(cell) != null) continue;   // a building or an earlier deposit

                _cells.PlaceDeposit(cell, definition);
                placed++;
            }

            if (placed > 0) MaterialisedSectorCount++;
            return placed;
        }

        /// <summary>
        /// Whether anything already stands in this sector - the test that makes placed content win.
        ///
        /// Walks the sector's cells with plain loops rather than the iterator, for the reason
        /// `IsWhollyUnknown` gives: this is asked once per sector opened, and an enumerator per sector
        /// would be an allocation per mission.
        /// </summary>
        public bool CarriesPlacedContent(int sectorIndex)
        {
            if (_grid == null || _cells == null || !_grid.ContainsIndex(sectorIndex)) return false;

            GridCoord origin = _grid.OriginOf(sectorIndex);
            int maxX = System.Math.Min(origin.X + _grid.SectorSizeCells, _grid.MapSizeCells);
            int maxY = System.Math.Min(origin.Y + _grid.SectorSizeCells, _grid.MapSizeCells);

            for (int y = origin.Y; y < maxY; y++)
            {
                for (int x = origin.X; x < maxX; x++)
                {
                    if (_cells.GetOccupant(new GridCoord(x, y)) != null) return true;
                }
            }

            return false;
        }

        OreDepositDefinition ResourceFor(int index)
            => index >= 0 && index < _resources.Length ? _resources[index] : null;
    }
}

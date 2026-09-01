using System.Collections.Generic;
using Game.Core;
using Game.Data;
using UnityEngine;

namespace Game.Grid
{
    /// <summary>
    /// Authoritative runtime grid: cell occupancy and world/cell conversion.
    /// Stores an opaque occupant handle per cell rather than a Gameplay type,
    /// since Game.Grid must not depend on Game.Gameplay.
    /// </summary>
    public sealed class GridRuntime
    {
        readonly Dictionary<GridCoord, object> _occupants = new Dictionary<GridCoord, object>();

        public float CellSize { get; }

        public GridRuntime(float cellSize)
        {
            CellSize = cellSize;
        }

        public bool IsOccupied(GridCoord cell) => _occupants.ContainsKey(cell);

        public object GetOccupant(GridCoord cell)
        {
            return _occupants.TryGetValue(cell, out var occupant) ? occupant : null;
        }

        public void SetOccupant(GridCoord cell, object occupant)
        {
            _occupants[cell] = occupant;
        }

        public void ClearOccupant(GridCoord cell)
        {
            _occupants.Remove(cell);
        }

        /// <summary>True when every cell of the footprint (origin = bottom-left cell) is unoccupied.</summary>
        public bool IsAreaFree(GridCoord origin, Vector2Int sizeInCells)
        {
            for (int x = 0; x < sizeInCells.x; x++)
            {
                for (int y = 0; y < sizeInCells.y; y++)
                {
                    if (IsOccupied(new GridCoord(origin.X + x, origin.Y + y)))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Registers the same occupant on every cell of a multi-cell footprint (origin = bottom-left cell).</summary>
        public void SetOccupantFootprint(GridCoord origin, Vector2Int sizeInCells, object occupant)
        {
            for (int x = 0; x < sizeInCells.x; x++)
            {
                for (int y = 0; y < sizeInCells.y; y++)
                {
                    SetOccupant(new GridCoord(origin.X + x, origin.Y + y), occupant);
                }
            }
        }

        public Vector3 CellToWorld(GridCoord cell)
        {
            return new Vector3(cell.X * CellSize, cell.Y * CellSize, 0f);
        }

        /// <summary>Center of the cell in world space (CellToWorld returns the cell's corner, used by the grid-line overlay).</summary>
        public Vector3 CellCenterToWorld(GridCoord cell)
        {
            float half = CellSize * 0.5f;
            return new Vector3(cell.X * CellSize + half, cell.Y * CellSize + half, 0f);
        }

        public GridCoord WorldToCell(Vector3 world)
        {
            int x = Mathf.FloorToInt(world.x / CellSize);
            int y = Mathf.FloorToInt(world.y / CellSize);
            return new GridCoord(x, y);
        }

        /// <summary>Center of a multi-cell footprint (origin = bottom-left cell) in world space.</summary>
        public Vector3 FootprintCenterToWorld(GridCoord origin, Vector2Int sizeInCells)
        {
            float centerX = origin.X * CellSize + sizeInCells.x * CellSize * 0.5f;
            float centerY = origin.Y * CellSize + sizeInCells.y * CellSize * 0.5f;
            return new Vector3(centerX, centerY, 0f);
        }

        /// <summary>
        /// Places a deposit (a world entity, not a building - PROJECT_ARCHITECTURE.md §12) and
        /// registers it as the occupant of its whole footprint. Grid owns the ore/deposit
        /// registry (§7); this is the only place a DepositRuntime is constructed.
        /// </summary>
        public DepositRuntime PlaceDeposit(GridCoord origin, OreDepositDefinition definition)
        {
            var deposit = new DepositRuntime(definition, origin);
            SetOccupantFootprint(origin, definition.FootprintSize, deposit);
            return deposit;
        }
    }
}

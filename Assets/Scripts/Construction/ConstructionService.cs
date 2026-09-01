using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Construction
{
    /// <summary>
    /// Construction tool state and placement orchestration (CONTRACTS.md §8).
    /// Exposes intent-level operations; owns preview/ghost state (selected definition,
    /// preview rotation) but never creates GameObjects - that is Presentation's job.
    /// </summary>
    public sealed class ConstructionService
    {
        readonly GridRuntime _grid;

        public BuildingDefinition Selected { get; private set; }
        public Direction PreviewRotation { get; private set; } = Direction.North;

        public ConstructionService(GridRuntime grid)
        {
            _grid = grid;
        }

        public void SelectBuilding(BuildingDefinition definition)
        {
            Selected = definition;
            PreviewRotation = Direction.North;
        }

        public void Cancel()
        {
            Selected = null;
        }

        public void SetPreviewRotation(Direction rotation)
        {
            PreviewRotation = rotation;
        }

        /// <summary>Non-mutating check used by ghost-preview valid/invalid tinting.</summary>
        public bool CanPlace(GridCoord cell)
        {
            return Selected != null && IsPlaceable(cell);
        }

        /// <summary>
        /// Places the currently selected building at the requested cell/rotation.
        /// Only mutates grid/runtime state - never creates a GameObject.
        /// </summary>
        public bool TryPlace(GridCoord cell, Direction rotation, out BuildingRuntime placed)
        {
            placed = null;

            if (Selected == null || !IsPlaceable(cell))
            {
                return false;
            }

            if (Selected is ConveyorDefinition conveyorDefinition)
            {
                // Overtake exception: placing a conveyor onto an existing conveyor replaces it
                // instead of being blocked by the normal occupancy check (see IsPlaceable).
                if (_grid.GetOccupant(cell) is ConveyorRuntime)
                {
                    _grid.ClearOccupant(cell);
                }

                var conveyor = new ConveyorRuntime(conveyorDefinition, cell, rotation);
                switch (conveyorDefinition.DefaultShape)
                {
                    case ConveyorShapeKind.Straight:
                        conveyor.ConfigureAsStraight(rotation);
                        break;
                    case ConveyorShapeKind.Corner:
                        conveyor.ConfigureAsCornerShape();
                        conveyor.SetRotation(rotation);
                        break;
                    case ConveyorShapeKind.Crossroad:
                        conveyor.ConfigureAsCrossroadShape();
                        conveyor.SetRotation(rotation);
                        break;
                }

                _grid.SetOccupant(cell, conveyor);
                placed = conveyor;
                return true;
            }

            if (Selected is ExtractorDefinition extractorDefinition)
            {
                // Guaranteed by IsPlaceable: only reachable when the whole footprint is the same exploitable deposit.
                var deposit = (DepositRuntime)_grid.GetOccupant(cell);
                var extractor = new ExtractorRuntime(extractorDefinition, cell, rotation, deposit);
                _grid.SetOccupantFootprint(cell, extractorDefinition.FootprintSize, extractor);
                placed = extractor;
                return true;
            }

            if (Selected is StorageDefinition storageDefinition)
            {
                var storage = new StorageRuntime(storageDefinition, cell, rotation);
                _grid.SetOccupantFootprint(cell, storageDefinition.FootprintSize, storage);
                placed = storage;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Demolishes whatever building occupies the cell (its whole footprint, not just the
        /// clicked cell - removed.Cell is always the footprint's origin regardless of which
        /// cell was clicked). Demolishing an Extractor restores the deposit underneath instead
        /// of leaving the cells empty: ore deposits are world/terrain entities, not buildings,
        /// and outlive whatever gets built and later removed on top of them.
        /// </summary>
        public bool TryDemolish(GridCoord cell, out BuildingRuntime removed)
        {
            removed = _grid.GetOccupant(cell) as BuildingRuntime;
            if (removed == null)
            {
                return false;
            }

            Vector2Int footprint = removed.Definition.FootprintSize;

            if (removed is ExtractorRuntime extractor)
            {
                _grid.SetOccupantFootprint(extractor.Cell, footprint, extractor.Deposit);
            }
            else
            {
                for (int x = 0; x < footprint.x; x++)
                {
                    for (int y = 0; y < footprint.y; y++)
                    {
                        _grid.ClearOccupant(new GridCoord(removed.Cell.X + x, removed.Cell.Y + y));
                    }
                }
            }

            return true;
        }

        bool IsPlaceable(GridCoord cell)
        {
            if (Selected is ExtractorDefinition extractorDefinition)
            {
                return IsSameExploitableDeposit(cell, extractorDefinition.FootprintSize);
            }

            object occupant = _grid.GetOccupant(cell);
            if (occupant == null)
            {
                return true;
            }

            return Selected is ConveyorDefinition && occupant is ConveyorRuntime;
        }

        /// <summary>
        /// True when every cell of the footprint (from origin = cell) belongs to the same
        /// exploitable DepositRuntime instance - i.e. the extractor exactly covers one deposit,
        /// never straddling two deposits or partially overlapping empty ground.
        /// </summary>
        bool IsSameExploitableDeposit(GridCoord origin, Vector2Int footprint)
        {
            DepositRuntime deposit = null;

            for (int x = 0; x < footprint.x; x++)
            {
                for (int y = 0; y < footprint.y; y++)
                {
                    if (!(_grid.GetOccupant(new GridCoord(origin.X + x, origin.Y + y)) is DepositRuntime candidate))
                    {
                        return false;
                    }

                    if (deposit == null)
                    {
                        deposit = candidate;
                    }
                    else if (!ReferenceEquals(deposit, candidate))
                    {
                        return false;
                    }
                }
            }

            return deposit != null && deposit.RemainingQuantity > 0;
        }
    }
}

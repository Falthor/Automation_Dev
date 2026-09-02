using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Transport;
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
        readonly ItemDatabase _itemDatabase;
        readonly RecipeDatabase _recipeDatabase;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly TransportSystem _transport;
        readonly CoreRuntime _core;

        public BuildingDefinition Selected { get; private set; }
        public Direction PreviewRotation { get; private set; } = Direction.North;

        public ConstructionService(GridRuntime grid, ItemDatabase itemDatabase, RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem, TransportSystem transport = null, CoreRuntime core = null)
        {
            _grid = grid;
            _itemDatabase = itemDatabase;
            _recipeDatabase = recipeDatabase;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            _transport = transport;
            _core = core;
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
        /// Whether every item in definition.Cost is currently available from Core + every placed
        /// Storage combined. Public so the Building menu can show the same affordability the
        /// placement check itself enforces (CONTRACTS.md §12), without duplicating the
        /// Core+Storage aggregation logic.
        /// </summary>
        public bool CanAfford(BuildingDefinition definition)
        {
            foreach (RecipeIngredient ingredient in definition.Cost)
            {
                if (ingredient.Item == null) continue;
                if (GetAvailableAmount(ingredient.Item.Id) < ingredient.Amount) return false;
            }
            return true;
        }

        /// <summary>Total of one item id currently held by Core plus every placed Storage - the same pool a construction cost draws from.</summary>
        public int GetAvailableAmount(string itemId)
        {
            int total = _core?.GetInputAmount(itemId) ?? 0;
            if (_transport != null)
            {
                foreach (StorageRuntime storage in _transport.Storages)
                {
                    total += storage.GetInputAmount(itemId);
                }
            }
            return total;
        }

        /// <summary>Deducts a definition's cost from Core first, then every Storage in turn (arbitrary but deterministic order). Caller must have checked CanAfford first.</summary>
        void PayCost(BuildingDefinition definition)
        {
            foreach (RecipeIngredient ingredient in definition.Cost)
            {
                if (ingredient.Item == null) continue;

                int remaining = ingredient.Amount;
                if (_core != null)
                {
                    int fromCore = Mathf.Min(remaining, _core.GetInputAmount(ingredient.Item.Id));
                    if (fromCore > 0)
                    {
                        _core.TakeInput(ingredient.Item.Id, fromCore);
                        remaining -= fromCore;
                    }
                }

                if (_transport == null) continue;

                foreach (StorageRuntime storage in _transport.Storages)
                {
                    if (remaining <= 0) break;

                    int fromStorage = Mathf.Min(remaining, storage.GetInputAmount(ingredient.Item.Id));
                    if (fromStorage <= 0) continue;

                    storage.TakeInput(ingredient.Item.Id, fromStorage);
                    remaining -= fromStorage;
                }
            }
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

            PayCost(Selected);

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
                }

                _grid.SetOccupant(cell, conveyor);
                placed = conveyor;
                return true;
            }

            if (Selected is SplitterDefinition splitterDefinition)
            {
                ClearOvertakenConveyors(cell, splitterDefinition.FootprintCells);
                var splitter = new SplitterRuntime(splitterDefinition, cell, rotation);
                _grid.SetOccupantFootprint(cell, splitterDefinition.FootprintCells, splitter);
                placed = splitter;
                return true;
            }

            if (Selected is CrossroadDefinition crossroadDefinition)
            {
                ClearOvertakenConveyors(cell, crossroadDefinition.FootprintCells);
                var crossroad = new CrossroadRuntime(crossroadDefinition, cell, rotation);
                _grid.SetOccupantFootprint(cell, crossroadDefinition.FootprintCells, crossroad);
                placed = crossroad;
                return true;
            }

            if (Selected is ExtractorDefinition extractorDefinition)
            {
                // Guaranteed by IsPlaceable: only reachable when the whole footprint is the same exploitable deposit.
                var deposit = (DepositRuntime)_grid.GetOccupant(cell);
                var extractor = new ExtractorRuntime(extractorDefinition, cell, rotation, deposit, _computeSystem, _powerSystem);
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

            if (Selected is FoundryDefinition foundryDefinition)
            {
                var foundry = new FoundryRuntime(foundryDefinition, cell, rotation, _recipeDatabase, _itemDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, foundryDefinition.FootprintSize, foundry);
                placed = foundry;
                return true;
            }

            if (Selected is FactoryDefinition factoryDefinition)
            {
                var factory = new FactoryRuntime(factoryDefinition, cell, rotation, _recipeDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, factoryDefinition.FootprintSize, factory);
                placed = factory;
                return true;
            }

            if (Selected is AdvancedFoundryDefinition advancedFoundryDefinition)
            {
                var advancedFoundry = new AdvancedFoundryRuntime(advancedFoundryDefinition, cell, rotation, _recipeDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, advancedFoundryDefinition.FootprintSize, advancedFoundry);
                placed = advancedFoundry;
                return true;
            }

            if (Selected is AssemblerDefinition assemblerDefinition)
            {
                var assembler = new AssemblerRuntime(assemblerDefinition, cell, rotation, _recipeDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, assemblerDefinition.FootprintSize, assembler);
                placed = assembler;
                return true;
            }

            if (Selected is PowerplantGazDefinition powerplantGazDefinition)
            {
                var powerplant = new PowerplantGazRuntime(powerplantGazDefinition, cell, rotation, _computeSystem, _powerSystem);
                _grid.SetOccupantFootprint(cell, powerplantGazDefinition.FootprintSize, powerplant);
                placed = powerplant;
                return true;
            }

            if (Selected is LaboratoryDefinition laboratoryDefinition)
            {
                var laboratory = new LaboratoryRuntime(laboratoryDefinition, cell, rotation, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, laboratoryDefinition.FootprintSize, laboratory);
                placed = laboratory;
                return true;
            }

            if (Selected is DataCenterDefinition dataCenterDefinition)
            {
                var dataCenter = new DataCenterRuntime(dataCenterDefinition, cell, rotation, _itemDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, dataCenterDefinition.FootprintSize, dataCenter);
                placed = dataCenter;
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
                _grid.ClearOccupantFootprint(removed.Cell, removed.Definition.FootprintCells);
            }

            return true;
        }

        bool IsPlaceable(GridCoord cell)
        {
            if (Selected.UnlockResearch != null && !_researchSystem.IsUnlocked(Selected.UnlockResearch.Id))
            {
                return false;
            }

            if (!IsWithinActionRadius(cell, Selected.FootprintCells))
            {
                return false;
            }

            if (!CanAfford(Selected))
            {
                return false;
            }

            if (Selected is ExtractorDefinition extractorDefinition)
            {
                return IsSameExploitableDeposit(cell, extractorDefinition.FootprintSize);
            }

            if (Selected is ConveyorDefinition)
            {
                // Conveyors are always 1x1, so a single-cell check (plus the overtake exception)
                // is exhaustive here - every other building must check its whole footprint below.
                object occupant = _grid.GetOccupant(cell);
                return occupant == null || occupant is ConveyorRuntime;
            }

            if (Selected is SplitterDefinition || Selected is CrossroadDefinition)
            {
                // Same overtake exception as a plain conveyor, extended across every cell of the
                // "+" footprint - lets a Splitter/Crossroad be dropped onto existing belt
                // segments (e.g. two straight lines about to cross) instead of requiring the
                // player to demolish them first.
                return IsFootprintPlaceableOverConveyors(cell, Selected.FootprintCells);
            }

            // Checks every cell of the footprint, not just the origin - a building whose origin
            // sits on empty ground but whose footprint extends onto a deposit (or any other
            // occupant) must still be rejected, not just partially overlap it unnoticed.
            return _grid.IsAreaFree(cell, Selected.FootprintSize);
        }

        bool IsFootprintPlaceableOverConveyors(GridCoord origin, Vector2Int[] cells)
        {
            foreach (Vector2Int offset in cells)
            {
                object occupant = _grid.GetOccupant(new GridCoord(origin.X + offset.x, origin.Y + offset.y));
                if (occupant != null && !(occupant is ConveyorRuntime)) return false;
            }
            return true;
        }

        void ClearOvertakenConveyors(GridCoord origin, Vector2Int[] cells)
        {
            foreach (Vector2Int offset in cells)
            {
                var coord = new GridCoord(origin.X + offset.x, origin.Y + offset.y);
                if (_grid.GetOccupant(coord) is ConveyorRuntime) _grid.ClearOccupant(coord);
            }
        }

        /// <summary>
        /// True when every cell of the footprint is within the Core's action radius - a plain
        /// distance-from-Core's-origin-cell check per cell, matching the source project exactly.
        /// No Core in this scene (e.g. a headless test) means no restriction at all.
        /// </summary>
        bool IsWithinActionRadius(GridCoord origin, Vector2Int[] cells)
        {
            if (_core == null) return true;

            var coreDefinition = (CoreDefinition)_core.Definition;
            float radius = coreDefinition.ActionRadiusCells;
            GridCoord coreOrigin = _core.Cell;

            foreach (Vector2Int offset in cells)
            {
                float dx = origin.X + offset.x - coreOrigin.X;
                float dy = origin.Y + offset.y - coreOrigin.Y;
                if (Mathf.Sqrt(dx * dx + dy * dy) > radius) return false;
            }

            return true;
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

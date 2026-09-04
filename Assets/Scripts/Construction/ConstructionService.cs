using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Transport;
using Game.Grid;
using UnityEngine;

namespace Game.Construction
{
    /// <summary>Why a placement is refused - CONTRACTS.md §8's CanPlace/TryPlace stay a plain bool for ghost tinting; this is the explanatory read GetPlacementRefusalReason exposes for player-facing messaging (TASK_04_PLAFOND_RAYON.md §3.2).</summary>
    public enum PlacementRefusalReason
    {
        None,
        NotUnlocked,
        OutOfActionRadius,
        CannotAfford,
        BuildingCapReached,
        CellOccupied
    }

    /// <summary>
    /// Construction tool state and placement orchestration (CONTRACTS.md §8).
    /// Exposes intent-level operations; owns preview/ghost state (selected definition,
    /// preview rotation) but never creates GameObjects - that is Presentation's job.
    /// </summary>
    public sealed class ConstructionService
    {
        public const int DefaultBuildingCap = 40;
        const string MemoryAllocationResearchId = "memory_allocation";
        const int ExtendedBuildingCap = 52;

        readonly GridRuntime _grid;
        readonly ItemDatabase _itemDatabase;
        readonly RecipeDatabase _recipeDatabase;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly TransportSystem _transport;
        readonly CoreRuntime _core;
        readonly PooledItemStock _globalStock;

        public BuildingDefinition Selected { get; private set; }
        public Direction PreviewRotation { get; private set; } = Direction.North;

        /// <summary>
        /// Current building slot cap (TASK_04_PLAFOND_RAYON.md §3) - starts at 40, raised to 52 by
        /// memory_allocation. Runtime state owned here (the same layer that enforces it), not on
        /// any definition; persisted directly by the save layer via RestoreBuildingCap, with a
        /// fallback to DefaultBuildingCap for a save predating this task.
        /// </summary>
        public int BuildingCap { get; private set; } = DefaultBuildingCap;

        /// <summary>
        /// How many currently-placed buildings count against BuildingCap right now - every
        /// registered building except the Core (placed by world generation, not a player decision)
        /// and Conveyor/Splitter/Crossroad (transport pieces, never slot-limited). Computed live
        /// from TransportSystem's registry rather than tracked as a separate counter, so placing
        /// and demolishing can never drift out of sync with it. 0 when there is no TransportSystem
        /// (e.g. a headless test that never registers anything) - no restriction without data,
        /// same convention IsWithinActionRadius already uses for a missing Core.
        /// </summary>
        public int OccupiedBuildingSlots
        {
            get
            {
                if (_transport == null) return 0;

                int count = 0;
                foreach (BuildingRuntime building in _transport.GetAllBuildings())
                {
                    if (ReferenceEquals(building, _core)) continue;
                    if (building is ConveyorRuntime || building is SplitterRuntime || building is CrossroadRuntime) continue;
                    count++;
                }
                return count;
            }
        }

        public ConstructionService(GridRuntime grid, ItemDatabase itemDatabase, RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem, TransportSystem transport = null, CoreRuntime core = null, PooledItemStock globalStock = null)
        {
            _globalStock = globalStock;
            _grid = grid;
            _itemDatabase = itemDatabase;
            _recipeDatabase = recipeDatabase;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            _transport = transport;
            _core = core;

            researchSystem.ResearchCompleted += OnResearchCompleted;
        }

        /// <summary>
        /// No unsubscription: ConstructionService lives exactly as long as its ResearchSystem
        /// (both owned by GameRuntime for the whole session, never demolished/recreated
        /// independently), unlike a BuildingRuntime's OnUnregistered subscription.
        /// </summary>
        void OnResearchCompleted(string researchId)
        {
            if (researchId == MemoryAllocationResearchId) BuildingCap = ExtendedBuildingCap;
        }

        /// <summary>Restores the persisted cap directly (TASK_04_PLAFOND_RAYON.md §6) - never re-derived from ResearchSystem.IsUnlocked, so a future non-research source of extra cap wouldn't need to also be mirrored here. Falls back to DefaultBuildingCap for an absent/older save.</summary>
        public void RestoreBuildingCap(int? cap) => BuildingCap = cap ?? DefaultBuildingCap;

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
        /// Whether every item in definition.Cost is currently available from the player's global
        /// stock + Core + every placed Storage combined. Public so the Building menu can show the
        /// same affordability the placement check itself enforces (CONTRACTS.md §12), without
        /// duplicating the aggregation logic.
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

        /// <summary>Total of one item id currently held by the player's global stock, the Core, every placed Storage and every production building's internal stock (input+output) - the same pool a construction cost draws from.</summary>
        public int GetAvailableAmount(string itemId)
        {
            int total = (_globalStock?.GetAmount(itemId) ?? 0) + (_core?.GetInputAmount(itemId) ?? 0);
            if (_transport != null)
            {
                foreach (StorageRuntime storage in _transport.Storages)
                {
                    total += storage.GetInputAmount(itemId);
                }

                foreach (BuildingRuntime building in _transport.GetAllBuildings())
                {
                    if (!(building is ProductionBuildingRuntime production)) continue;
                    total += production.GetInputAmount(itemId);
                    if (production.GetOutputContents().TryGetValue(itemId, out int outputAmount)) total += outputAmount;
                }
            }
            return total;
        }

        /// <summary>Deducts a definition's cost from the player's global stock first, then Core, then every Storage in turn (arbitrary but deterministic order). Caller must have checked CanAfford first.</summary>
        void PayCost(BuildingDefinition definition)
        {
            foreach (RecipeIngredient ingredient in definition.Cost)
            {
                if (ingredient.Item == null) continue;

                int remaining = ingredient.Amount;
                if (_globalStock != null)
                {
                    remaining -= _globalStock.Take(ingredient.Item.Id, remaining);
                }

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

                if (remaining <= 0) continue;

                foreach (BuildingRuntime building in _transport.GetAllBuildings())
                {
                    if (remaining <= 0) break;
                    if (!(building is ProductionBuildingRuntime production)) continue;

                    int fromInput = Mathf.Min(remaining, production.GetInputAmount(ingredient.Item.Id));
                    if (fromInput > 0)
                    {
                        production.TakeInput(ingredient.Item.Id, fromInput);
                        remaining -= fromInput;
                    }

                    if (remaining <= 0) break;
                    if (!production.GetOutputContents().TryGetValue(ingredient.Item.Id, out int outputAmount)) continue;

                    int fromOutput = Mathf.Min(remaining, outputAmount);
                    if (fromOutput <= 0) continue;

                    production.TakeOutput(ingredient.Item.Id, fromOutput);
                    remaining -= fromOutput;
                }
            }
        }

        /// <summary>Gives a demolished building's construction cost back to the player's global stock (no-op when there is no stock, e.g. a headless test).</summary>
        void RefundCost(BuildingDefinition definition)
        {
            if (_globalStock == null) return;

            foreach (RecipeIngredient ingredient in definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;
                _globalStock.Add(ingredient.Item.Id, ingredient.Amount);
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

            // Overtake exception: placing a conveyor/splitter/crossroad onto existing conveyor
            // segments replaces them instead of being blocked by the normal occupancy check
            // (see IsPlaceable) - lets the player drop a junction piece onto belts already laid.
            if (Selected is ConveyorDefinition && _grid.GetOccupant(cell) is ConveyorRuntime)
            {
                _grid.ClearOccupant(cell);
            }
            else if (Selected is SplitterDefinition || Selected is CrossroadDefinition)
            {
                ClearOvertakenConveyors(cell, Selected.FootprintCells);
            }

            placed = CreateAndRegister(Selected, cell, rotation);
            return placed != null;
        }

        /// <summary>
        /// Reconstructs a previously-placed building from a saved definition/cell/rotation, with
        /// no cost deduction and no placement validity check - both already happened once, at the
        /// original construction time the save captured (CONTRACTS.md §14). The only other caller
        /// allowed to bypass TryPlace's gate; used exclusively by the save/load restore path
        /// (Game.Save.SaveService). The caller is responsible for placing deposits/Core into
        /// Game.Grid first, since an Extractor resolves its deposit from whatever already
        /// occupies its cell, exactly like TryPlace does.
        /// </summary>
        public BuildingRuntime CreateForRestore(BuildingDefinition definition, GridCoord cell, Direction rotation)
        {
            return CreateAndRegister(definition, cell, rotation);
        }

        /// <summary>
        /// Instantiates the concrete runtime type for a definition and registers it as the
        /// occupant of its footprint in Game.Grid. The one place that maps a BuildingDefinition
        /// to its BuildingRuntime subclass - shared by TryPlace (after cost/placement checks) and
        /// CreateForRestore (after the save/load layer already knows placement was once valid).
        /// </summary>
        BuildingRuntime CreateAndRegister(BuildingDefinition definition, GridCoord cell, Direction rotation)
        {
            if (definition is ConveyorDefinition conveyorDefinition)
            {
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
                return conveyor;
            }

            if (definition is SplitterDefinition splitterDefinition)
            {
                var splitter = new SplitterRuntime(splitterDefinition, cell, rotation);
                _grid.SetOccupantFootprint(cell, splitterDefinition.FootprintCells, splitter);
                return splitter;
            }

            if (definition is CrossroadDefinition crossroadDefinition)
            {
                var crossroad = new CrossroadRuntime(crossroadDefinition, cell, rotation);
                _grid.SetOccupantFootprint(cell, crossroadDefinition.FootprintCells, crossroad);
                return crossroad;
            }

            if (definition is ExtractorDefinition extractorDefinition)
            {
                // Guaranteed by IsPlaceable during interactive TryPlace: only reachable when the
                // whole footprint is the same exploitable deposit. During restore the caller has
                // already placed the matching deposit at this cell before calling us.
                var deposit = _grid.GetOccupant(cell) as DepositRuntime;
                var extractor = new ExtractorRuntime(extractorDefinition, cell, rotation, deposit, _computeSystem, _powerSystem);
                _grid.SetOccupantFootprint(cell, extractorDefinition.FootprintSize, extractor);
                return extractor;
            }

            if (definition is StorageDefinition storageDefinition)
            {
                var storage = new StorageRuntime(storageDefinition, cell, rotation);
                _grid.SetOccupantFootprint(cell, storageDefinition.FootprintSize, storage);
                return storage;
            }

            if (definition is FoundryDefinition foundryDefinition)
            {
                var foundry = new FoundryRuntime(foundryDefinition, cell, rotation, _recipeDatabase, _itemDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, foundryDefinition.FootprintSize, foundry);
                return foundry;
            }

            if (definition is FactoryDefinition factoryDefinition)
            {
                var factory = new FactoryRuntime(factoryDefinition, cell, rotation, _recipeDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, factoryDefinition.FootprintSize, factory);
                return factory;
            }

            if (definition is AdvancedFoundryDefinition advancedFoundryDefinition)
            {
                var advancedFoundry = new AdvancedFoundryRuntime(advancedFoundryDefinition, cell, rotation, _recipeDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, advancedFoundryDefinition.FootprintSize, advancedFoundry);
                return advancedFoundry;
            }

            if (definition is AssemblerDefinition assemblerDefinition)
            {
                var assembler = new AssemblerRuntime(assemblerDefinition, cell, rotation, _recipeDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, assemblerDefinition.FootprintSize, assembler);
                return assembler;
            }

            if (definition is PowerplantGazDefinition powerplantGazDefinition)
            {
                var powerplant = new PowerplantGazRuntime(powerplantGazDefinition, cell, rotation, _computeSystem, _powerSystem);
                _grid.SetOccupantFootprint(cell, powerplantGazDefinition.FootprintSize, powerplant);
                return powerplant;
            }

            if (definition is DataCenterDefinition dataCenterDefinition)
            {
                var dataCenter = new DataCenterRuntime(dataCenterDefinition, cell, rotation, _itemDatabase, _computeSystem, _powerSystem, _researchSystem);
                _grid.SetOccupantFootprint(cell, dataCenterDefinition.FootprintSize, dataCenter);
                return dataCenter;
            }

            return null;
        }

        /// <summary>
        /// Demolishes whatever building occupies the cell (its whole footprint, not just the
        /// clicked cell - removed.Cell is always the footprint's origin regardless of which
        /// cell was clicked). Demolishing an Extractor restores the deposit underneath instead
        /// of leaving the cells empty: ore deposits are world/terrain entities, not buildings,
        /// and outlive whatever gets built and later removed on top of them.
        ///
        /// The building's full construction cost is refunded into the player's global stock -
        /// the pool PayCost draws from first - so building and removing is cost-neutral.
        /// </summary>
        public bool TryDemolish(GridCoord cell, out BuildingRuntime removed)
        {
            removed = _grid.GetOccupant(cell) as BuildingRuntime;
            if (removed == null)
            {
                return false;
            }

            RefundCost(removed.Definition);

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

        bool IsPlaceable(GridCoord cell) => GetPlacementRefusalReason(cell) == PlacementRefusalReason.None;

        /// <summary>
        /// Single source of truth for every placement gate, driving both CanPlace (ghost tinting,
        /// bool only) and this explanatory read (TASK_04_PLAFOND_RAYON.md §3.2 - a refusal at the
        /// building cap must name that cause, not fail silently or generically). Meaningful only
        /// while Selected != null; callers check that themselves via CanPlace/TryPlace first.
        /// </summary>
        public PlacementRefusalReason GetPlacementRefusalReason(GridCoord cell)
        {
            if (Selected.UnlockResearch != null && !_researchSystem.IsUnlocked(Selected.UnlockResearch.Id))
            {
                return PlacementRefusalReason.NotUnlocked;
            }

            if (!IsWithinActionRadius(cell, Selected.FootprintCells))
            {
                return PlacementRefusalReason.OutOfActionRadius;
            }

            if (!CanAfford(Selected))
            {
                return PlacementRefusalReason.CannotAfford;
            }

            bool countsAgainstCap = !(Selected is ConveyorDefinition || Selected is SplitterDefinition || Selected is CrossroadDefinition);
            if (countsAgainstCap && OccupiedBuildingSlots >= BuildingCap)
            {
                return PlacementRefusalReason.BuildingCapReached;
            }

            if (Selected is ExtractorDefinition extractorDefinition)
            {
                return IsSameExploitableDeposit(cell, extractorDefinition.FootprintSize) ? PlacementRefusalReason.None : PlacementRefusalReason.CellOccupied;
            }

            if (Selected is ConveyorDefinition)
            {
                // Conveyors are always 1x1, so a single-cell check (plus the overtake exception)
                // is exhaustive here - every other building must check its whole footprint below.
                object occupant = _grid.GetOccupant(cell);
                return occupant == null || occupant is ConveyorRuntime ? PlacementRefusalReason.None : PlacementRefusalReason.CellOccupied;
            }

            if (Selected is SplitterDefinition || Selected is CrossroadDefinition)
            {
                // Same overtake exception as a plain conveyor, extended across every cell of the
                // "+" footprint - lets a Splitter/Crossroad be dropped onto existing belt
                // segments (e.g. two straight lines about to cross) instead of requiring the
                // player to demolish them first.
                return IsFootprintPlaceableOverConveyors(cell, Selected.FootprintCells) ? PlacementRefusalReason.None : PlacementRefusalReason.CellOccupied;
            }

            // Checks every cell of the footprint, not just the origin - a building whose origin
            // sits on empty ground but whose footprint extends onto a deposit (or any other
            // occupant) must still be rejected, not just partially overlap it unnoticed.
            return _grid.IsAreaFree(cell, Selected.FootprintSize) ? PlacementRefusalReason.None : PlacementRefusalReason.CellOccupied;
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
        /// No Core in this scene (e.g. a headless test) means no restriction at all. Reads
        /// _core.ActionRadiusCells (runtime, extendable by research), never CoreDefinition's own
        /// ActionRadiusCells (the starting value only) - TASK_04_PLAFOND_RAYON.md §4.1/§4.3.
        /// </summary>
        bool IsWithinActionRadius(GridCoord origin, Vector2Int[] cells)
        {
            if (_core == null) return true;

            float radius = _core.ActionRadiusCells;
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

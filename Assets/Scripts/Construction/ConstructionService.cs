using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
using Game.Gameplay.Transport;
using Game.Grid;
using UnityEngine;

namespace Game.Construction
{
    /// <summary>
    /// Why a placement is refused - CONTRACTS.md §8's CanPlace/TryPlace stay a plain bool for
    /// ghost tinting; this is the explanatory read GetPlacementRefusalReason exposes for
    /// player-facing messaging (TASK_04_PLAFOND_RAYON.md §3.2).
    ///
    /// CannotAfford is a real refusal: a building whose materials do not exist cannot be placed at
    /// all. Placing still does not PAY - it opens a site that reserves the whole bill and waits for
    /// robots to carry it - but the bill must be coverable by unreserved stock at that instant, so a
    /// site is never opened against material nobody has.
    /// </summary>
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
        public const int DefaultBuildingCap = 30;
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
        readonly ConstructionSiteSystem _constructionSites;

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
        /// How many building slots are taken against BuildingCap right now - every registered
        /// building except the Core (placed by world generation, not a player decision) and
        /// Conveyor/Splitter/Crossroad (transport pieces, never slot-limited), plus every pending
        /// construction site of a slot-consuming type. A site counts from the moment it is placed
        /// rather than only once its materials arrive (TASK_05_ROBOT_CONSTRUCTEUR.md): otherwise
        /// the cap could be walked straight past by queueing sites faster than robots can serve
        /// them. Computed live from TransportSystem's registry plus the site queue rather than
        /// tracked as a separate counter, so placing/cancelling/demolishing can never drift out of
        /// sync with it. 0 when there is no TransportSystem (e.g. a headless test that never
        /// registers anything) - no restriction without data, the same convention
        /// IsWithinActionRadius already uses for a missing Core.
        /// </summary>
        public int OccupiedBuildingSlots
        {
            get
            {
                int count = _constructionSites?.OccupiedSiteSlots ?? 0;
                if (_transport == null) return count;

                foreach (BuildingRuntime building in _transport.GetAllBuildings())
                {
                    if (ReferenceEquals(building, _core)) continue;
                    // The Core chest is a world-generated fixture like the Core itself, never a
                    // player decision (TASK_05_ROBOT_CONSTRUCTEUR.md §1b) - a player-built Storage
                    // Box still counts.
                    if (building.Definition.Id == CoreStorageDefinitionId) continue;
                    if (!building.Definition.CountsAgainstBuildingCap) continue;
                    count++;
                }
                return count;
            }
        }

        public ConstructionService(GridRuntime grid, ItemDatabase itemDatabase, RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem, TransportSystem transport = null, CoreRuntime core = null, ConstructionSiteSystem constructionSites = null)
        {
            _constructionSites = constructionSites;
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
        /// Whether every item in definition.Cost is available right now across the aggregate a robot
        /// could actually draw from - unreserved stock only, so material another site has already
        /// claimed does not count towards this one.
        ///
        /// Both the Building menu's "you can/cannot pay for this yet" styling AND the placement gate
        /// (see PlacementRefusalReason.CannotAfford) read it, so the greyed-out card and the refused
        /// click can never disagree about what is affordable.
        ///
        /// All-or-nothing per definition: there is no partial placement. A site is therefore opened
        /// only when its ENTIRE bill can be reserved on the spot, which is what makes a placed site's
        /// missing count zero in ordinary play.
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

        /// <summary>
        /// How much of one item id is still unreserved across the Core chest, every placed Storage
        /// and every production building's output - i.e. GlobalStock's new read-only aggregate
        /// (TASK_05_ROBOT_CONSTRUCTEUR.md §1), the single source of truth for what a robot could
        /// still be sent to fetch. This service no longer aggregates that itself: it asks
        /// ConstructionSiteSystem, which also owns the reservations that must be subtracted.
        /// </summary>
        public int GetAvailableAmount(string itemId)
        {
            if (_constructionSites == null) return 0;
            return _constructionSites.GetAvailableAggregate().TryGetValue(itemId, out int amount) ? amount : 0;
        }

        /// <summary>
        /// Opens a construction site for the currently selected building at the requested
        /// cell/rotation (TASK_05_ROBOT_CONSTRUCTEUR.md §3/§7). Nothing is paid here and nothing
        /// becomes functional: the BuildingRuntime is instantiated and occupies its grid cells
        /// immediately (so nothing else can be placed on top of it, and a conveyor drag can keep
        /// reshaping its anchor exactly as before), but it is deliberately NOT registered with
        /// TransportSystem and has no view - it neither ticks, transports nor produces until
        /// ConstructionSiteSystem materializes it, once robots have delivered its full cost.
        ///
        /// Passing an existing conveyorRunSite appends this cell to that site instead of opening a
        /// new one - a whole conveyor drag is one single chantier, not one per segment (§3).
        ///
        /// Only mutates grid/runtime state - never creates a GameObject.
        /// </summary>
        public bool TryPlace(GridCoord cell, Direction rotation, out ConstructionSiteRuntime site, ConstructionSiteRuntime conveyorRunSite = null)
        {
            site = null;

            if (Selected == null || _constructionSites == null || !IsPlaceable(cell))
            {
                return false;
            }

            ReleaseOvertakenConveyors(cell);

            BuildingRuntime segment = CreateAndRegister(Selected, cell, rotation);
            if (segment == null) return false;

            if (conveyorRunSite != null)
            {
                _constructionSites.AppendSegment(conveyorRunSite, segment);
                site = conveyorRunSite;
            }
            else
            {
                site = _constructionSites.CreateSite(segment);
            }

            return true;
        }

        /// <summary>
        /// Cancels whatever unbuilt segment holds this cell, if any (TASK_05_ROBOT_CONSTRUCTEUR.md
        /// §4): its earmarks go back to their containers and its ground is freed.
        ///
        /// Scoped to the one segment under the cursor rather than to its whole chantier. A drag lays
        /// one site across many belts, and cancelling all of them because the player right-clicked
        /// the third meant a run could only ever be undone whole. A single building is one segment,
        /// so for it this is still the cancellation it always was.
        /// </summary>
        public bool TryCancelPendingAt(GridCoord cell)
        {
            if (_constructionSites == null) return false;
            if (!(_grid.GetOccupant(cell) is BuildingRuntime occupant)) return false;

            // False for anything that is not an unbuilt segment - a finished building, a deposit -
            // which is exactly how the demolition input falls through to TryDemolish.
            return _constructionSites.CancelPendingSegment(occupant);
        }

        /// <summary>
        /// Re-points a belt that already exists instead of replacing it, and answers whether it did.
        ///
        /// Dragging across a belt that is already built is a change of direction, not a demolition
        /// followed by a new chantier. The belt is there and it is paid for: rebuilding it charged a
        /// second plate, destroyed the first one silently, and dropped a working belt back to a blue
        /// silhouette until a robot came round - for a gesture whose whole intent was "this one goes
        /// that way now". Nothing is spent here and no site is opened; the items riding it keep
        /// riding it.
        ///
        /// Only a belt already delivered qualifies. One still pending is a segment of somebody's
        /// chantier and belongs to TryDetachPendingSegment, and anything that is not a conveyor at
        /// all was already refused by the occupancy check.
        /// </summary>
        public bool TryRedirectExistingConveyor(GridCoord cell, Direction rotation, out ConveyorRuntime redirected)
        {
            redirected = null;

            if (!(Selected is ConveyorDefinition definition)) return false;
            if (!(_grid.GetOccupant(cell) is ConveyorRuntime existing)) return false;
            if (_constructionSites != null && _constructionSites.TryGetSiteContaining(existing, out _)) return false;

            // Every gate still applies except affordability, which is the one a redirect has no
            // business reading: it spends nothing, so an empty chest is no reason to refuse turning
            // a belt the player already owns.
            PlacementRefusalReason refusal = GetPlacementRefusalReason(cell);
            if (refusal != PlacementRefusalReason.None && refusal != PlacementRefusalReason.CannotAfford) return false;

            // The same shaping CreateAndRegister would have given a fresh one, applied in place, so
            // a redirected belt and a rebuilt one are never a different belt.
            if (definition.DefaultShape == ConveyorShapeKind.Corner)
            {
                existing.ConfigureAsCornerShape();
                existing.SetRotation(rotation);
            }
            else
            {
                existing.ConfigureAsStraight(rotation);
            }

            redirected = existing;
            return true;
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
        /// clicked cell - removed.Cell is always the footprint's origin regardless of which cell was
        /// clicked). What the ground gets back is BuildingRuntime.ReleaseFootprint's business, not
        /// this method's - an Extractor puts its deposit back rather than leaving empty cells, and
        /// stating that rule here as well as in the cancellation path is how the two came to
        /// disagree in the first place.
        ///
        /// The building disappears immediately - the player wants the space back, which is usually
        /// the whole point of demolishing - but its materials are no longer refunded anywhere on
        /// the spot: a robot must physically haul them back to the Core chest or a Storage
        /// (TASK_05_ROBOT_CONSTRUCTEUR.md §5). A still-pending construction site is never
        /// demolished through here (its building was never paid for); the caller routes that to
        /// TryCancelPendingAt instead.
        /// </summary>
        public bool TryDemolish(GridCoord cell, out BuildingRuntime removed)
        {
            removed = _grid.GetOccupant(cell) as BuildingRuntime;
            if (removed == null || IsProtectedFromDemolition(removed))
            {
                removed = null;
                return false;
            }

            if (_constructionSites != null && _constructionSites.TryGetSiteContaining(removed, out _))
            {
                removed = null;
                return false;
            }

            _constructionSites?.EnqueueRepatriation(removed.Cell, removed.Definition.Cost);

            BuildingRuntime.ReleaseFootprint(_grid, removed);

            return true;
        }

        /// <summary>
        /// World-generated fixtures the player never placed and must never be able to remove: the
        /// Core itself, and the Storage Box holding the starting resources one cell south of it
        /// (WorldGenerator.CoreStorage). Matched by definition id rather than a tracked instance
        /// reference for the Storage box, so the guard still holds after a save/load - it comes
        /// back as an ordinary entry in the restored building list, and this service never gets a
        /// fresh reference to it then. Demolishing the Core was already unreachable in practice,
        /// but nothing previously stopped it explicitly.
        /// </summary>
        static bool IsProtectedFromDemolition(BuildingRuntime building) =>
            building is CoreRuntime || building.Definition.Id == CoreStorageDefinitionId;

        const string CoreStorageDefinitionId = "core_storage";

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

            // Read against the aggregate MINUS what other sites have already reserved, so placing
            // four buildings with stock for three refuses the fourth: the first three took their
            // material out of what is claimable the instant they were placed. Without that
            // subtraction the gate would pass four times over one stock and the sites would fight
            // over it afterwards, which is the thing the gate exists to prevent.
            if (!CanAfford(Selected))
            {
                return PlacementRefusalReason.CannotAfford;
            }

            if (Selected.CountsAgainstBuildingCap && OccupiedBuildingSlots >= BuildingCap)
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

        /// <summary>
        /// Frees the conveyor cells this placement is about to take, and settles what each of them
        /// owes. The overtake exception (see IsPlaceable) lets a Conveyor/Splitter/Crossroad be
        /// dropped onto belts already laid instead of forcing the player to demolish them first;
        /// this is where that is paid for.
        ///
        /// A belt <b>still pending</b> was paid for by nobody: it simply leaves its chantier, and
        /// only it - the rest of a dragged run keeps its own ground
        /// (ConstructionSiteSystem.RemovePendingSegment).
        ///
        /// A belt <b>already built</b> is being demolished, so its cost becomes a repatriation job a
        /// robot hauls back, exactly like TryDemolish. It used to be dropped where it stood, which
        /// destroyed the material silently - the one thing demolition is careful never to do.
        ///
        /// Runs after the placement is known to be valid, never before: a refused placement must not
        /// cost the player the belts that were already there. None of the gates it reads changes by
        /// removing them - a belt counts against no cap, and a demolition credits no stock.
        ///
        /// Only conveyors are ever overtaken; every other occupant was already refused.
        /// </summary>
        void ReleaseOvertakenConveyors(GridCoord origin)
        {
            if (!(Selected is ConveyorDefinition || Selected is SplitterDefinition || Selected is CrossroadDefinition)) return;

            foreach (Vector2Int offset in Selected.FootprintCells)
            {
                var coord = new GridCoord(origin.X + offset.x, origin.Y + offset.y);
                if (!(_grid.GetOccupant(coord) is ConveyorRuntime overtaken)) continue;

                if (!_constructionSites.RemovePendingSegment(overtaken))
                {
                    _constructionSites.EnqueueRepatriation(coord, overtaken.Definition.Cost);
                }

                _grid.ClearOccupant(coord);
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

            // No "still has ore left" test: a deposit never runs out (ALIGNEMENT_PROJET.md §8).
            return deposit != null;
        }
    }
}

using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using Game.Grid;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>
    /// Raw mouse/keyboard polling adapter: translates device input into ConstructionService
    /// calls and drives the ghost preview + spawned views. No .inputactions asset needed for
    /// this pass. Building selection itself comes from the Building menu / Bottom Nav toolbar
    /// (Game.UI); this adapter only owns R (rotate preview), Esc (cancel), left click/drag
    /// (place), right click/drag (demolish, also cancels an armed tool).
    /// </summary>
    public sealed class ConstructionInputAdapter : MonoBehaviour
    {
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Camera worldCamera;
        [SerializeField] ConveyorGhostView ghostView;
        [SerializeField] BuildingGhostView buildingGhostView;
        [SerializeField] BuildingHoverHighlightView hoverHighlightView;
        [SerializeField] DepositHoverGlowView depositHoverGlowView;

        /// <summary>
        /// Raised when a placement attempt is refused specifically because the building cap was
        /// reached (TASK_04_PLAFOND_RAYON.md §3.2 - this refusal must name its cause explicitly,
        /// not fail silently). Game.Presentation must not depend on Game.UI (PROJECT_ARCHITECTURE.md
        /// §4's dependency direction), so this is a plain event a UI-layer listener (TopBarController)
        /// subscribes to instead of a direct reference the other way.
        /// </summary>
        public event System.Action<string> PlacementRefusedAtBuildingCap;

        /// <summary>
        /// Used to lay straight segments while dragging with the Corner tool selected - a corner
        /// is only ever stamped at the anchor (initial click) or at a later turn, never repeated
        /// along a straight run. See PlaceStraightSegment. Also passed to BuildingSpawner so a
        /// conveyor reshaped into a straight (e.g. a corner drag-turned back straight) still
        /// shows this art instead of the procedural placeholder - see
        /// BuildingSpawner.ResolveConveyorArtDefinition.
        /// </summary>
        [SerializeField] ConveyorDefinition straightConveyorForDragContinuation;

        /// <summary>
        /// Passed to BuildingSpawner so a conveyor reshaped into a corner (drag-turned from the
        /// Straight tool, or from the Corner tool re-pointed at a later turn) shows real corner
        /// art - its own Definition stays whichever tool originally placed it, which no longer
        /// matches its current Orientation.Shape after a reshape. See
        /// BuildingSpawner.ResolveConveyorArtDefinition.
        /// </summary>
        [SerializeField] ConveyorDefinition cornerConveyorForReshape;

        static readonly Color OutputArrowColor = new Color(0.25f, 0.95f, 0.35f, 1f);
        static readonly Color InputArrowColor = new Color(0.3f, 0.6f, 1f, 1f);

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        BuildingSpawner _spawner;

        // Axis-lock drag: the axis (horizontal/vertical) locks automatically from the first
        // mouse movement after the anchor cell, and placement follows the mouse's projection
        // onto that axis, ignoring drift off it - matching the reference Godot behavior.
        // Holding Ctrl while the mouse sits exactly on the last placed cell "drops" a new
        // anchor there and unlocks the axis, so the next movement can pick a different one
        // (turning the drop point into a corner if the new axis differs from the old one).
        bool _isDragPlacing;
        GridCoord _dragAnchorCell;
        GridCoord _lastPlacedCell;
        Direction? _dragAxis;
        Direction? _pendingCornerEntry;

        /// <summary>
        /// The single construction site every cell of the current conveyor/splitter drag is
        /// appended to - a whole gesture is one chantier, not one per segment
        /// (TASK_05_ROBOT_CONSTRUCTEUR.md §3), so a fifty-belt line is seven robot waves rather
        /// than fifty separate sites. Cleared on mouse-up: the next drag opens a new site.
        /// </summary>
        ConstructionSiteRuntime _activeConveyorSite;

        bool _subscribedToMaterialization;

        bool _isDragDemolishing;
        GridCoord _lastDemolishedCell;

        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

        void Start()
        {
            // Cross-object wiring belongs in Start(), not Awake(): Awake ordering between
            // GameRuntime and this adapter is not guaranteed, but Start always runs after
            // every object's Awake, so gameRuntime.Grid is guaranteed to be initialized here.
            if (worldCamera == null) worldCamera = Camera.main;
            if (hoverHighlightView != null) hoverHighlightView.Initialize(gameRuntime.Grid);
            if (depositHoverGlowView != null) depositHoverGlowView.Initialize(gameRuntime.Grid, _spriteFactory);

            // _spawner is NOT built here: it needs gameRuntime.GroundSlabSettings, which
            // GameRuntime only populates partway through its own Start() (after TerrainView.
            // Initialize runs) - and Unity does not guarantee Start() order between different
            // components (unlike Awake, which gameRuntime.Grid above already relies on). Built
            // lazily on first Update() instead, by which point every object's Start() has run.
        }

        void Update()
        {
            if (worldCamera == null || gameRuntime == null) return;
            if (_spawner == null)
            {
                _spawner = new BuildingSpawner(gameRuntime.Grid, _spriteFactory, straightConveyorForDragContinuation, cornerConveyorForReshape, gameRuntime.GroundSlabSettings, gameRuntime.GroundSlabNeighborLinker);
            }

            // Subscribed here rather than in Start() for the same reason _spawner is built lazily:
            // it needs the fully-configured spawner (conveyor art definitions included) to exist.
            if (!_subscribedToMaterialization && gameRuntime.ConstructionSites != null)
            {
                gameRuntime.ConstructionSites.SegmentMaterialized += OnSegmentMaterialized;

                // The one spawner of the scene, lent to the site views so they can finish a
                // segment's assembly and swap in the real building themselves. A second spawner
                // built over there would keep its own per-cell view dictionary, and demolition
                // would stop finding views created by the other.
                if (gameRuntime.ConstructionSiteVisuals != null) gameRuntime.ConstructionSiteVisuals.SetViewSpawner(_spawner.SpawnView);

                _subscribedToMaterialization = true;
            }

            // A UI panel (Building menu, Storage panel, ...) owns mouse/keyboard input while
            // open, and for one extra frame after it closes - otherwise the same click that
            // selected a menu item or closed a panel also lands on the world underneath it.
            if (gameRuntime.IsUIBlockingInput || gameRuntime.LastMenuCloseFrame == Time.frameCount) return;

            HandleRotateAndCancel();

            GridCoord cellUnderMouse = CellUnderMouse();
            UpdateGhost(cellUnderMouse);
            HandleHoverHighlight(cellUnderMouse);
            HandlePlacement(cellUnderMouse);
            HandleDemolition(cellUnderMouse);
        }

        /// <summary>
        /// Outlines the footprint of whatever building sits under the mouse, active both with
        /// and without a construction tool armed (only suppressed while a UI panel owns input,
        /// handled by the early-return above).
        /// </summary>
        void HandleHoverHighlight(GridCoord cell)
        {
            object occupant = gameRuntime.Grid.GetOccupant(cell);

            if (hoverHighlightView != null)
            {
                if (occupant is BuildingRuntime building)
                {
                    hoverHighlightView.Show(building.Cell, building.Definition.FootprintSize);
                }
                else
                {
                    hoverHighlightView.Hide();
                }
            }

            if (depositHoverGlowView != null)
            {
                if (occupant is DepositRuntime deposit)
                {
                    depositHoverGlowView.Show(deposit.Origin, deposit.Definition.FootprintSize);
                }
                else
                {
                    depositHoverGlowView.Hide();
                }
            }
        }

        void HandleRotateAndCancel()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.rKey.wasPressedThisFrame)
            {
                Direction next = gameRuntime.Construction.PreviewRotation.RotateCW(1);
                gameRuntime.Construction.SetPreviewRotation(next);
            }

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                gameRuntime.Construction.Cancel();
            }
        }

        GridCoord CellUnderMouse()
        {
            Vector2 screenPos = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -worldCamera.transform.position.z));
            return gameRuntime.Grid.WorldToCell(world);
        }

        void UpdateGhost(GridCoord cell)
        {
            BuildingDefinition selected = gameRuntime.Construction.Selected;

            if (selected is ConveyorDefinition conveyorDefinition)
            {
                if (buildingGhostView != null) buildingGhostView.Hide();
                if (ghostView == null) return;

                bool conveyorValid = gameRuntime.Construction.CanPlace(cell);
                Vector3 conveyorWorldPos = gameRuntime.Grid.CellCenterToWorld(cell);
                ghostView.Show(_spriteFactory, conveyorDefinition, gameRuntime.Construction.PreviewRotation, conveyorWorldPos, conveyorValid);
                return;
            }

            if (ghostView != null) ghostView.Hide();

            if (selected == null || buildingGhostView == null)
            {
                if (buildingGhostView != null) buildingGhostView.Hide();
                return;
            }

            bool valid = gameRuntime.Construction.CanPlace(cell);
            Vector3 worldCenter = gameRuntime.Grid.FootprintCenterToWorld(cell, selected.FootprintSize);
            Vector2 worldSize = new Vector2(gameRuntime.Grid.CellSize, gameRuntime.Grid.CellSize) * selected.FootprintSize;
            Sprite sprite = ResolveGhostSprite(selected);
            Direction previewRotation = gameRuntime.Construction.PreviewRotation;
            (bool rotateSprite, Direction artNativeDirection) = ResolveGhostRotation(selected);

            // Output and entry arrows are independent: a building can take deliveries without
            // producing anything physical (DataCenter), so each side is previewed on its own.
            Sprite outputArrowSprite = null;
            Vector3? outputArrowWorldPos = null;
            if (selected.HasOutputArrow)
            {
                GridCoord outputCell = BuildingRuntime.ComputeOutputCells(cell, selected.FootprintSize, previewRotation)[0];
                outputArrowWorldPos = gameRuntime.Grid.CellCenterToWorld(outputCell);
                outputArrowSprite = _spriteFactory.CreateArrowSprite(OutputArrowColor);
            }

            Sprite inputArrowSprite = null;
            List<(Vector3 position, Direction direction)> inputArrows = null;
            if (selected.HasInputArrows)
            {
                inputArrowSprite = _spriteFactory.CreateArrowSprite(InputArrowColor);
                inputArrows = new List<(Vector3, Direction)>();
                foreach ((GridCoord edgeCell, Direction fromMySide) in BuildingRuntime.ComputeInputCells(cell, selected.FootprintSize, previewRotation))
                {
                    inputArrows.Add((gameRuntime.Grid.CellCenterToWorld(edgeCell), fromMySide));
                }
            }

            buildingGhostView.Show(sprite, worldSize, worldCenter, previewRotation, valid,
                outputArrowSprite, outputArrowWorldPos, gameRuntime.Grid.CellSize * 0.4f,
                inputArrowSprite, inputArrows, rotateSprite, artNativeDirection);
        }

        Sprite ResolveGhostSprite(BuildingDefinition definition)
        {
            return definition.Sprite != null ? definition.Sprite : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
        }

        /// <summary>
        /// Whether the ghost's sprite itself must rotate to match the real built view. Only the
        /// "+"-shaped Splitter/Crossroad rotate their sprite (SpawnRotatingCrossView) - every other
        /// building's root never rotates (SpawnStandardView), so the ghost mustn't either.
        /// </summary>
        static (bool rotateSprite, Direction artNativeDirection) ResolveGhostRotation(BuildingDefinition definition)
        {
            if (definition is SplitterDefinition splitter) return (true, splitter.ArtNativeEntrySide);
            if (definition is CrossroadDefinition) return (true, Direction.North);
            return (false, Direction.North);
        }

        void HandlePlacement(GridCoord cell)
        {
            var mouse = Mouse.current;
            if (mouse == null || gameRuntime.Construction.Selected == null) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                // A conveyor being placed (fresh ground, or overtaking an existing one) inherits
                // the direction of whichever neighbor already flows into this cell. That same
                // entry direction is kept as _pendingCornerEntry so that once the drag's actual
                // axis becomes known (first movement), this anchor cell gets reshaped to match
                // it exactly - straight if collinear, a corner otherwise - instead of being
                // stuck with whatever rotation it happened to be given at the initial click.
                Direction? entryDirection = gameRuntime.Construction.Selected is ConveyorDefinition
                    ? FindEntryDirection(cell)
                    : null;
                Direction rotation = entryDirection.HasValue ? entryDirection.Value.Opposite() : gameRuntime.Construction.PreviewRotation;

                _activeConveyorSite = null; // a new gesture always opens its own chantier
                PlaceAt(cell, rotation);
                _isDragPlacing = true;
                _dragAnchorCell = cell;
                _lastPlacedCell = cell;
                _dragAxis = null;
                _pendingCornerEntry = entryDirection;
            }
            else if (_isDragPlacing && mouse.leftButton.isPressed)
            {
                HandleAxisDropRequest(cell);
                AdvanceLockedAxisDrag(cell);
            }

            if (mouse.leftButton.wasReleasedThisFrame)
            {
                _isDragPlacing = false;
                _dragAxis = null;
                _pendingCornerEntry = null;
                _activeConveyorSite = null;
            }
        }

        /// <summary>
        /// Ctrl, pressed while the mouse sits exactly on the last placed cell, drops a fresh
        /// anchor there and unlocks the axis so the next movement can pick a different one.
        /// </summary>
        void HandleAxisDropRequest(GridCoord cell)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || !_dragAxis.HasValue || cell != _lastPlacedCell) return;

            bool ctrlPressed = keyboard.leftCtrlKey.wasPressedThisFrame || keyboard.rightCtrlKey.wasPressedThisFrame;
            if (!ctrlPressed) return;

            _pendingCornerEntry = _dragAxis.Value.Opposite();
            _dragAxis = null;
            _dragAnchorCell = _lastPlacedCell;
        }

        /// <summary>
        /// The neighbor (if any) whose own configured output points exactly into `cell`,
        /// expressed as the direction FROM that neighbor TOWARD cell - i.e. the direction flow
        /// naturally enters from. Unlike a plain "is there any conveyor next door" check, this
        /// requires actual alignment (GetOutputCell() == cell), so a neighbor pointed elsewhere
        /// is correctly ignored instead of producing a bogus inherited direction.
        /// </summary>
        Direction? FindEntryDirection(GridCoord cell)
        {
            foreach (Direction dir in AllDirections)
            {
                GridCoord neighborCell = cell + dir;
                if (gameRuntime.Grid.GetOccupant(neighborCell) is BuildingRuntime candidate && candidate.GetOutputCell() == cell)
                {
                    return dir;
                }
            }

            return null;
        }

        /// <summary>
        /// Projects the mouse's current cell onto the locked axis (dot product against the
        /// axis unit offset) and places any newly-covered cells along it. Perpendicular drift
        /// off the axis is ignored entirely, and the projected distance is clamped at 0 so the
        /// run only ever grows forward from the anchor, never retracts.
        /// </summary>
        void AdvanceLockedAxisDrag(GridCoord cell)
        {
            GridCoord rawDelta = new GridCoord(cell.X - _dragAnchorCell.X, cell.Y - _dragAnchorCell.Y);

            if (_dragAxis == null)
            {
                if (rawDelta.X == 0 && rawDelta.Y == 0) return;

                Direction newAxis = DominantDirection(rawDelta);

                // Both conveyor tools share the same auto-corner-on-turn reshape for the anchor -
                // only what it started as differs. The straight tool's anchor starts straight and
                // only becomes a corner if the drag turns; the corner tool's anchor already IS a
                // corner from the initial click, so with no inherited entry to reconcile there is
                // nothing to reshape - it just keeps facing however it was placed.
                if (gameRuntime.Construction.Selected is ConveyorDefinition selectedConveyor)
                {
                    if (_pendingCornerEntry.HasValue)
                    {
                        if (_pendingCornerEntry.Value.Opposite() != newAxis)
                        {
                            ReshapeAnchorAsCorner(_dragAnchorCell, _pendingCornerEntry.Value, newAxis);
                        }
                        // else: the inherited entry is already collinear with the discovered
                        // axis - the anchor was placed facing the right way, nothing to redo.
                    }
                    else if (selectedConveyor.DefaultShape == ConveyorShapeKind.Straight)
                    {
                        // No neighbor feeding into the anchor: it was placed with whatever
                        // rotation happened to be previewed, which may not match the direction
                        // the drag actually went. Re-point it at the discovered axis.
                        ReshapeAnchorAsStraight(_dragAnchorCell, newAxis);
                    }
                }

                _pendingCornerEntry = null;
                _dragAxis = newAxis;
            }

            Direction axis = _dragAxis.Value;
            GridCoord axisOffset = axis.ToOffset();

            int axisDistance = Mathf.Max(0, rawDelta.X * axisOffset.X + rawDelta.Y * axisOffset.Y);

            GridCoord placedDelta = new GridCoord(_lastPlacedCell.X - _dragAnchorCell.X, _lastPlacedCell.Y - _dragAnchorCell.Y);
            int placedDistance = placedDelta.X * axisOffset.X + placedDelta.Y * axisOffset.Y;

            int steps = axisDistance - placedDistance;
            int guard = 0;
            while (steps > 0 && guard++ < 4096)
            {
                _lastPlacedCell += axis;
                // A run continues as straight belts regardless of which tool started the drag -
                // a corner only ever belongs at the anchor (or a later turn, handled above).
                PlaceStraightSegment(_lastPlacedCell, axis);
                steps--;
            }
        }

        /// <summary>
        /// Places a straight conveyor at `cell` even if the Corner tool is the one currently
        /// selected, by briefly swapping ConstructionService's selection to the dedicated
        /// straight definition and back. Selection/preview rotation are restored immediately
        /// after so the drag's own tool state is unaffected.
        /// </summary>
        void PlaceStraightSegment(GridCoord cell, Direction axis)
        {
            var construction = gameRuntime.Construction;
            if (!(construction.Selected is ConveyorDefinition selectedConveyor) || selectedConveyor.DefaultShape == ConveyorShapeKind.Straight)
            {
                PlaceAt(cell, axis);
                return;
            }

            BuildingDefinition previousSelected = construction.Selected;
            Direction previousPreview = construction.PreviewRotation;
            construction.SelectBuilding(straightConveyorForDragContinuation);
            PlaceAt(cell, axis);
            construction.SelectBuilding(previousSelected);
            construction.SetPreviewRotation(previousPreview);
        }

        static Direction DominantDirection(GridCoord delta)
        {
            if (Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y))
            {
                return delta.X >= 0 ? Direction.East : Direction.West;
            }

            return delta.Y >= 0 ? Direction.North : Direction.South;
        }

        // Both reshapes mutate the anchor's ConveyorRuntime in place exactly as before - it is a
        // real runtime object occupying its cell from the moment it was placed, whether or not its
        // construction site has delivered it yet. No view is spawned here any more: a segment
        // still under construction shows the blue site overlay (ConstructionSiteVisualSync), and
        // its real view only appears when the site materializes it. A segment already materialized
        // (a drag continued over a finished belt) still refreshes its view, since it has one.
        void ReshapeAnchorAsCorner(GridCoord anchorCell, Direction entry, Direction exit)
        {
            if (gameRuntime.Grid.GetOccupant(anchorCell) is ConveyorRuntime conveyor)
            {
                try
                {
                    conveyor.ConfigureAsCorner(entry, exit);
                    RefreshViewIfMaterialized(conveyor);
                }
                catch (System.ArgumentException)
                {
                    // Reversed direction (entry == exit or opposite) - not a valid corner, leave as-is.
                }
            }
        }

        void ReshapeAnchorAsStraight(GridCoord anchorCell, Direction exit)
        {
            if (gameRuntime.Grid.GetOccupant(anchorCell) is ConveyorRuntime conveyor)
            {
                conveyor.ConfigureAsStraight(exit);
                RefreshViewIfMaterialized(conveyor);
            }
        }

        void RefreshViewIfMaterialized(BuildingRuntime runtime)
        {
            if (gameRuntime.ConstructionSites != null && gameRuntime.ConstructionSites.TryGetSiteContaining(runtime, out _)) return;

            // Materialized but still assembling: ConstructionSiteVisualSync re-reads the runtime's
            // orientation every frame, so the reshape already shows - and spawning the real view
            // here would double-draw it until the dissolve hands over.
            if (gameRuntime.ConstructionSiteVisuals != null && gameRuntime.ConstructionSiteVisuals.Draws(runtime)) return;

            _spawner.SpawnView(runtime);
        }

        /// <summary>
        /// A construction site just delivered a segment's full cost: it is now a real, registered
        /// building (ConstructionSiteSystem already called Transport.Register) and needs the same
        /// view/item-visual wiring an immediate placement used to do inline.
        ///
        /// The view is the one part that is no longer immediate. Delivery completes long before the
        /// sprite has finished assembling on screen, so ConstructionSiteVisualSync keeps the segment
        /// in its assembling set and calls the spawner lent above once the dissolve reaches 1.
        /// Item visuals stay immediate on purpose: the segment is live in TransportSystem from this
        /// instant, and items already riding it must be visible while it assembles.
        /// </summary>
        void OnSegmentMaterialized(BuildingRuntime runtime)
        {
            bool assembledElsewhere = gameRuntime.ConstructionSiteVisuals != null
                && gameRuntime.ConstructionSiteVisuals.AssemblesMaterializedSegments;
            if (!assembledElsewhere) _spawner?.SpawnView(runtime);

            if (gameRuntime.ItemVisuals != null) gameRuntime.ItemVisuals.Register(runtime);
        }

        void OnDestroy()
        {
            if (_subscribedToMaterialization && gameRuntime != null && gameRuntime.ConstructionSites != null)
            {
                gameRuntime.ConstructionSites.SegmentMaterialized -= OnSegmentMaterialized;
            }
        }

        void PlaceAt(GridCoord cell, Direction rotation)
        {
            // Captured before TryPlace: a conveyor placed onto an existing conveyor "overtakes"
            // it (see ConstructionService), which would otherwise leave the replaced instance
            // stuck registered in Transport forever with no grid cell pointing to it. A masked
            // multi-cell footprint (Splitter/Crossroad's "+" shape) can overtake several distinct
            // conveyor instances at once across its footprint, not just the one at the clicked
            // cell - scan every footprint cell, not just `cell` itself.
            var previousOccupants = new HashSet<BuildingRuntime>();
            if (gameRuntime.Construction.Selected != null)
            {
                foreach (Vector2Int offset in gameRuntime.Construction.Selected.FootprintCells)
                {
                    if (gameRuntime.Grid.GetOccupant(new GridCoord(cell.X + offset.x, cell.Y + offset.y)) is BuildingRuntime occupant)
                    {
                        previousOccupants.Add(occupant);
                    }
                }
            }

            // Overtaking a cell that belongs to another still-pending site cancels that whole site
            // first (releasing its reservations and freeing its cells) - half a chantier cannot be
            // overtaken and left behind with segments that no longer own their ground.
            foreach (BuildingRuntime previousBuilding in previousOccupants)
            {
                gameRuntime.Construction.TryCancelSiteAt(previousBuilding.Cell);
            }

            bool placingIntoConveyorRun = _isDragPlacing && _activeConveyorSite != null && IsConveyorRunDefinition(gameRuntime.Construction.Selected);

            if (gameRuntime.Construction.TryPlace(cell, rotation, out ConstructionSiteRuntime site, placingIntoConveyorRun ? _activeConveyorSite : null))
            {
                // Nothing is spawned or registered here any more: the segment exists as runtime
                // state occupying its cells, but stays inert (no view, not in TransportSystem)
                // until robots have delivered its full cost - OnSegmentMaterialized does that part.
                if (IsConveyorRunDefinition(gameRuntime.Construction.Selected)) _activeConveyorSite = site;

                foreach (BuildingRuntime previousBuilding in previousOccupants)
                {
                    if (site.Segments.Count > 0 && ReferenceEquals(previousBuilding, site.Segments[site.Segments.Count - 1])) continue;
                    _spawner.RemoveView(previousBuilding.Cell);
                    gameRuntime.Transport.Unregister(previousBuilding);
                    if (gameRuntime.ItemVisuals != null) gameRuntime.ItemVisuals.Unregister(previousBuilding);
                }
            }
            else if (gameRuntime.Construction.Selected != null
                     && gameRuntime.Construction.GetPlacementRefusalReason(cell) == PlacementRefusalReason.BuildingCapReached)
            {
                PlacementRefusedAtBuildingCap?.Invoke("Plafond de batiments atteint");
            }
        }

        static bool IsConveyorRunDefinition(BuildingDefinition definition) =>
            definition is ConveyorDefinition || definition is SplitterDefinition || definition is CrossroadDefinition;

        void HandleDemolition(GridCoord cell)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                // Right-click while a building/conveyor is armed for placement also cancels the
                // ghost/construction tool - a right-click "cancel" gesture is the expected escape
                // hatch mid-place - but demolition still happens underneath it regardless: right-
                // click stays the one method to remove an existing building.
                if (gameRuntime.Construction.Selected != null)
                {
                    gameRuntime.Construction.Cancel();
                }

                DemolishAt(cell);
                _isDragDemolishing = true;
                _lastDemolishedCell = cell;
            }
            else if (_isDragDemolishing && mouse.rightButton.isPressed && cell != _lastDemolishedCell)
            {
                SweepDemolish(_lastDemolishedCell, cell);
                _lastDemolishedCell = cell;
            }

            if (mouse.rightButton.wasReleasedThisFrame)
            {
                _isDragDemolishing = false;
            }
        }

        void SweepDemolish(GridCoord from, GridCoord to)
        {
            // Simple greedy grid walk so a fast drag doesn't skip cells.
            GridCoord current = from;
            int guard = 0;
            while (current != to && guard++ < 256)
            {
                int dx = to.X - current.X;
                int dy = to.Y - current.Y;
                if (Mathf.Abs(dx) >= Mathf.Abs(dy))
                {
                    current = new GridCoord(current.X + System.Math.Sign(dx), current.Y);
                }
                else
                {
                    current = new GridCoord(current.X, current.Y + System.Math.Sign(dy));
                }

                DemolishAt(current);
            }
        }

        void DemolishAt(GridCoord cell)
        {
            // A still-pending chantier was never paid for and has no view: right-clicking it
            // cancels it (releasing its reservations) rather than demolishing a building that does
            // not exist yet (TASK_05_ROBOT_CONSTRUCTEUR.md §4).
            if (gameRuntime.Construction.TryCancelSiteAt(cell)) return;

            if (gameRuntime.Construction.TryDemolish(cell, out BuildingRuntime removed))
            {
                // removed.Cell (the footprint's origin) may differ from the clicked cell for a
                // multi-cell building - the view is keyed by origin, not by whichever cell was clicked.
                // The deposit's own view (spawned once at world generation) was never destroyed
                // by placing the extractor on top of it - it was simply covered up - so removing
                // just the extractor's view here is enough to reveal it again underneath.
                _spawner.RemoveView(removed.Cell);
                gameRuntime.Transport.Unregister(removed);
                if (gameRuntime.ItemVisuals != null) gameRuntime.ItemVisuals.Unregister(removed);
            }
        }
    }
}

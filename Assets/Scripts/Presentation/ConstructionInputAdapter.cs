using System.Collections.Generic;
using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Exploration;
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
        /// Raised when a placement attempt is refused for a reason the player cannot see for
        /// themselves - the building cap (TASK_04_PLAFOND_RAYON.md §3.2) and insufficient resources.
        /// Both must name their cause rather than failing silently: nothing on screen distinguishes
        /// "this click did nothing" from "this click was refused", and a gate the player cannot
        /// perceive is worse than no gate at all. See RefusalMessage for why the other reasons stay
        /// quiet. Game.Presentation must not depend on Game.UI (PROJECT_ARCHITECTURE.md §4's
        /// dependency direction), so this is a plain event a UI-layer listener (TopBarController)
        /// subscribes to instead of a direct reference the other way.
        /// </summary>
        public event System.Action<string> PlacementRefused;

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

        // Arrow colours and size come from BuildingSpawner, which draws the real ones - the ghost
        // previewing a building must not describe it with a different marker.

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

        InputAction _rotate;
        InputAction _moveInputSide;

        /// <summary>Bound to the synthetic control that is either Ctrl key, which is what the two literal reads it replaced meant together.</summary>
        InputAction _dropDragAxis;

        /// <summary>
        /// The document consulted to tell a click on the interface from a click on the world -
        /// found rather than wired, following the camera controllers' precedent: there is one in
        /// the scene, and a missing one only means the pointer is never considered to be over UI,
        /// which PointerOverUI already treats as "not over UI" anyway.
        /// </summary>
        UnityEngine.UIElements.UIDocument _uiDocument;

        void Start()
        {
            _uiDocument = FindAnyObjectByType<UnityEngine.UIElements.UIDocument>();
            _rotate = InputBindings.Find(InputActionCatalogue.Rotate);
            _moveInputSide = InputBindings.Find(InputActionCatalogue.MoveInputSide);
            _dropDragAxis = InputBindings.Find(InputActionCatalogue.DropDragAxis);

            // Cross-object wiring belongs in Start(), not Awake(): Awake ordering between
            // GameRuntime and this adapter is not guaranteed, but Start always runs after
            // every object's Awake, so gameRuntime.Grid is guaranteed to be initialized here.
            if (worldCamera == null) worldCamera = Camera.main;
            if (hoverHighlightView != null) hoverHighlightView.Initialize(gameRuntime.Grid);
            if (depositHoverGlowView != null) depositHoverGlowView.Initialize(gameRuntime.Grid, _spriteFactory);

            // _spawner is NOT taken here: it is GameRuntime.BuildingViews, which GameRuntime
            // only builds partway through its own Start() (after TerrainView.Initialize runs, since
            // the ground slab settings come from it) - and Unity does not guarantee Start() order
            // between different components (unlike Awake, which gameRuntime.Grid above already
            // relies on). Taken on the first Update() instead, by which point every object's
            // Start() has run.
        }

        void Update()
        {
            if (worldCamera == null || gameRuntime == null) return;
            if (_spawner == null)
            {
                // Borrowed, never built. This used to construct one, and a second spawner is a
                // second per-cell view dictionary: demolition reads this one, so every view created
                // anywhere else became impossible to remove. See GameRuntime.BuildingViews.
                _spawner = gameRuntime.BuildingViews;
                if (_spawner == null) return;   // GameRuntime.Start has not reached it yet
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
                // Same reason, for the panels: a building whose rotation changed needs its view
                // rebuilt (its arrows are baked children), and the UI has no spawner of its own.
                // Lending this one keeps every view in the single per-cell dictionary demolition
                // reads from.
                gameRuntime.BuildingViewRebuilder = RebuildView;

                if (gameRuntime.ConstructionSiteVisuals != null)
                {
                    gameRuntime.ConstructionSiteVisuals.SetViewSpawner(_spawner.SpawnView);

                    // And its concrete pad, for the same reason: the slab a site reveals while
                    // converting has to be the pad the finished building keeps, tiling phase
                    // included, or the swap at the handover would show.
                    gameRuntime.ConstructionSiteVisuals.SetGroundSlabSpawner(
                        (cell, footprint) => _spawner.SpawnGroundSlab(null, cell, footprint, revealedByNanoFront: true));
                }

                _subscribedToMaterialization = true;
            }

            GridCoord cellUnderMouse = CellUnderMouse();

            // Above the gate on purpose. The outline is not click handling, and freezing it while
            // a panel is open is what left it stranded on the previously inspected building: with
            // the whole method returning early, the last cell it had been given simply stayed on
            // screen while the panel moved on to another building.
            HandleHoverHighlight(cellUnderMouse);

            // Also above the gate, and for a reason the arbiter creates. An armed tool outranks an
            // open panel for Escape, and a panel can be open with a tool still armed - clicking a
            // notification opens a robot's own panel without disarming anything. Left below the
            // gate, this method would never run in exactly the case the arbiter awards to it, and
            // the key would go to nobody at all.
            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.ArmedTool))
            {
                gameRuntime.Construction.Cancel();
            }

            // The one exception to the rule below, taken before it: right-clicking the very
            // building whose panel is open removes it.
            if (TryDemolishTheInspectedBuilding(cellUnderMouse)) return;

            // The second exception, and it is about a chantier rather than a building.
            if (TryCancelTheChantierUnderTheCursor(cellUnderMouse)) return;

            // A UI panel (Building menu, Storage panel, ...) owns mouse/keyboard input while
            // open, and for one extra frame after it closes - otherwise the same click that
            // selected a menu item or closed a panel also lands on the world underneath it.
            if (gameRuntime.IsUIBlockingInput || gameRuntime.LastMenuCloseFrame == Time.frameCount) return;

            HandleRotateAndInputSide();

            UpdateGhost(cellUnderMouse);
            HandlePlacement(cellUnderMouse);
            HandleDemolition(cellUnderMouse);
        }

        /// <summary>
        /// Outlines a building's footprint, active both with and without a construction tool armed.
        ///
        /// While a building is being inspected the outline marks <b>that</b> building rather than
        /// whatever the cursor happens to be over: the outline's job is then to say which building
        /// the open panel is about, and the cursor is somewhere else entirely - on the panel. It
        /// follows the selection from one building to the next, so clicking a second building moves
        /// both the panel and the outline.
        ///
        /// A global panel (Research, the Building menu, Storage) has no building to point at, so
        /// the outline steps aside entirely rather than tracking a cursor that is busy elsewhere.
        /// </summary>
        /// <summary>The next segment a site will materialize - the one its panel's bill is being spent on. Null once every segment is built.</summary>
        static BuildingRuntime FrontSegmentOf(ConstructionSiteRuntime site)
            => site.MaterializedCount < site.Segments.Count ? site.Segments[site.MaterializedCount] : null;

        void HandleHoverHighlight(GridCoord cell)
        {
            BuildingRuntime inspected = gameRuntime.Selection.SelectedBuilding;
            if (inspected != null)
            {
                hoverHighlightView?.Show(inspected.Cell, inspected.Definition.FootprintSize);
                depositHoverGlowView?.Hide();
                return;
            }

            // A robot under inspection gets the same halo a building does - the player selected a
            // thing and expects to see which. Asked for by centre rather than by cell because it
            // stands between cells and keeps moving: the outline follows it instead of jumping a
            // cell at a time.
            ExplorerRobotRuntime inspectedRobot = gameRuntime.Selection.SelectedExplorerRobot;
            if (inspectedRobot != null)
            {
                float cellSize = gameRuntime.Grid.CellSize;
                hoverHighlightView?.ShowAt(inspectedRobot.Position * cellSize, cellSize);
                depositHoverGlowView?.Hide();
                return;
            }

            // A site under inspection is marked the same way, on the segment its panel is really
            // about: the one being built. On a dragged run the earlier segments are already
            // buildings and outlining the whole run would claim ground that is no longer the
            // site's.
            ConstructionSiteRuntime inspectedSite = gameRuntime.Selection.SelectedSite;
            if (inspectedSite != null)
            {
                BuildingRuntime front = FrontSegmentOf(inspectedSite);
                if (front != null) hoverHighlightView?.Show(front.Cell, front.Definition.FootprintSize);
                else hoverHighlightView?.Hide();
                depositHoverGlowView?.Hide();
                return;
            }

            if (gameRuntime.Selection.ActiveGlobalPanel != null)
            {
                // A global panel usually covers no particular place, so nothing is outlined. The
                // Storage panel's per-box view is the exception: it is about one box on the map, and
                // said so through a slot of its own rather than through SelectedBuilding, which a
                // global panel excludes.
                BuildingRuntime subject = gameRuntime.Selection.GlobalPanelSubject;
                if (subject != null) hoverHighlightView?.Show(subject.Cell, subject.Definition.FootprintSize);
                else hoverHighlightView?.Hide();

                depositHoverGlowView?.Hide();
                return;
            }

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

        /// <summary>
        /// R and T, which only mean anything while the ghost is up. Escape used to live here and
        /// moved above the UI gate - see Update.
        /// </summary>
        void HandleRotateAndInputSide()
        {
            if (InputBindings.WasPressedThisFrame(_rotate))
            {
                Direction next = gameRuntime.Construction.PreviewRotation.RotateCW(1);
                gameRuntime.Construction.SetPreviewRotation(next);
            }

            // T moves the single entry arrow round the building, skipping the output side. Only
            // buildings that declare one input have a side to move; for the rest the key does
            // nothing rather than something invisible.
            if (InputBindings.WasPressedThisFrame(_moveInputSide)
                && gameRuntime.Construction.Selected != null
                && gameRuntime.Construction.Selected.HasSingleInputArrow)
            {
                gameRuntime.Construction.SetPreviewInputSide(NextInputSide(
                    gameRuntime.Construction.PreviewInputSide, gameRuntime.Construction.PreviewRotation));
            }
        }

        /// <summary>
        /// The next side round from <paramref name="current"/>, skipping <paramref name="exit"/>.
        ///
        /// Clockwise, so T reads the same way R does, and it steps twice when the next side round is
        /// the output - which is why this is a loop rather than one RotateCW: three legal sides out
        /// of four means the skip can land anywhere in the cycle.
        /// </summary>
        static Direction NextInputSide(Direction current, Direction exit)
        {
            Direction next = current;
            for (int i = 0; i < 4; i++)
            {
                next = next.RotateCW(1);
                if (next != exit) return next;
            }
            return current;
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
                ghostView.Show(_spriteFactory, conveyorDefinition, gameRuntime.Construction.PreviewRotation, conveyorWorldPos, conveyorValid, gameRuntime.Grid.CellSize);
                return;
            }

            if (ghostView != null) ghostView.Hide();

            if (selected == null || buildingGhostView == null)
            {
                if (buildingGhostView != null) buildingGhostView.Hide();
                return;
            }

            bool valid = gameRuntime.Construction.CanPlace(cell);
            Sprite sprite = ResolveGhostSprite(selected);
            // The size and the lift the built view will use, never the footprint's own: the ghost
            // previews the building that will be built, art taller than its ground included.
            Vector2 worldSize = BuildingSpawner.ArtWorldSize(selected, gameRuntime.Grid.CellSize, sprite);
            Vector3 worldCenter = gameRuntime.Grid.FootprintCenterToWorld(cell, selected.FootprintSize)
                + Vector3.up * BuildingSpawner.ArtLift(selected, gameRuntime.Grid.CellSize, sprite);
            Direction previewRotation = gameRuntime.Construction.PreviewRotation;
            (bool rotateSprite, Direction artNativeDirection) = ResolveGhostRotation(selected);

            // Output and entry arrows are independent: a building can take deliveries without
            // producing anything physical (DataCenter), so each side is previewed on its own.
            Sprite outputArrowSprite = null;
            Vector3? outputArrowWorldPos = null;
            if (selected.HasOutputArrow)
            {
                // The building's own rule for which cell of its output edge carries the arrow, not
                // the first one: they differ on every even-width edge, so a 2x2 Foundry previewed
                // its arrow one cell away from where it grew it.
                GridCoord outputCell = BuildingRuntime.ComputeOutputCell(cell, selected.FootprintSize, previewRotation);
                // Inset the same way the built view does, or the preview would show the arrow a
                // half-cell further out than where it ends up.
                outputArrowWorldPos = BuildingSpawner.ArrowPosition(
                    gameRuntime.Grid.CellCenterToWorld(outputCell), previewRotation, gameRuntime.Grid.CellSize);
                outputArrowSprite = _spriteFactory.CreateArrowSprite(BuildingSpawner.OutputArrowColor);
            }

            Sprite inputArrowSprite = null;
            List<(Vector3 position, Direction direction)> inputArrows = null;
            if (selected.HasInputArrows)
            {
                inputArrowSprite = _spriteFactory.CreateArrowSprite(BuildingSpawner.InputArrowColor);
                inputArrows = new List<(Vector3, Direction)>();

                // One arrow for a single-input building, on the side T has landed on - so the ghost
                // shows the one face the building will actually take from, rather than three faces
                // it will refuse two of.
                if (selected.HasSingleInputArrow)
                {
                    (GridCoord inputCell, Direction inputSide) = BuildingRuntime.ComputeSingleInputCell(
                        cell, selected.FootprintSize, gameRuntime.Construction.PreviewInputSide);

                    inputArrows.Add((BuildingSpawner.ArrowPosition(
                        gameRuntime.Grid.CellCenterToWorld(inputCell), inputSide, gameRuntime.Grid.CellSize), inputSide));
                }
                else
                {
                    foreach ((GridCoord edgeCell, Direction fromMySide) in BuildingRuntime.ComputeInputCells(cell, selected.FootprintSize, previewRotation))
                    {
                        inputArrows.Add((BuildingSpawner.ArrowPosition(
                            gameRuntime.Grid.CellCenterToWorld(edgeCell), fromMySide, gameRuntime.Grid.CellSize), fromMySide));
                    }
                }
            }

            buildingGhostView.Show(sprite, worldSize, worldCenter, previewRotation, valid,
                outputArrowSprite, outputArrowWorldPos, BuildingSpawner.ArrowWorldSize(gameRuntime.Grid.CellSize),
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

            // A move borrows the whole placement gesture - same ghost, same gates, same R to turn -
            // and differs only here, at the click: one building changes address instead of a new
            // chantier being opened. Never a drag: there is one building to move, not a run to lay.
            if (gameRuntime.Construction.RelocationTarget != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) RelocateTo(cell);
                return;
            }

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

                // Only a belt is laid by dragging. A Splitter or a Crossroad used to start a drag
                // too, and the drag is what decides a segment's rotation: moving the mouse a few
                // cells along the line the player was aiming at dropped extra copies of the piece,
                // each turned to face the drag's axis instead of the rotation being previewed. One
                // click, one piece, facing the way the ghost showed it.
                _isDragPlacing = IsDraggableRun(gameRuntime.Construction.Selected);
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
            if (!_dragAxis.HasValue || cell != _lastPlacedCell) return;
            if (!InputBindings.WasPressedThisFrame(_dropDragAxis)) return;

            _pendingCornerEntry = _dragAxis.Value.Opposite();
            _dragAxis = null;
            _dragAnchorCell = _lastPlacedCell;
        }

        /// <summary>
        /// The side (if any) `cell` is fed from: the direction toward a neighbor whose own output
        /// lands in this cell. Unlike a plain "is there any conveyor next door" check, this requires
        /// actual alignment (BuildingRuntime.FeedsCell), so a neighbor pointed elsewhere is ignored
        /// instead of producing a bogus inherited direction - and a Splitter or Crossroad answers
        /// for every exit it has, not for one edge derived from a rotation that names its entry.
        /// </summary>
        Direction? FindEntryDirection(GridCoord cell, Direction? excluding = null)
        {
            foreach (Direction dir in AllDirections)
            {
                if (excluding.HasValue && dir == excluding.Value) continue;

                GridCoord neighborCell = cell + dir;
                if (gameRuntime.Grid.GetOccupant(neighborCell) is BuildingRuntime candidate && candidate.FeedsCell(cell))
                {
                    return dir;
                }
            }

            return null;
        }

        /// <summary>
        /// The side the anchor is fed from, reconciled against the axis the drag turned out to take.
        ///
        /// The feeder is found at click time, before the axis is known, and it can turn out to sit
        /// on the side the drag then heads for - a storage box directly below the anchor, dragged
        /// downward. A belt cannot enter and leave by the same side, so that candidate is dropped
        /// and the question asked again ignoring that side: another neighbour may well feed the
        /// anchor from somewhere the drag can actually leave.
        ///
        /// Without this the reshape asked for a corner whose entry equalled its exit,
        /// ConfigureAsCorner refused it as the non-corner it is, and the anchor kept pointing back
        /// into the very thing feeding it - never once facing the way the player dragged.
        /// </summary>
        Direction? ResolveInheritedEntry(Direction newAxis)
        {
            if (!_pendingCornerEntry.HasValue) return null;
            if (_pendingCornerEntry.Value != newAxis) return _pendingCornerEntry;

            return FindEntryDirection(_dragAnchorCell, excluding: newAxis);
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
                    Direction? inheritedEntry = ResolveInheritedEntry(newAxis);

                    if (inheritedEntry.HasValue)
                    {
                        if (inheritedEntry.Value.Opposite() != newAxis)
                        {
                            ReshapeAnchorAsCorner(_dragAnchorCell, inheritedEntry.Value, newAxis);
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
        /// A segment finished assembling: it is now a real, registered building
        /// (ConstructionSiteSystem already called Transport.Register) and needs the same
        /// view/item-visual wiring an immediate placement used to do inline.
        ///
        /// The view is handed over rather than spawned here when a dissolve is configured: the
        /// segment arrives whole but its assembling objects are still on screen for a frame, and
        /// ConstructionSiteVisualSync swaps both in one call so nothing flickers.
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
            // A belt dragged over a belt that already exists just turns it. Handled before anything
            // else because it is not a placement at all: no cost, no chantier, no destruction, and
            // the items riding it stay on it. See ConstructionService.TryRedirectExistingConveyor.
            if (gameRuntime.Construction.TryRedirectExistingConveyor(cell, rotation, out ConveyorRuntime redirected))
            {
                gameRuntime.NotePlayerAction();
                RefreshViewIfMaterialized(redirected);
                return;
            }

            // Captured before TryPlace: overtaking replaces conveyors that are already there, and
            // the replaced instances would otherwise stay registered in Transport forever with no
            // grid cell pointing at them. A masked multi-cell footprint (Splitter/Crossroad's "+")
            // can overtake several distinct conveyors at once, so scan every footprint cell rather
            // than just `cell`. What each of them OWES is settled inside TryPlace, the layer that
            // knows whether a belt was ever paid for.
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

            bool placingIntoConveyorRun = _isDragPlacing && _activeConveyorSite != null && IsDraggableRun(gameRuntime.Construction.Selected);

            if (gameRuntime.Construction.TryPlace(cell, rotation, out ConstructionSiteRuntime site, placingIntoConveyorRun ? _activeConveyorSite : null))
            {
                gameRuntime.NotePlayerAction();

                // Nothing is spawned or registered here any more: the segment exists as runtime
                // state occupying its cells, but stays inert (no view, not in TransportSystem)
                // until robots have delivered its full cost - OnSegmentMaterialized does that part.
                if (IsDraggableRun(gameRuntime.Construction.Selected)) _activeConveyorSite = site;

                // The view/registration half of the overtake TryPlace has just settled. Harmless on
                // a segment that was still pending: it was never registered and never had a view.
                foreach (BuildingRuntime previousBuilding in previousOccupants)
                {
                    _spawner.RemoveView(previousBuilding.Cell);
                    gameRuntime.Transport.Unregister(previousBuilding);
                    if (gameRuntime.ItemVisuals != null) gameRuntime.ItemVisuals.Unregister(previousBuilding);
                }
            }
            else if (gameRuntime.Construction.Selected != null)
            {
                // Only the two refusals a player cannot see for themselves are announced. Out of
                // radius, not unlocked and occupied are already legible from the ghost's own tint
                // and from where the cursor is; a message on every one of those would be noise on
                // gestures the player is making deliberately.
                string message = RefusalMessage(gameRuntime.Construction.GetPlacementRefusalReason(cell));
                if (message != null) PlacementRefused?.Invoke(message);
            }
        }

        /// <summary>The player-facing wording for a refusal, or null when the refusal already shows itself.</summary>
        static string RefusalMessage(PlacementRefusalReason reason) => reason switch
        {
            PlacementRefusalReason.BuildingCapReached => "Plafond de batiments atteint",
            PlacementRefusalReason.CannotAfford => "Ressources insuffisantes",
            _ => null
        };

        /// <summary>
        /// Whether this tool lays a <b>run</b> - a line of pieces drawn in one gesture, gathered into
        /// a single chantier.
        ///
        /// Belts only. A Splitter and a Crossroad are single pieces placed one per click: they were
        /// counted as runs, which made a click-and-slide drop several of them, each rotated to the
        /// drag's axis rather than to the rotation the ghost was showing.
        /// </summary>
        static bool IsDraggableRun(BuildingDefinition definition) => definition is ConveyorDefinition;

        /// <summary>
        /// Right-clicking the building whose contextual panel is open removes it, panel and all.
        ///
        /// <b>Why it needed saying at all.</b> Demolition was never missing - the adapter simply
        /// never got that far: <c>IsUIBlockingInput</c> is true the moment
        /// <c>Selection.SelectedBuilding</c> is set, so opening a building's panel made the world
        /// inert, right button included. Hence one narrow exception rather than a relaxation of the
        /// rule: it fires only on the press frame, only with no ghost armed, only when the cell
        /// under the cursor is occupied by <b>that same</b> building, and never while the pointer
        /// is over the interface - the panel is docked over the world, and a right-click inside it
        /// must not reach a building that happens to sit behind it.
        ///
        /// The selection is cleared first, so the panel goes with the building rather than
        /// surviving a frame over something that no longer exists.
        /// </summary>
        bool TryDemolishTheInspectedBuilding(GridCoord cell)
        {
            BuildingRuntime inspected = gameRuntime.Selection.SelectedBuilding;
            if (inspected == null) return false;
            if (gameRuntime.Construction.Selected != null) return false; // a ghost is armed: right-click cancels it

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame) return false;
            if (PointerOverUI.At(_uiDocument, mouse.position.ReadValue())) return false;
            if (!ReferenceEquals(gameRuntime.Grid.GetOccupant(cell), inspected)) return false;

            gameRuntime.Selection.Clear();
            DemolishAt(cell);
            return true;
        }

        /// <summary>
        /// Right-clicking a construction site cancels it, <b>whatever else is open or armed</b>.
        ///
        /// Two states used to make a chantier impossible to take back without first putting the
        /// interface away. With the building menu open, <c>IsUIBlockingInput</c> is true and the
        /// world is inert, right button included. With a belt still armed on the cursor -
        /// which is exactly the moment a misplaced belt is noticed - right-click means "stop
        /// placing" and nothing else, by a decision this deliberately does not reverse: it only
        /// takes precedence when the cell under the cursor holds a chantier, and the tool stays
        /// armed afterwards so a corrected run continues in the same gesture.
        ///
        /// Narrow like its neighbour above: the press frame only, never through the interface, and
        /// only on a cell whose occupant belongs to a site that has not materialised. A built
        /// building goes on being demolished by the ordinary path, with its confirmation and its
        /// refund.
        /// </summary>
        bool TryCancelTheChantierUnderTheCursor(GridCoord cell)
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.rightButton.wasPressedThisFrame) return false;
            if (PointerOverUI.At(_uiDocument, mouse.position.ReadValue())) return false;

            if (!(gameRuntime.Grid.GetOccupant(cell) is BuildingRuntime occupant)) return false;
            if (gameRuntime.ConstructionSites == null) return false;
            if (!gameRuntime.ConstructionSites.TryGetSiteContaining(occupant, out _)) return false;

            if (!gameRuntime.Construction.TryCancelPendingAt(cell)) return false;

            gameRuntime.NotePlayerAction();
            return true;
        }

        void HandleDemolition(GridCoord cell)
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                // While a ghost is armed, right-click cancels it and does nothing else.
                //
                // It used to cancel AND demolish whatever was underneath, on the reasoning that
                // right-click is the one way to remove a building. That reasoning ignores where the
                // player's attention is: with a ghost on the cursor the gesture means "stop placing",
                // and it is aimed whereever the ghost happened to be - usually over the base they are
                // building in. So the escape hatch destroyed a working building as a side effect, and
                // the sweep it armed could take out several while the button stayed down.
                //
                // Demolition is not lost, it is one click further: the ghost is gone now, so the next
                // right-click demolishes normally.
                if (gameRuntime.Construction.Selected != null)
                {
                    gameRuntime.Construction.Cancel();
                    return;
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

        /// <summary>
        /// Completes a move. The runtime is the same object at a new address, so nothing is
        /// transferred and nothing is re-registered with transport or the item views - those key on
        /// the building, not on where it stands. Only the view has to be rebuilt, because it is
        /// keyed by cell and carries its arrows as baked children.
        /// </summary>
        void RelocateTo(GridCoord cell)
        {
            GridCoord previousCell = gameRuntime.Construction.RelocationTarget.Cell;

            if (!gameRuntime.Construction.TryRelocate(cell, out BuildingRuntime moved)) return;

            gameRuntime.NotePlayerAction();
            RebuildView(moved, previousCell);
        }

        /// <summary>
        /// Destroys a building's view and draws it again from its current state. The view is keyed
        /// by cell and its arrows are children fixed at spawn, so both a rotation and a move need
        /// this - <paramref name="previousCell"/> is where the old view is filed, which is not where
        /// the building now stands after a move.
        /// </summary>
        void RebuildView(BuildingRuntime building, GridCoord previousCell)
        {
            if (building == null || _spawner == null) return;

            _spawner.RemoveView(previousCell);
            _spawner.SpawnView(building);
        }

        void DemolishAt(GridCoord cell)
        {
            // A still-pending segment was never paid for and has no view: right-clicking it cancels
            // that segment (releasing its earmarks) rather than demolishing a building that does not
            // exist yet (TASK_05_ROBOT_CONSTRUCTEUR.md §4). One segment, not its whole chantier - so
            // a sweep across three belts of a twenty-belt drag removes exactly those three, and the
            // sweep above needs no special case for it.
            if (gameRuntime.Construction.TryCancelPendingAt(cell))
            {
                gameRuntime.NotePlayerAction();
                return;
            }

            if (gameRuntime.Construction.TryDemolish(cell, out BuildingRuntime removed))
            {
                gameRuntime.NotePlayerAction();

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

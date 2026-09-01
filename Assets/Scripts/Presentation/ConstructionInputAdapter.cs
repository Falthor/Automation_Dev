using Game.Construction;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
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

        static readonly Color OutputArrowColor = new Color(0.25f, 0.95f, 0.35f, 1f);

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

        bool _isDragDemolishing;
        GridCoord _lastDemolishedCell;

        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

        void Start()
        {
            // Cross-object wiring belongs in Start(), not Awake(): Awake ordering between
            // GameRuntime and this adapter is not guaranteed, but Start always runs after
            // every object's Awake, so gameRuntime.Grid is guaranteed to be initialized here.
            if (worldCamera == null) worldCamera = Camera.main;
            _spawner = new BuildingSpawner(gameRuntime.Grid, _spriteFactory);
            if (hoverHighlightView != null) hoverHighlightView.Initialize(gameRuntime.Grid);
        }

        void Update()
        {
            if (worldCamera == null || gameRuntime == null || _spawner == null) return;

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
            if (hoverHighlightView == null) return;

            if (gameRuntime.Grid.GetOccupant(cell) is BuildingRuntime building)
            {
                hoverHighlightView.Show(building.Cell, building.Definition.FootprintSize);
            }
            else
            {
                hoverHighlightView.Hide();
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

            if (selected.HasOutputArrow)
            {
                GridCoord outputCell = BuildingRuntime.ComputeOutputCell(cell, selected.FootprintSize, previewRotation);
                Vector3 arrowWorldPos = gameRuntime.Grid.CellCenterToWorld(outputCell);
                Sprite arrowSprite = _spriteFactory.CreateArrowSprite(OutputArrowColor);
                buildingGhostView.Show(sprite, worldSize, worldCenter, previewRotation, valid,
                    arrowSprite, arrowWorldPos, gameRuntime.Grid.CellSize * 0.4f);
            }
            else
            {
                buildingGhostView.Show(sprite, worldSize, worldCenter, previewRotation, valid);
            }
        }

        Sprite ResolveGhostSprite(BuildingDefinition definition)
        {
            if (definition is ExtractorDefinition extractorDef)
            {
                return extractorDef.Sprite != null ? extractorDef.Sprite : _spriteFactory.CreateSolidSquareSprite(extractorDef.PlaceholderColor);
            }

            if (definition is StorageDefinition storageDef)
            {
                return storageDef.Sprite != null ? storageDef.Sprite : _spriteFactory.CreateSolidSquareSprite(storageDef.PlaceholderColor);
            }

            return _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
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

                // Only the "straight" tool auto-turns its anchor into a corner on an axis
                // change; corner/crossroad tools stamp the same explicit shape along the drag.
                bool isStraightTool = gameRuntime.Construction.Selected is ConveyorDefinition def
                    && def.DefaultShape == ConveyorShapeKind.Straight;

                if (isStraightTool)
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
                    else
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
                PlaceAt(_lastPlacedCell, axis);
                steps--;
            }
        }

        static Direction DominantDirection(GridCoord delta)
        {
            if (Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y))
            {
                return delta.X >= 0 ? Direction.East : Direction.West;
            }

            return delta.Y >= 0 ? Direction.North : Direction.South;
        }

        void ReshapeAnchorAsCorner(GridCoord anchorCell, Direction entry, Direction exit)
        {
            if (gameRuntime.Grid.GetOccupant(anchorCell) is ConveyorRuntime conveyor)
            {
                try
                {
                    conveyor.ConfigureAsCorner(entry, exit);
                    _spawner.SpawnView(conveyor);
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
                _spawner.SpawnView(conveyor);
            }
        }

        void PlaceAt(GridCoord cell, Direction rotation)
        {
            // Captured before TryPlace: a conveyor placed onto an existing conveyor "overtakes"
            // it (see ConstructionService), which would otherwise leave the replaced instance
            // stuck registered in Transport forever with no grid cell pointing to it.
            object previousOccupant = gameRuntime.Grid.GetOccupant(cell);

            if (gameRuntime.Construction.TryPlace(cell, rotation, out BuildingRuntime placed))
            {
                _spawner.SpawnView(placed);
                gameRuntime.Transport.Register(placed);
                if (gameRuntime.ItemVisuals != null) gameRuntime.ItemVisuals.Register(placed);

                if (previousOccupant is BuildingRuntime previousBuilding && !ReferenceEquals(previousOccupant, placed))
                {
                    gameRuntime.Transport.Unregister(previousBuilding);
                    if (gameRuntime.ItemVisuals != null) gameRuntime.ItemVisuals.Unregister(previousBuilding);
                }
            }
        }

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

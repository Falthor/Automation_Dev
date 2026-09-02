using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Turns a BuildingRuntime (already placed by ConstructionService) into its Unity
    /// GameObject/View. Never called by Game.Construction - Construction must not depend
    /// on Presentation, so the caller (an input adapter) drives this after a successful
    /// TryPlace/TryDemolish.
    /// </summary>
    public sealed class BuildingSpawner
    {
        const int StandardSortingOrder = 10;
        const int OutputArrowSortingOrder = 11;
        const int InputArrowSortingOrder = 11;
        static readonly Color OutputArrowColor = new Color(0.25f, 0.95f, 0.35f, 1f);
        static readonly Color InputArrowColor = new Color(0.3f, 0.6f, 1f, 1f);

        readonly GridRuntime _grid;
        readonly ProceduralSpriteFactory _spriteFactory;
        readonly Dictionary<GridCoord, GameObject> _views = new Dictionary<GridCoord, GameObject>();

        public BuildingSpawner(GridRuntime grid, ProceduralSpriteFactory spriteFactory)
        {
            _grid = grid;
            _spriteFactory = spriteFactory;
        }

        public void SpawnView(BuildingRuntime runtime)
        {
            if (_views.TryGetValue(runtime.Cell, out var existing))
            {
                Object.Destroy(existing);
                _views.Remove(runtime.Cell);
            }

            if (runtime is ConveyorRuntime conveyorRuntime)
            {
                var go = new GameObject($"Conveyor {runtime.Cell}");
                go.transform.position = _grid.CellCenterToWorld(runtime.Cell);
                var view = go.AddComponent<ConveyorView>();
                var conveyorDefinition = (ConveyorDefinition)conveyorRuntime.Definition;
                view.Sync(conveyorRuntime, _spriteFactory, conveyorDefinition, _grid.CellSize);

                _views[runtime.Cell] = go;
            }
            else if (runtime is SplitterRuntime splitterRuntime)
            {
                _views[runtime.Cell] = SpawnRotatingCrossView(splitterRuntime, (SplitterDefinition)splitterRuntime.Definition, ((SplitterDefinition)splitterRuntime.Definition).ArtNativeEntrySide);
            }
            else if (runtime is CrossroadRuntime crossroadRuntime)
            {
                _views[runtime.Cell] = SpawnRotatingCrossView(crossroadRuntime, (CrossroadDefinition)crossroadRuntime.Definition, Direction.North);
            }
            else
            {
                _views[runtime.Cell] = SpawnStandardView(runtime);
            }
        }

        /// <summary>
        /// Generic view for every non-conveyor building: a sprite sized to its footprint, plus
        /// an output arrow (and, for a recipe-based production building, entry arrows on every
        /// other side) if its Definition declares one. Covers Extractor/Storage/Foundry/Factory/
        /// AdvancedFoundry/Assembler/PowerplantGaz/Laboratory/DataCenter - the only per-type
        /// differences (HasOutputArrow/HasInputArrows) already live on BuildingDefinition, so no
        /// concrete-type dispatch is needed here at all.
        ///
        /// The root never rotates - only the sprite's world size follows the footprint. Rotating
        /// a building must not change how it looks, only where its input/output arrows sit, so
        /// facing is baked into each arrow's own position/rotation instead of the root's.
        /// </summary>
        GameObject SpawnStandardView(BuildingRuntime runtime)
        {
            BuildingDefinition definition = runtime.Definition;

            var root = new GameObject($"{definition.DisplayName} {runtime.Cell}");
            root.transform.position = _grid.FootprintCenterToWorld(runtime.Cell, definition.FootprintSize);

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(root.transform, false);
            var renderer = spriteGo.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = StandardSortingOrder;
            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
            SetSpriteToWorldSize(renderer, sprite, new Vector2(_grid.CellSize, _grid.CellSize) * definition.FootprintSize);

            if (definition.HasOutputArrow)
            {
                SpawnDirectionalArrow(root.transform, _grid.CellCenterToWorld(runtime.GetOutputCell()), runtime.ExitDirection, OutputArrowColor, OutputArrowSortingOrder, inward: false);

                if (definition.HasInputArrows)
                {
                    var drawnSides = new HashSet<Direction>();
                    foreach ((GridCoord cell, Direction fromMySide) in runtime.GetEdgeCells())
                    {
                        if (fromMySide == runtime.ExitDirection) continue;
                        if (!drawnSides.Add(fromMySide)) continue;
                        SpawnDirectionalArrow(root.transform, _grid.CellCenterToWorld(cell), fromMySide, InputArrowColor, InputArrowSortingOrder, inward: true);
                    }
                }
            }

            return root;
        }

        /// <summary>
        /// Shared view for the "+"-shaped rotatable buildings (Splitter, Crossroad): unlike
        /// SpawnStandardView, the sprite itself DOES rotate with FacingRotation - their asymmetric
        /// art (an entry chevron, or two crossing lanes) has to visually turn with it, since there
        /// is no separate procedural arrow overlay to move instead (matches ConveyorView's own
        /// rotation formula: rotation minus the art's native pose, negated for Unity's CCW-positive Z).
        /// </summary>
        GameObject SpawnRotatingCrossView(BuildingRuntime runtime, BuildingDefinition definition, Direction artNativeDirection)
        {
            var root = new GameObject($"{definition.DisplayName} {runtime.Cell}");
            root.transform.position = _grid.FootprintCenterToWorld(runtime.Cell, definition.FootprintSize);

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = StandardSortingOrder;
            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
            SetSpriteToWorldSize(renderer, sprite, new Vector2(_grid.CellSize, _grid.CellSize) * definition.FootprintSize);

            if (definition.RenderOverscan != 1f)
            {
                renderer.transform.localScale *= definition.RenderOverscan;
            }

            if (definition.AnimationFrames != null && definition.AnimationFrames.Length >= 2)
            {
                renderer.gameObject.AddComponent<SpriteFlipbook>().Initialize(definition.AnimationFrames, definition.AnimationFps);
            }

            int rotationDegrees = runtime.FacingRotation.ToRotationDegrees() - artNativeDirection.ToRotationDegrees();
            root.transform.rotation = Quaternion.Euler(0f, 0f, -rotationDegrees);

            return root;
        }

        /// <summary>
        /// One small arrow sprite at a world position, facing outward from the building
        /// (output) or inward toward it (entry). Facing is entirely determined by `direction`
        /// and `inward`, never by the parent's rotation - the parent (root) never rotates.
        /// </summary>
        void SpawnDirectionalArrow(Transform parent, Vector3 worldPosition, Direction direction, Color color, int sortingOrder, bool inward)
        {
            var arrowGo = new GameObject(inward ? "InputArrow" : "OutputArrow");
            arrowGo.transform.position = worldPosition;
            Direction pointingDirection = inward ? direction.Opposite() : direction;
            arrowGo.transform.rotation = Quaternion.Euler(0f, 0f, -pointingDirection.ToRotationDegrees());
            arrowGo.transform.localScale = Vector3.one * (_grid.CellSize * 0.4f);
            arrowGo.transform.SetParent(parent, true);

            var arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
            arrowRenderer.sortingOrder = sortingOrder;
            arrowRenderer.sprite = _spriteFactory.CreateArrowSprite(color);
        }

        internal static void SetSpriteToWorldSize(SpriteRenderer renderer, Sprite sprite, Vector2 desiredWorldSize)
        {
            renderer.sprite = sprite;
            Vector2 nativeSize = sprite.bounds.size;
            Vector3 scale = renderer.transform.localScale;
            scale.x = desiredWorldSize.x / nativeSize.x;
            scale.y = desiredWorldSize.y / nativeSize.y;
            renderer.transform.localScale = scale;
        }

        public void RemoveView(GridCoord cell)
        {
            if (_views.TryGetValue(cell, out var go))
            {
                Object.Destroy(go);
                _views.Remove(cell);
            }
        }
    }
}

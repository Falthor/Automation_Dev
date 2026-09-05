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
        const int GroundSlabSortingOrder = 5;

        // How far the concrete slab bleeds past the building's true footprint on each side, so it
        // reads as sitting on top of the ground rather than stopping exactly at the grid line.
        const float GroundSlabOverscanMargin = 0.3f;

        // Splitter/Crossroad's RenderOverscan deliberately makes their arms overlap the
        // neighboring conveyor's sprite bounds at the seam (to close the visual gap). With an
        // equal sortingOrder, Unity breaks the tie by draw/instantiation order, which is
        // unstable across placements - the overlapping edge would randomly render behind or in
        // front of the conveyor. Giving the cross-shaped view a strictly higher order makes it
        // always win at that seam, matching the intent of the overscan.
        //
        // This also must stay above ItemVisualSync.ItemSortingOrder: an item riding the
        // conveyor right up to the shared edge sits inside that same overscanned overlap, and an
        // equal order there caused the item to flicker in and out as the tie-break flipped. Cross
        // always winning means the item is reliably covered as soon as it reaches the overlap,
        // instead of flickering.
        const int CrossSortingOrder = 13;
        const int OutputArrowSortingOrder = 14;
        const int InputArrowSortingOrder = 14;
        static readonly Color OutputArrowColor = new Color(0.25f, 0.95f, 0.35f, 1f);
        static readonly Color InputArrowColor = new Color(0.3f, 0.6f, 1f, 1f);

        readonly GridRuntime _grid;
        readonly ProceduralSpriteFactory _spriteFactory;
        readonly ConveyorDefinition _straightConveyorArt;
        readonly ConveyorDefinition _cornerConveyorArt;
        readonly GroundSlabSettings _groundSlabSettings;
        readonly GroundSlabNeighborLinker _groundSlabNeighborLinker;
        readonly Dictionary<GridCoord, GameObject> _views = new Dictionary<GridCoord, GameObject>();

        /// <summary>
        /// straightConveyorArt/cornerConveyorArt are optional canonical art sources used only
        /// when a conveyor's own Definition no longer matches its current Orientation.Shape
        /// (reshaped via a drag turn) - see ResolveConveyorArtDefinition. Null is fine wherever
        /// conveyors are never reshaped (e.g. EditMode tests spawning other building types).
        /// groundSlabSettings is optional; null (or its diffuse/normal being null) means no
        /// concrete slab is spawned under buildings (e.g. EditMode tests with no such art
        /// configured). groundSlabNeighborLinker is optional too; null means slabs never react
        /// to neighboring buildings being placed/demolished.
        /// </summary>
        public BuildingSpawner(GridRuntime grid, ProceduralSpriteFactory spriteFactory, ConveyorDefinition straightConveyorArt = null, ConveyorDefinition cornerConveyorArt = null, GroundSlabSettings groundSlabSettings = null, GroundSlabNeighborLinker groundSlabNeighborLinker = null)
        {
            _grid = grid;
            _spriteFactory = spriteFactory;
            _straightConveyorArt = straightConveyorArt;
            _cornerConveyorArt = cornerConveyorArt;
            _groundSlabSettings = groundSlabSettings;
            _groundSlabNeighborLinker = groundSlabNeighborLinker;
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
                ConveyorDefinition conveyorDefinition = ResolveConveyorArtDefinition(conveyorRuntime);
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
        /// A conveyor's Definition is fixed to whichever tool placed it and never changes, but
        /// its Orientation.Shape can (a straight drag-turned into a corner, or - since the Corner
        /// tool's drag now continues in straight - a corner re-pointed at a later turn). When
        /// Shape no longer matches Definition.DefaultShape, fall back to whichever of the two
        /// canonical conveyor definitions' art actually matches the current shape instead of the
        /// procedural placeholder ConveyorView would otherwise use.
        /// </summary>
        ConveyorDefinition ResolveConveyorArtDefinition(ConveyorRuntime conveyorRuntime)
        {
            var ownDefinition = (ConveyorDefinition)conveyorRuntime.Definition;
            if (ownDefinition.DefaultShape == conveyorRuntime.Orientation.Shape) return ownDefinition;

            switch (conveyorRuntime.Orientation.Shape)
            {
                case ConveyorShapeKind.Straight: return _straightConveyorArt != null ? _straightConveyorArt : ownDefinition;
                case ConveyorShapeKind.Corner: return _cornerConveyorArt != null ? _cornerConveyorArt : ownDefinition;
                default: return ownDefinition;
            }
        }

        /// <summary>
        /// Generic view for every non-conveyor building: a sprite sized to its footprint, plus
        /// an output arrow (and, for a recipe-based production building, entry arrows on every
        /// other side) if its Definition declares one. Covers Extractor/Storage/Foundry/Factory/
        /// AdvancedFoundry/Assembler/PowerplantGaz/DataCenter - the only per-type
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

            // An Extractor sits on its ore deposit, not open ground - a concrete pad under it
            // would cover the deposit's own art/terrain instead of the building's real footing.
            if (!(runtime is ExtractorRuntime))
            {
                SpawnGroundSlab(root.transform, runtime.Cell, definition.FootprintSize);
            }

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(root.transform, false);
            var renderer = spriteGo.AddComponent<SpriteRenderer>();
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

            if (definition.HasOutputArrow)
            {
                SpawnDirectionalArrow(root.transform, _grid.CellCenterToWorld(runtime.GetOutputCell()), runtime.ExitDirection, OutputArrowColor, OutputArrowSortingOrder, inward: false);
            }

            // Independent of the output arrow: a building can take deliveries without producing
            // anything physical (DataCenter). Same list the transport pull reads from, so an
            // arrow always marks a cell items are genuinely taken from - and only those cells.
            if (definition.HasInputArrows)
            {
                foreach ((GridCoord cell, Direction fromMySide) in runtime.GetInputCells())
                {
                    SpawnDirectionalArrow(root.transform, _grid.CellCenterToWorld(cell), fromMySide, InputArrowColor, InputArrowSortingOrder, inward: true);
                }
            }

            return root;
        }

        /// <summary>
        /// Concrete pad shown under a standard building, exactly matching its footprint - a
        /// tiled Custom/BuildingGroundSlab material with a per-cell random UV phase so adjacent
        /// buildings don't tile in visible lockstep, and a soft alpha fade at the footprint edge
        /// instead of a hard cutoff. No-op when no slab texture pair is configured (e.g. EditMode
        /// tests). Not used by conveyors/Splitter/Crossroad - see BuildingSpawner class docs on
        /// SpawnStandardView for why the transport family is excluded.
        /// </summary>
        void SpawnGroundSlab(Transform parent, GridCoord cell, Vector2Int footprintSize)
        {
            if (_groundSlabSettings == null || !_groundSlabSettings.CanRenderSlab) return;

            var slabGo = new GameObject("GroundSlab");
            slabGo.transform.SetParent(parent, false);
            var renderer = slabGo.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = GroundSlabSortingOrder;
            renderer.sharedMaterial = _spriteFactory.GetGroundSlabMaterial(_groundSlabSettings);

            Vector2 footprintWorldSize = new Vector2(_grid.CellSize, _grid.CellSize) * footprintSize;
            Vector2 slabWorldSize = footprintWorldSize + Vector2.one * (GroundSlabOverscanMargin * 2f);
            SetSpriteToWorldSize(renderer, _spriteFactory.GetGroundSlabUnitSprite(), slabWorldSize);
            ApplyGroundSlabPropertyBlock(renderer, cell, slabWorldSize);

            _groundSlabNeighborLinker?.Register(cell, footprintSize, renderer);
        }

        /// <summary>
        /// Per-instance shader inputs that the shared GetGroundSlabMaterial can't carry itself:
        /// a random UV phase (seeded by cell, so it's stable across a view rebuild) so adjacent
        /// slabs don't tile in visible lockstep, and the footprint's own world size so the
        /// shader's edge fade reads as the same physical width regardless of footprint size.
        /// </summary>
        static void ApplyGroundSlabPropertyBlock(SpriteRenderer renderer, GridCoord cell, Vector2 footprintWorldSize)
        {
            var random = new System.Random(cell.GetHashCode());
            var propertyBlock = new MaterialPropertyBlock();
            propertyBlock.SetVector("_UVOffset", new Vector4((float)random.NextDouble() * 10f, (float)random.NextDouble() * 10f, 0f, 0f));
            propertyBlock.SetVector("_FootprintWorldSize", new Vector4(footprintWorldSize.x, footprintWorldSize.y, 0f, 0f));
            renderer.SetPropertyBlock(propertyBlock);
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
            renderer.sortingOrder = CrossSortingOrder;
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

            // No-op if cell was never registered (e.g. a conveyor/Splitter/Crossroad, which never gets a slab).
            _groundSlabNeighborLinker?.Unregister(cell);
        }
    }
}

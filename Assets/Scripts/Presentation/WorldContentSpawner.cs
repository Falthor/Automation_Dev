using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Turns world-generated BuildingRuntime instances (Core, ore deposits) into their Unity
    /// GameObject/view, once at game start. Separate from BuildingSpawner: that one exists to
    /// service ConstructionService's player-driven place/demolish loop, this one is a one-shot
    /// spawn for world generation content that is never rebuilt or removed at runtime.
    /// </summary>
    public sealed class WorldContentSpawner
    {
        const int CoreSortingOrder = 10;
        const int GroundSlabSortingOrder = 5;
        const int OreDepositSortingOrder = 9;

        // How far the concrete slab bleeds past the Core's true footprint on each side, so it
        // reads as sitting on top of the ground rather than stopping exactly at the grid line -
        // matches BuildingSpawner.GroundSlabOverscanMargin.
        const float GroundSlabOverscanMargin = 0.3f;

        readonly GridRuntime _grid;
        readonly ProceduralSpriteFactory _spriteFactory;
        readonly GroundSlabSettings _groundSlabSettings;
        readonly GroundSlabNeighborLinker _groundSlabNeighborLinker;
        readonly BuildingShadowSettings _shadowSettings;

        /// <summary>
        /// groundSlabSettings is optional; null (or its diffuse/normal being null) means the Core
        /// spawns with no concrete pad. groundSlabNeighborLinker is optional too; null means the
        /// Core's slab (if any) never reacts to buildings placed next to it later. shadowSettings
        /// is optional as well; null means the Core casts no drop shadow - same all-or-nothing
        /// convention as the slab, no hardcoded fallback look.
        /// </summary>
        public WorldContentSpawner(GridRuntime grid, ProceduralSpriteFactory spriteFactory, GroundSlabSettings groundSlabSettings = null, GroundSlabNeighborLinker groundSlabNeighborLinker = null, BuildingShadowSettings shadowSettings = null)
        {
            _grid = grid;
            _spriteFactory = spriteFactory;
            _groundSlabSettings = groundSlabSettings;
            _groundSlabNeighborLinker = groundSlabNeighborLinker;
            _shadowSettings = shadowSettings;
        }

        public void SpawnCore(BuildingRuntime core)
        {
            var definition = (CoreDefinition)core.Definition;
            var go = new GameObject("Core");
            go.transform.position = _grid.FootprintCenterToWorld(core.Cell, definition.FootprintSize);

            Vector2 footprintWorldSize = WorldFootprintSize(definition.FootprintSize);

            if (_groundSlabSettings != null && _groundSlabSettings.CanRenderSlab)
            {
                var slabGo = new GameObject("GroundSlab");
                slabGo.transform.SetParent(go.transform, false);
                var slabRenderer = slabGo.AddComponent<SpriteRenderer>();
                slabRenderer.sortingOrder = GroundSlabSortingOrder;
                slabRenderer.sharedMaterial = _spriteFactory.GetGroundSlabMaterial(_groundSlabSettings);

                Vector2 slabWorldSize = footprintWorldSize + Vector2.one * (GroundSlabOverscanMargin * 2f);
                SetSpriteToWorldSize(slabRenderer, _spriteFactory.GetGroundSlabUnitSprite(), slabWorldSize);

                var random = new System.Random(core.Cell.GetHashCode());
                var propertyBlock = new MaterialPropertyBlock();
                propertyBlock.SetVector("_UVOffset", new Vector4((float)random.NextDouble() * 10f, (float)random.NextDouble() * 10f, 0f, 0f));
                propertyBlock.SetVector("_FootprintWorldSize", new Vector4(slabWorldSize.x, slabWorldSize.y, 0f, 0f));
                slabRenderer.SetPropertyBlock(propertyBlock);

                _groundSlabNeighborLinker?.Register(core.Cell, definition.FootprintSize, slabRenderer);
            }

            // The building's own sprite gets its own child (not the root) so fitting it to the
            // footprint only scales this child - the root stays unscaled, which GroundSlab (also
            // parented to the root, sized independently) relies on to avoid inheriting this scale.
            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(go.transform, false);
            var renderer = spriteGo.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = CoreSortingOrder;

            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);

            // A uniform fit, not the per-axis one the slab and the deposits use: the Core's art is
            // deliberately taller than its footprint (4 cells wide, 5 tall) to read as having
            // height, and stretching it to a square footprint would squash exactly that away.
            // Fitting on the widest-needed ratio pins the width to the 4x4 footprint and lets the
            // extra cell overhang upward, which is what the sprite's own pivot is placed for -
            // it sits at the footprint's centre, 2/5 up the art, so the base lands on the cells the
            // Core actually occupies. The concrete slab above stays on footprintWorldSize.
            BuildingSpawner.FitSpriteUniform(renderer, sprite, BuildingSpawner.ArtWorldSize(definition, _grid.CellSize));

            if (definition.AnimationFrames != null && definition.AnimationFrames.Length >= 2)
            {
                renderer.gameObject.AddComponent<SpriteFlipbook>().Initialize(definition.AnimationFrames, definition.AnimationFps);
            }

            // On the sprite child rather than the root: the shadow must be the silhouette that is
            // actually drawn, which is this renderer's current sprite (a flipbook frame included).
            if (_shadowSettings != null)
            {
                spriteGo.AddComponent<DropShadow>().Settings = _shadowSettings;
            }
        }

        public void SpawnOreDeposit(DepositRuntime deposit)
        {
            OreDepositDefinition definition = deposit.Definition;
            var go = new GameObject($"OreDeposit_{definition.Item.Id}");
            go.transform.position = _grid.FootprintCenterToWorld(deposit.Origin, definition.FootprintSize);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = OreDepositSortingOrder;

            // Real deposit art has its own transparent background, so the terrain tile
            // underneath must stay visible through it - unlike the solid-color placeholder
            // fallback, it must not be tinted by PlaceholderColor.
            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
            renderer.color = Color.white;
            SetSpriteToWorldSize(renderer, sprite, WorldFootprintSize(definition.FootprintSize));
        }

        Vector2 WorldFootprintSize(Vector2Int footprintCells)
        {
            return new Vector2(footprintCells.x * _grid.CellSize, footprintCells.y * _grid.CellSize);
        }

        static void SetSpriteToWorldSize(SpriteRenderer renderer, Sprite sprite, Vector2 desiredWorldSize)
        {
            renderer.sprite = sprite;
            Vector2 nativeSize = sprite.bounds.size;
            Vector3 scale = renderer.transform.localScale;
            scale.x = desiredWorldSize.x / nativeSize.x;
            scale.y = desiredWorldSize.y / nativeSize.y;
            renderer.transform.localScale = scale;
        }
    }
}

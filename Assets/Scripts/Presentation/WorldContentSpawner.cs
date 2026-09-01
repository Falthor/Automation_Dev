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
        const int OreDepositSortingOrder = 9;

        readonly GridRuntime _grid;
        readonly ProceduralSpriteFactory _spriteFactory;

        public WorldContentSpawner(GridRuntime grid, ProceduralSpriteFactory spriteFactory)
        {
            _grid = grid;
            _spriteFactory = spriteFactory;
        }

        public void SpawnCore(BuildingRuntime core)
        {
            var definition = (CoreDefinition)core.Definition;
            var go = new GameObject("Core");
            go.transform.position = _grid.FootprintCenterToWorld(core.Cell, definition.FootprintSize);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = CoreSortingOrder;

            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);

            SetSpriteToWorldSize(renderer, sprite, WorldFootprintSize(definition.FootprintSize));
        }

        public void SpawnOreDeposit(DepositRuntime deposit)
        {
            OreDepositDefinition definition = deposit.Definition;
            var go = new GameObject($"OreDeposit_{definition.OreType}");
            go.transform.position = _grid.FootprintCenterToWorld(deposit.Origin, definition.FootprintSize);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = OreDepositSortingOrder;

            Sprite sprite = _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
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

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
        const int ExtractorSortingOrder = 10;
        const int StorageSortingOrder = 10;
        const int OutputArrowSortingOrder = 11;
        static readonly Color OutputArrowColor = new Color(0.25f, 0.95f, 0.35f, 1f);

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
                view.Sync(conveyorRuntime, _spriteFactory, conveyorDefinition);

                _views[runtime.Cell] = go;
            }
            else if (runtime is ExtractorRuntime extractorRuntime)
            {
                _views[runtime.Cell] = SpawnExtractorView(extractorRuntime);
            }
            else if (runtime is StorageRuntime storageRuntime)
            {
                _views[runtime.Cell] = SpawnStorageView(storageRuntime);
            }
        }

        GameObject SpawnExtractorView(ExtractorRuntime extractor)
        {
            var definition = (ExtractorDefinition)extractor.Definition;

            // Root carries only position/rotation (no scale) so the sprite and the arrow can
            // each have their own independent scale without compounding through the hierarchy.
            var root = new GameObject($"Extractor {extractor.Cell}");
            root.transform.position = _grid.FootprintCenterToWorld(extractor.Cell, definition.FootprintSize);
            root.transform.rotation = Quaternion.Euler(0f, 0f, -extractor.FacingRotation.ToRotationDegrees());

            var spriteGo = new GameObject("Sprite");
            spriteGo.transform.SetParent(root.transform, false);
            var renderer = spriteGo.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = ExtractorSortingOrder;
            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
            SetSpriteToWorldSize(renderer, sprite, new Vector2(_grid.CellSize, _grid.CellSize) * definition.FootprintSize);

            var arrowGo = new GameObject("OutputArrow");
            arrowGo.transform.position = _grid.CellCenterToWorld(extractor.GetOutputCell());
            arrowGo.transform.rotation = root.transform.rotation;
            arrowGo.transform.localScale = Vector3.one * (_grid.CellSize * 0.4f);
            arrowGo.transform.SetParent(root.transform, true);

            var arrowRenderer = arrowGo.AddComponent<SpriteRenderer>();
            arrowRenderer.sortingOrder = OutputArrowSortingOrder;
            arrowRenderer.sprite = _spriteFactory.CreateArrowSprite(OutputArrowColor);

            return root;
        }

        GameObject SpawnStorageView(StorageRuntime storage)
        {
            var definition = (StorageDefinition)storage.Definition;
            var go = new GameObject($"Storage {storage.Cell}");
            go.transform.position = _grid.CellCenterToWorld(storage.Cell);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = StorageSortingOrder;
            Sprite sprite = definition.Sprite != null
                ? definition.Sprite
                : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
            SetSpriteToWorldSize(renderer, sprite, new Vector2(_grid.CellSize, _grid.CellSize) * definition.FootprintSize);

            return go;
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

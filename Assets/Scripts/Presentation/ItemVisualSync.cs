using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws a small colored square for every item currently in transit (sitting ready at an
    /// extractor's output, or riding a conveyor). Purely a view over TransportSystem's runtime
    /// state - it never owns or mutates gameplay state, and never searches the scene: it is
    /// driven directly by the same BuildingSpawner-tracked instances the input adapter placed.
    /// </summary>
    public sealed class ItemVisualSync : MonoBehaviour
    {
        const int ItemSortingOrder = 12;

        [SerializeField, Range(0.05f, 1f)] float itemVisualScale = 0.35f;

        readonly List<ExtractorRuntime> _extractors = new List<ExtractorRuntime>();
        readonly List<ConveyorRuntime> _conveyors = new List<ConveyorRuntime>();
        readonly Dictionary<object, GameObject> _views = new Dictionary<object, GameObject>();
        readonly HashSet<object> _liveKeys = new HashSet<object>();

        GridRuntime _grid;
        ProceduralSpriteFactory _spriteFactory;

        public void Initialize(GridRuntime grid, ProceduralSpriteFactory spriteFactory)
        {
            _grid = grid;
            _spriteFactory = spriteFactory;
        }

        public void Register(BuildingRuntime building)
        {
            if (building is ExtractorRuntime extractor) _extractors.Add(extractor);
            else if (building is ConveyorRuntime conveyor) _conveyors.Add(conveyor);
        }

        public void Unregister(BuildingRuntime building)
        {
            if (building is ExtractorRuntime extractor)
            {
                _extractors.Remove(extractor);
                RemoveView(extractor);
            }
            else if (building is ConveyorRuntime conveyor)
            {
                _conveyors.Remove(conveyor);
                RemoveView(conveyor);
            }
        }

        void LateUpdate()
        {
            if (_grid == null) return;

            _liveKeys.Clear();

            for (int i = 0; i < _extractors.Count; i++)
            {
                ExtractorRuntime extractor = _extractors[i];
                object pending = extractor.PeekPullableItem();
                if (pending == null) continue;

                _liveKeys.Add(extractor);
                Vector3 outputWorld = _grid.CellCenterToWorld(extractor.GetOutputCell());
                SyncView(extractor, pending, outputWorld);
            }

            for (int i = 0; i < _conveyors.Count; i++)
            {
                ConveyorRuntime conveyor = _conveyors[i];
                if (!conveyor.HasItem) continue;

                _liveKeys.Add(conveyor);
                Vector3 cellCenter = _grid.CellCenterToWorld(conveyor.Cell);
                Vector3 exitOffset = new Vector3(conveyor.Orientation.Rotation.ToOffset().X, conveyor.Orientation.Rotation.ToOffset().Y, 0f) * (_grid.CellSize * 0.5f);
                Vector3 backEdge = cellCenter - exitOffset;
                Vector3 frontEdge = cellCenter + exitOffset;
                Vector3 worldPos = Vector3.Lerp(backEdge, frontEdge, conveyor.ItemProgress);

                SyncView(conveyor, conveyor.CarriedItem, worldPos);
            }

            RemoveStaleViews();
        }

        void SyncView(object owner, object item, Vector3 worldPosition)
        {
            if (!_views.TryGetValue(owner, out GameObject go))
            {
                go = new GameObject("Item");
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = ItemSortingOrder;
                renderer.sprite = _spriteFactory.CreateSolidSquareSprite(ColorFor(item));
                go.transform.localScale = Vector3.one * (_grid.CellSize * itemVisualScale);
                _views[owner] = go;
            }

            go.transform.position = worldPosition;
        }

        void RemoveStaleViews()
        {
            List<object> toRemove = null;
            foreach (var kvp in _views)
            {
                if (!_liveKeys.Contains(kvp.Key))
                {
                    (toRemove ??= new List<object>()).Add(kvp.Key);
                }
            }

            if (toRemove == null) return;

            foreach (object key in toRemove)
            {
                Destroy(_views[key]);
                _views.Remove(key);
            }
        }

        void RemoveView(object owner)
        {
            if (_views.TryGetValue(owner, out GameObject go))
            {
                Destroy(go);
                _views.Remove(owner);
            }
        }

        static Color ColorFor(object item)
        {
            if (item is OreType ore)
            {
                return ore switch
                {
                    OreType.Iron => new Color(0.80f, 0.78f, 0.75f),
                    OreType.Copper => new Color(0.90f, 0.50f, 0.20f),
                    OreType.Coal => new Color(0.10f, 0.10f, 0.10f),
                    _ => Color.magenta
                };
            }

            return Color.magenta;
        }
    }
}

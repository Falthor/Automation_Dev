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

        /// <summary>
        /// Items riding a conveyor are drawn larger than elsewhere (e.g. an extractor's pending
        /// output) - up to MaxItemsPerCell of them share a single cell at once, so they need to
        /// read clearly at that reduced spacing rather than looking like a generic small icon.
        /// </summary>
        [SerializeField, Range(0.05f, 1f)] float conveyorItemVisualScale = 0.4f;

        readonly List<ExtractorRuntime> _extractors = new List<ExtractorRuntime>();
        readonly List<ConveyorRuntime> _conveyors = new List<ConveyorRuntime>();
        readonly List<SplitterRuntime> _splitters = new List<SplitterRuntime>();
        readonly List<CrossroadRuntime> _crossroads = new List<CrossroadRuntime>();
        readonly Dictionary<object, GameObject> _views = new Dictionary<object, GameObject>();
        readonly HashSet<object> _liveKeys = new HashSet<object>();

        GridRuntime _grid;
        ProceduralSpriteFactory _spriteFactory;
        ItemDatabase _itemDatabase;

        public void Initialize(GridRuntime grid, ProceduralSpriteFactory spriteFactory, ItemDatabase itemDatabase)
        {
            _grid = grid;
            _spriteFactory = spriteFactory;
            _itemDatabase = itemDatabase;
        }

        public void Register(BuildingRuntime building)
        {
            if (building is ExtractorRuntime extractor) _extractors.Add(extractor);
            else if (building is ConveyorRuntime conveyor) _conveyors.Add(conveyor);
            else if (building is SplitterRuntime splitter) _splitters.Add(splitter);
            else if (building is CrossroadRuntime crossroad) _crossroads.Add(crossroad);
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
                foreach (ConveyorItemSlot slot in conveyor.Items) RemoveView(slot);
            }
            else if (building is SplitterRuntime splitter)
            {
                _splitters.Remove(splitter);
                RemoveView(splitter);
            }
            else if (building is CrossroadRuntime crossroad)
            {
                _crossroads.Remove(crossroad);
                RemoveView((crossroad, 'A'));
                RemoveView((crossroad, 'B'));
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
                SyncView(extractor, pending, outputWorld, itemVisualScale);
            }

            for (int i = 0; i < _conveyors.Count; i++)
            {
                ConveyorRuntime conveyor = _conveyors[i];
                IReadOnlyList<ConveyorItemSlot> slots = conveyor.Items;
                if (slots.Count == 0) continue;

                Vector3 cellCenter = _grid.CellCenterToWorld(conveyor.Cell);
                float half = _grid.CellSize * 0.5f;
                Direction entryDirection = conveyor.Orientation.Rotation.Opposite();
                Vector3 entryEdge = cellCenter + DirectionOffset(entryDirection) * half;
                Vector3 exitEdge = cellCenter + DirectionOffset(conveyor.ExitDirection) * half;

                for (int s = 0; s < slots.Count; s++)
                {
                    ConveyorItemSlot slot = slots[s];
                    _liveKeys.Add(slot);

                    // A corner's entry and exit sides are perpendicular, not opposite - a
                    // straight lerp between them would cut across the cell on the wrong diagonal
                    // instead of following the belt's curve. Quadratic bezier through the cell
                    // center fixes that; for a straight conveyor entry/center/exit are colinear
                    // with center at the midpoint, so this reduces to the exact same lerp as
                    // before for that shape.
                    float t = slot.Progress;
                    float oneMinusT = 1f - t;
                    Vector3 worldPos = oneMinusT * oneMinusT * entryEdge + 2f * oneMinusT * t * cellCenter + t * t * exitEdge;

                    SyncView(slot, slot.Item, worldPos, conveyorItemVisualScale);
                }
            }

            // Splitter/Crossroad items are deliberately not drawn while held/in-transit through
            // these two building types (per explicit request) - they still occupy _splitters/
            // _crossroads for registration bookkeeping, just never added to _liveKeys/synced.

            RemoveStaleViews();
        }

        void SyncView(object owner, object item, Vector3 worldPosition, float visualScale)
        {
            if (!_views.TryGetValue(owner, out GameObject go))
            {
                go = new GameObject("Item");
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sortingOrder = ItemSortingOrder;
                Sprite sprite = SpriteFor(item as string);
                renderer.sprite = sprite;

                // Icons come from imported art with their own native pixel size/PPU, unlike the
                // procedural placeholder square (always exactly 1x1 world unit) - normalize
                // against the sprite's own bounds so every item reads at the same world size.
                float desiredWorldSize = _grid.CellSize * visualScale;
                Vector2 nativeSize = sprite.bounds.size;
                go.transform.localScale = new Vector3(desiredWorldSize / nativeSize.x, desiredWorldSize / nativeSize.y, 1f);
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

        static Vector3 DirectionOffset(Direction direction)
        {
            GridCoord offset = direction.ToOffset();
            return new Vector3(offset.X, offset.Y, 0f);
        }

        Sprite SpriteFor(string itemId)
        {
            ItemDefinition item = _itemDatabase != null ? _itemDatabase.Get(itemId) : null;
            if (item != null && item.Icon != null) return item.Icon;

            Color fallback = item != null ? item.FallbackColor : Color.magenta;
            return _spriteFactory.CreateSolidSquareSprite(fallback);
        }
    }
}

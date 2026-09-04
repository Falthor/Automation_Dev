using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>One item riding a conveyor. Reference identity (not ItemId) is what Presentation keys its per-item view on, so it stays stable while the item advances or the queue shifts.</summary>
    public sealed class ConveyorItemSlot
    {
        public object Item { get; }
        public float Progress { get; internal set; }

        internal ConveyorItemSlot(object item)
        {
            Item = item;
        }
    }

    public sealed class ConveyorRuntime : BuildingRuntime
    {
        /// <summary>How many items can queue on a single conveyor cell at once, evenly spaced - see MinItemSpacing.</summary>
        public const int MaxItemsPerCell = 3;

        /// <summary>Minimum progress gap kept between consecutive items so MaxItemsPerCell of them fit without overlapping.</summary>
        const float MinItemSpacing = 1f / MaxItemsPerCell;

        public ConveyorOrientation Orientation { get; private set; }

        /// <summary>Items currently riding this conveyor, front (closest to the exit, index 0) to back, for Presentation to draw. Use PeekPullableItem() for transport logic instead.</summary>
        public IReadOnlyList<ConveyorItemSlot> Items => _slots;

        public bool HasItem => _slots.Count > 0;

        /// <summary>True while another item can be accepted at the back edge - either the queue isn't full, or the back-most item has already moved far enough ahead to leave room.</summary>
        public bool HasRoomForNewItem => _slots.Count < MaxItemsPerCell && (_slots.Count == 0 || _slots[_slots.Count - 1].Progress >= MinItemSpacing);

        readonly List<ConveyorItemSlot> _slots = new List<ConveyorItemSlot>();

        public ConveyorRuntime(ConveyorDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
            Orientation = new ConveyorOrientation(definition.DefaultShape, facingRotation, mirrored: false);
        }

        /// <summary>Accepts an item at the back edge (progress 0). Caller must have checked HasRoomForNewItem first.</summary>
        public void ReceiveItem(object item)
        {
            _slots.Add(new ConveyorItemSlot(item));
        }

        /// <summary>
        /// Advances every carried item toward the front edge; call once per simulation tick.
        /// Each item is capped by the one ahead of it (MinItemSpacing back from it) so items
        /// queue up instead of overlapping - only the front item (index 0) can reach 1.
        /// </summary>
        public void AdvanceItem(float deltaTime, float speedCellsPerSecond)
        {
            float delta = deltaTime * speedCellsPerSecond;
            for (int i = 0; i < _slots.Count; i++)
            {
                float cap = i == 0 ? 1f : _slots[i - 1].Progress - MinItemSpacing;
                _slots[i].Progress = System.Math.Min(cap, _slots[i].Progress + delta);
            }
        }

        /// <summary>Configures a straight conveyor toward the requested exit.</summary>
        public void ConfigureAsStraight(Direction exitDirection)
        {
            Orientation = new ConveyorOrientation(ConveyorShapeKind.Straight, exitDirection, mirrored: false);
            FacingRotation = exitDirection;
        }

        /// <summary>
        /// Configures a corner between the requested entry and exit. Derived from one fixed
        /// canonical reference (rotation=North, unmirrored => entry=South, exit=East) rotated
        /// through the 4 cardinal steps, trying both chiralities - closed-form and exhaustive
        /// over the 8 valid perpendicular (entry, exit) pairs.
        /// </summary>
        public void ConfigureAsCorner(Direction entryDirection, Direction exitDirection)
        {
            if (entryDirection == exitDirection || entryDirection == exitDirection.Opposite())
            {
                throw new ArgumentException(
                    $"Corner requires perpendicular entry/exit directions, got entry={entryDirection}, exit={exitDirection}.");
            }

            for (int steps = 0; steps < 4; steps++)
            {
                Direction candidateEntry = Direction.South.RotateCW(steps);
                Direction candidateExitUnmirrored = Direction.East.RotateCW(steps);
                Direction candidateExitMirrored = Direction.West.RotateCW(steps);
                Direction rotation = Direction.North.RotateCW(steps);

                if (candidateEntry == entryDirection && candidateExitUnmirrored == exitDirection)
                {
                    Orientation = new ConveyorOrientation(ConveyorShapeKind.Corner, rotation, mirrored: false);
                    FacingRotation = rotation;
                    return;
                }

                if (candidateEntry == entryDirection && candidateExitMirrored == exitDirection)
                {
                    Orientation = new ConveyorOrientation(ConveyorShapeKind.Corner, rotation, mirrored: true);
                    FacingRotation = rotation;
                    return;
                }
            }

            // Unreachable: the perpendicularity check above guarantees one of the 8 combinations matches.
            throw new ArgumentException($"No corner orientation found for entry={entryDirection}, exit={exitDirection}.");
        }

        /// <summary>Sets the corner shape without implying a direction. Rotation is applied separately via SetRotation.</summary>
        public void ConfigureAsCornerShape()
        {
            Orientation = new ConveyorOrientation(ConveyorShapeKind.Corner, Orientation.Rotation, Orientation.Mirrored);
        }

        /// <summary>Applies a rotation to the current shape without changing it.</summary>
        public void SetRotation(Direction rotation)
        {
            Orientation = new ConveyorOrientation(Orientation.Shape, rotation, Orientation.Mirrored);
            FacingRotation = rotation;
        }

        public override bool IsFlowReceiver() => true;

        /// <summary>
        /// FacingRotation on a corner is the visual asset rotation, not the geometric exit -
        /// the canonical unmirrored corner (entry=South, exit=East) is itself stored as
        /// FacingRotation=North (its "no rotation" pose). Re-derive the true exit here so
        /// GetOutputCell() (and therefore transport pulls and drag-corner detection) work
        /// correctly for corners. Straight has no such offset, so this matches the
        /// base FacingRotation default for them.
        /// </summary>
        public override Direction ExitDirection
        {
            get
            {
                if (Orientation.Shape != ConveyorShapeKind.Corner) return Orientation.Rotation;

                int steps = (int)Orientation.Rotation;
                Direction baseExit = Orientation.Mirrored ? Direction.West : Direction.East;
                return baseExit.RotateCW(steps);
            }
        }

        public override object PeekPullableItem() => _slots.Count > 0 && _slots[0].Progress >= 1f ? _slots[0].Item : null;

        public override void ConsumePulledItem(object item)
        {
            if (_slots.Count > 0 && Equals(_slots[0].Item, item))
            {
                _slots.RemoveAt(0);
            }
        }

        public override JObject CaptureState()
        {
            var items = new JArray();
            foreach (ConveyorItemSlot slot in _slots)
            {
                items.Add(new JObject { ["itemId"] = slot.Item as string, ["progress"] = slot.Progress });
            }
            return new JObject
            {
                ["shape"] = (int)Orientation.Shape,
                ["rotation"] = (int)Orientation.Rotation,
                ["mirrored"] = Orientation.Mirrored,
                ["items"] = items
            };
        }

        public override void RestoreState(JObject state)
        {
            var shape = (ConveyorShapeKind)(state.Value<int?>("shape") ?? (int)Orientation.Shape);
            var rotation = (Direction)(state.Value<int?>("rotation") ?? (int)Orientation.Rotation);
            bool mirrored = state.Value<bool?>("mirrored") ?? false;
            Orientation = new ConveyorOrientation(shape, rotation, mirrored);
            FacingRotation = rotation;

            _slots.Clear();
            if (state["items"] is JArray items)
            {
                foreach (JToken entry in items)
                {
                    string itemId = entry.Value<string>("itemId");
                    float progress = entry.Value<float?>("progress") ?? 0f;
                    if (string.IsNullOrEmpty(itemId)) continue;
                    _slots.Add(new ConveyorItemSlot(itemId) { Progress = progress });
                }
            }
        }
    }
}

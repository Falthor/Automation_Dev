using System;
using Game.Core;
using Game.Data;

namespace Game.Gameplay.Buildings
{
    public sealed class ConveyorRuntime : BuildingRuntime
    {
        public ConveyorOrientation Orientation { get; private set; }

        /// <summary>0 = item just entered at this conveyor's back edge, 1 = reached the front edge and ready to hand off.</summary>
        public float ItemProgress { get; private set; }
        public bool HasItem { get; private set; }

        /// <summary>The item currently riding this conveyor regardless of progress, for Presentation to draw. Use PeekPullableItem() for transport logic instead.</summary>
        public object CarriedItem => _item;

        object _item;

        public ConveyorRuntime(ConveyorDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
            Orientation = new ConveyorOrientation(definition.DefaultShape, facingRotation, mirrored: false);
        }

        /// <summary>Accepts an item at the back edge (progress 0). Caller must have checked !HasItem first.</summary>
        public void ReceiveItem(object item)
        {
            _item = item;
            HasItem = true;
            ItemProgress = 0f;
        }

        /// <summary>Advances the carried item toward the front edge; call once per simulation tick.</summary>
        public void AdvanceItem(float deltaTime, float speedCellsPerSecond)
        {
            if (!HasItem) return;
            ItemProgress = System.Math.Min(1f, ItemProgress + deltaTime * speedCellsPerSecond);
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

        /// <summary>Sets the crossroad shape without implying a direction. Rotation is applied separately via SetRotation.</summary>
        public void ConfigureAsCrossroadShape()
        {
            Orientation = new ConveyorOrientation(ConveyorShapeKind.Crossroad, Orientation.Rotation, mirrored: false);
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
        /// correctly for corners. Straight/Crossroad have no such offset, so this matches the
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

        public override object PeekPullableItem() => HasItem && ItemProgress >= 1f ? _item : null;

        public override void ConsumePulledItem(object item)
        {
            if (HasItem && Equals(_item, item))
            {
                _item = null;
                HasItem = false;
                ItemProgress = 0f;
            }
        }
    }
}

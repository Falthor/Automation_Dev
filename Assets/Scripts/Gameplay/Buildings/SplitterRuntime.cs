using System.Collections.Generic;
using Game.Core;
using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Splits a single conveyor-fed item across up to 3 outputs (every cardinal side except its
    /// one fixed entry side), matching the source project's splitter.gd: round-robin assignment
    /// per item, only conveyor/splitter/storage neighbors count as valid destinations, and a
    /// jammed assigned exit doesn't block the others. Holds at most one item at a time - no
    /// internal buffer, same capacity as a single belt tile. Entirely driven by
    /// TransportSystem's dedicated splitter step (see TransportSystem.TickSplitters), not the
    /// generic push/pull path, because its input/output cells are arm tips of a "+" footprint,
    /// not the rectangular-footprint edges every other building uses.
    /// </summary>
    public sealed class SplitterRuntime : BuildingRuntime
    {
        static readonly Direction[] AllDirections = { Direction.North, Direction.East, Direction.South, Direction.West };

        int _cursor;

        public string HeldItemId { get; private set; }
        public Direction? AssignedExit { get; private set; }
        public bool HasItem => HeldItemId != null;

        /// <summary>Fixed entry side - rotating the splitter directly changes which side accepts input.</summary>
        public Direction EntrySide => FacingRotation;

        public SplitterRuntime(SplitterDefinition definition, GridCoord cell, Direction facingRotation)
            : base(definition, cell, facingRotation)
        {
        }

        /// <summary>Absolute cell of the arm tip in the given cardinal direction - the same "+" shape regardless of rotation, only EntrySide changes.</summary>
        public GridCoord ArmCell(Direction direction) => CrossFootprint.ArmCell(Cell, direction);

        /// <summary>Absolute cell one step beyond the arm tip - where a neighbor must sit to count as touching this side.</summary>
        public GridCoord NeighborCell(Direction direction) => CrossFootprint.NeighborCell(Cell, direction);

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            return HeldItemId == null && fromDirection == EntrySide;
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection)
        {
            HeldItemId = itemId;
            AssignedExit = null; // assigned lazily by TransportSystem the moment it attempts delivery
        }

        /// <summary>Marks the held item delivered - called only by TransportSystem once a neighbor has actually accepted it.</summary>
        public void ClearHeldItem()
        {
            HeldItemId = null;
            AssignedExit = null;
        }

        public void AssignExit(Direction exit) => AssignedExit = exit;

        /// <summary>Advances the round-robin cursor and returns the next candidate exit from a connected list (wrapping).</summary>
        public Direction NextCursorExit(IReadOnlyList<Direction> connected)
        {
            Direction exit = connected[_cursor % connected.Count];
            _cursor++;
            return exit;
        }

        /// <summary>Every cardinal side except the fixed entry side - up to 3 candidate outputs.</summary>
        public static IReadOnlyList<Direction> CandidateExits(Direction entrySide)
        {
            var result = new List<Direction>(3);
            foreach (Direction direction in AllDirections)
            {
                if (direction != entrySide) result.Add(direction);
            }
            return result;
        }
    }
}

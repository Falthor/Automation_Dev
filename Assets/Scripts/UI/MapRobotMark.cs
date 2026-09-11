using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// One explorer robot as the zoomed-out map needs it: where it is, and whether it is the one being
    /// inspected.
    ///
    /// <b>A snapshot, not a reference.</b> The map element is handed a list of these rather than the
    /// robots themselves, for the reason every other mark on this map follows: the element draws what
    /// it is told and reaches into no system of its own. It also makes "has anything actually moved"
    /// a comparison of values.
    /// </summary>
    public readonly struct MapRobotMark
    {
        /// <summary>Continuous position in cell space - a robot stands between cells, so this is not a coordinate.</summary>
        public readonly Vector2 CellPosition;

        /// <summary>Whether this is the robot whose panel is open. Drawn with a ring, the same idea as the halo the world puts on it.</summary>
        public readonly bool Selected;

        /// <summary>Whether it is out working rather than parked at the base. Parked robots still draw, so the player can see the fleet is home.</summary>
        public readonly bool IsOut;

        public MapRobotMark(Vector2 cellPosition, bool selected, bool isOut)
        {
            CellPosition = cellPosition;
            Selected = selected;
            IsOut = isOut;
        }

        /// <summary>
        /// Whether this says the same thing as another. Position is compared with a tolerance of a
        /// tenth of a cell: a robot moves continuously, so exact equality would report a change on
        /// every frame and repaint the overlay sixty times a second for movement nobody can see.
        /// </summary>
        public bool SameAs(MapRobotMark other)
            => Selected == other.Selected
                && IsOut == other.IsOut
                && (CellPosition - other.CellPosition).sqrMagnitude < 0.01f;
    }
}

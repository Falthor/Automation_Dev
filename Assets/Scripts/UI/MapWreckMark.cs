using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// One wreck the player has found, as the map needs it: where, and nothing else.
    ///
    /// Only discovered wrecks ever become one of these - an undiscovered wreck is not a thing the
    /// map knows about, and filtering here rather than at the draw would have put the rule in the
    /// wrong place.
    /// </summary>
    public readonly struct MapWreckMark
    {
        public readonly Vector2 CellPosition;

        public MapWreckMark(Vector2 cellPosition)
        {
            CellPosition = cellPosition;
        }

        /// <summary>Compared to a tenth of a cell, like the robot marks: a wreck never moves, so this only ever answers "the same set" or "one more".</summary>
        public bool SameAs(MapWreckMark other)
            => (CellPosition - other.CellPosition).sqrMagnitude < 0.01f;
    }
}

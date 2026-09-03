using Game.Core;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Absolute-cell math for the "+"-shaped footprint shared by Splitter and Crossroad (center +
    /// one arm per cardinal side, inside a 3x3 bounding box with free corners - see
    /// SplitterDefinition/CrossroadDefinition.FootprintCells). The shape itself is rotationally
    /// symmetric, so these offsets never change with FacingRotation - only which side plays
    /// which role (entry/exit) does, on the runtime types themselves.
    /// </summary>
    static class CrossFootprint
    {
        public static GridCoord ArmCell(GridCoord origin, Direction direction)
        {
            switch (direction)
            {
                case Direction.North: return new GridCoord(origin.X + 1, origin.Y + 2);
                case Direction.South: return new GridCoord(origin.X + 1, origin.Y);
                case Direction.East: return new GridCoord(origin.X + 2, origin.Y + 1);
                default: return new GridCoord(origin.X, origin.Y + 1); // West
            }
        }

        /// <summary>The cell one step beyond the arm tip - where a neighbor must sit to count as touching this side.</summary>
        public static GridCoord NeighborCell(GridCoord origin, Direction direction) => ArmCell(origin, direction) + direction.ToOffset();
    }
}

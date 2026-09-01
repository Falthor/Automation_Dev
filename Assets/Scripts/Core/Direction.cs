namespace Game.Core
{
    /// <summary>Cardinal direction on the orthogonal grid.</summary>
    public enum Direction
    {
        North,
        East,
        South,
        West
    }

    public static class DirectionExtensions
    {
        /// <summary>Opposite cardinal direction.</summary>
        public static Direction Opposite(this Direction direction)
        {
            return (Direction)(((int)direction + 2) % 4);
        }

        /// <summary>Rotates clockwise by the given number of 90-degree steps (negative steps rotate counter-clockwise).</summary>
        public static Direction RotateCW(this Direction direction, int steps)
        {
            int normalized = (((int)direction + steps) % 4 + 4) % 4;
            return (Direction)normalized;
        }

        /// <summary>Unit grid offset for this direction (North = +Y, East = +X).</summary>
        public static GridCoord ToOffset(this Direction direction)
        {
            switch (direction)
            {
                case Direction.North: return new GridCoord(0, 1);
                case Direction.East: return new GridCoord(1, 0);
                case Direction.South: return new GridCoord(0, -1);
                default: return new GridCoord(-1, 0);
            }
        }

        /// <summary>North=0, East=90, South=180, West=270.</summary>
        public static int ToRotationDegrees(this Direction direction) => (int)direction * 90;

        public static Direction FromRotationDegrees(int degrees)
        {
            int normalized = ((degrees / 90) % 4 + 4) % 4;
            return (Direction)normalized;
        }
    }
}

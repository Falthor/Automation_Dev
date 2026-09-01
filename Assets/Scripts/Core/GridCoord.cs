using System;
using UnityEngine;

namespace Game.Core
{
    /// <summary>Integer grid cell coordinate. The runtime grid's unit of addressing.</summary>
    public readonly struct GridCoord : IEquatable<GridCoord>
    {
        public readonly int X;
        public readonly int Y;

        public GridCoord(int x, int y)
        {
            X = x;
            Y = y;
        }

        public static GridCoord operator +(GridCoord a, GridCoord b) => new GridCoord(a.X + b.X, a.Y + b.Y);
        public static GridCoord operator +(GridCoord coord, Direction direction) => coord + direction.ToOffset();

        public Vector2Int ToVector2Int() => new Vector2Int(X, Y);
        public static GridCoord FromVector2Int(Vector2Int v) => new GridCoord(v.x, v.y);

        public bool Equals(GridCoord other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is GridCoord other && Equals(other);
        public override int GetHashCode() => (X, Y).GetHashCode();
        public static bool operator ==(GridCoord a, GridCoord b) => a.Equals(b);
        public static bool operator !=(GridCoord a, GridCoord b) => !a.Equals(b);

        public override string ToString() => $"({X}, {Y})";
    }
}

namespace Game.UI
{
    /// <summary>
    /// One cell a building occupies, in the terms the map draws it in.
    ///
    /// <b>Per cell, not per building.</b> A splitter's footprint is a cross and a crossroad's is not the
    /// rectangle its size suggests, so drawing one rectangle per building would claim ground the player
    /// does not actually hold. The panel expands each building through its definition's footprint cells,
    /// which is the same list the grid, the demolition and the action-radius check all go through.
    ///
    /// <b>Two colours only.</b> What the base looks like from above is its shape and its transport
    /// network, not its building types - a legend of nine colours on a map read at this distance would
    /// be a legend nobody reads.
    /// </summary>
    public readonly struct MapBuildingCell
    {
        public readonly int X;
        public readonly int Y;

        /// <summary>Conveyors, splitters and crossroads. Drawn white against everything else's blue.</summary>
        public readonly bool IsBelt;

        public MapBuildingCell(int x, int y, bool isBelt)
        {
            X = x;
            Y = y;
            IsBelt = isBelt;
        }

        public bool SameAs(MapBuildingCell other) => X == other.X && Y == other.Y && IsBelt == other.IsBelt;
    }
}

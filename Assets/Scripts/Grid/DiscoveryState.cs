namespace Game.Grid
{
    /// <summary>
    /// What the player knows about one cell.
    ///
    /// An enum rather than a bool although two values are enough today: a third state - discovered
    /// but not currently observed - becomes necessary once there are nests and enemy units to watch,
    /// and swapping a bool for an enum at that point would touch every call site instead of none.
    /// The distinction it will carry is the static versus the living: a deposit, once seen, stays
    /// drawn because it does not move, while a nest would be showing information already out of date.
    /// </summary>
    public enum DiscoveryState : byte
    {
        /// <summary>Never seen. Under the fog.</summary>
        Unknown = 0,

        /// <summary>Seen at least once. Permanent - a discovered cell is never un-discovered, even once the Core's radius no longer covers it.</summary>
        Discovered = 1
    }
}

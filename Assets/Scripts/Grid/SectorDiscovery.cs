namespace Game.Grid
{
    /// <summary>
    /// How much of a sector the player has seen. Derived from the sector's cells every time it is
    /// asked for, never stored - <see cref="DiscoveryRuntime"/> is the only authority on discovery,
    /// and a cached copy here would be a second one that drifts.
    /// </summary>
    public enum SectorDiscovery
    {
        /// <summary>Nothing in it has been seen.</summary>
        Unknown = 0,

        /// <summary>Some of it has. The resting state of a sector opened by a mission: the inscribed disc leaves the four corners hidden for good.</summary>
        Partial = 1,

        /// <summary>Every cell seen - which takes exploring the corners, not just a mission.</summary>
        Discovered = 2
    }
}

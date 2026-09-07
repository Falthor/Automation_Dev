namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// The one point of interest a sector holds. Exactly one, which is what fixes the sector's size:
    /// a mission's reward has to be nameable ("an ore cluster", "a wreck"), and a sector holding
    /// three of them makes the choice of destination indifferent.
    ///
    /// Always placed at the sector's centre, so it always falls inside the revealed disc - a
    /// successful mission always shows what it found.
    /// </summary>
    public enum SectorFeature
    {
        /// <summary>Nothing worth naming. Kept as a value so an empty sector is a stated outcome rather than an absent one.</summary>
        None = 0,

        OreCluster = 1,
        Wreck = 2,
        Nest = 3
    }
}

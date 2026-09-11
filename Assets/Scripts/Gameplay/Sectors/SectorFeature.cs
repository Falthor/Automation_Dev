namespace Game.Gameplay.Sectors
{
    /// <summary>
    /// What a sector holds, when it holds anything.
    ///
    /// <b>Two members, and the two that were removed are worth a line.</b> A wreck and a nest used
    /// to be drawn here and nothing ever rendered one: materialisation reads only the deposit cells,
    /// so their whole effect was to divide the ore rate by three. Deleted rather than left as dead
    /// values - a wreck is now a real thing with its own placement and its own art
    /// (<c>Game.Gameplay.Wrecks</c>), derived on rings around the Core rather than per sector, and
    /// keeping a second unrelated meaning of the word here could only mislead.
    /// </summary>
    public enum SectorFeature
    {
        /// <summary>Nothing. Kept as a value so an empty sector is a stated outcome rather than an absent one.</summary>
        None = 0,

        OreCluster = 1
    }
}

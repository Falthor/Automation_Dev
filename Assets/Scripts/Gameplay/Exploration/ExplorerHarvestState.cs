namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// Whether a robot is still earning, and if not, why not.
    ///
    /// <b>Derived, never stored</b> - a plain reading of the robot's state, its card count and
    /// whether its last few reveals turned up anything. It exists because the panel has one question
    /// to answer that the raw numbers do not: a robot that is full, or one going back over ground it
    /// already opened, looks exactly like one at work.
    /// </summary>
    public enum ExplorerHarvestState
    {
        /// <summary>At the base, doing nothing.</summary>
        AtBase,

        /// <summary>On its way home. Reveals nothing, so earns nothing.</summary>
        Returning,

        /// <summary>Wandering, and opening ground it is paid for.</summary>
        Harvesting,

        /// <summary>Wandering over what it has already opened. Still exploring, earning nothing.</summary>
        OverKnownGround,

        /// <summary>Carrying all the cards it can. Still wandering - there is no automatic return - but no longer harvesting.</summary>
        StockFull
    }
}

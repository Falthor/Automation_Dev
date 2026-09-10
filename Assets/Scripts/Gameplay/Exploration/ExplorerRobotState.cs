namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// What an explorer robot is doing. Three states and no more: free exploration is an action the
    /// player starts and interrupts, not an expedition that resolves - there is no launch, no
    /// duration and no arrival.
    ///
    /// Saved as <c>(int)</c>, so members are <b>appended, never inserted</b>.
    /// </summary>
    public enum ExplorerRobotState
    {
        /// <summary>At the base, still.</summary>
        Idle = 0,

        /// <summary>Wandering, and uncovering ground as it goes.</summary>
        Exploring = 1,

        /// <summary>Heading straight home, uncovering nothing new.</summary>
        Returning = 2
    }
}

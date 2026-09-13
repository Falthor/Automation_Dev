namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// What an explorer robot is doing. Three states and no more - manual control
    /// (<see cref="ExplorerRobotRuntime.Auto"/>) is layered orthogonally on top of these rather than
    /// adding a fourth: "out in the field" is still one state whether the robot is wandering under
    /// its own steering or converging on a player-placed <see cref="ExplorerRobotRuntime.ManualTarget"/>.
    ///
    /// Saved as <c>(int)</c>, so members are <b>appended, never inserted</b>.
    /// </summary>
    public enum ExplorerRobotState
    {
        /// <summary>At the base, still.</summary>
        Idle = 0,

        /// <summary>
        /// Out in the field, uncovering ground as it goes - wandering under its own steering when
        /// <see cref="ExplorerRobotRuntime.Auto"/> is true, converging on a right-clicked
        /// <see cref="ExplorerRobotRuntime.ManualTarget"/> when it is false, or simply holding
        /// position when Auto is off and no target is set.
        /// </summary>
        Exploring = 1,

        /// <summary>Heading straight home, uncovering nothing new.</summary>
        Returning = 2
    }
}

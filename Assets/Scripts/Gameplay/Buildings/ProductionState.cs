namespace Game.Gameplay.Buildings
{
    /// <summary>Production state machine states (CONTRACTS.md §6).</summary>
    public enum ProductionState
    {
        Idle,
        Producing,
        WaitingResources,
        OutputBlocked,
        WaitingCompute,

        /// <summary>Switched off by the player. Distinct from Idle, which is "nothing to do": a paused building has been told to stop, draws no power, and stays exactly where it was until switched back on.</summary>
        Paused
    }
}

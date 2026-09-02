namespace Game.Gameplay.Buildings
{
    /// <summary>Production state machine states (CONTRACTS.md §6).</summary>
    public enum ProductionState
    {
        Idle,
        Producing,
        WaitingResources,
        OutputBlocked,
        WaitingCompute
    }
}

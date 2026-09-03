namespace Game.Gameplay.Power
{
    /// <summary>
    /// Global power supply/demand (CONTRACTS.md §9). Report-then-settle, one frame of lag by
    /// design: buildings call ReportDemand/ReportSupply during their own tick this frame;
    /// Settle() (called once per GameRuntime.Update(), before that tick) moves last frame's
    /// reports into the settled totals IsPowered() reads. Binary powered/unpowered - no partial
    /// degradation, and recovery is automatic the instant reported demand drops back at/under
    /// supply (no cooldown).
    /// </summary>
    public sealed class PowerSystem
    {
        float _pendingDemand;
        float _pendingSupply;

        public float SettledDemand { get; private set; }
        public float SettledSupply { get; private set; }

        public void ReportDemand(float kilowatts) => _pendingDemand += kilowatts;
        public void ReportSupply(float kilowatts) => _pendingSupply += kilowatts;

        public bool IsPowered() => SettledDemand <= SettledSupply;

        /// <summary>Moves this frame's reports into the settled totals and clears the pending accumulators for the next frame.</summary>
        public void Settle()
        {
            SettledDemand = _pendingDemand;
            SettledSupply = _pendingSupply;
            _pendingDemand = 0f;
            _pendingSupply = 0f;
        }
    }
}

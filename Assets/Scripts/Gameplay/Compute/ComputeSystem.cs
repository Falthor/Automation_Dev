namespace Game.Gameplay.Compute
{
    /// <summary>
    /// Global compute supply/demand (CONTRACTS.md §10). Two independent mechanisms share the
    /// same settled supply number:
    /// 1) Continuous flow: consumers report a CU/s demand every frame; GetPerformanceRatio()
    ///    (settled supply / settled demand, capped at 1) throttles all of them proportionally.
    ///    Same report-then-settle, one-frame-lag pattern as PowerSystem.
    /// 2) A pooled reserve (capped at ReserveCap) that grows every frame by settled supply * dt
    ///    and is spent in one-shot chunks (a recipe's ComputeCost at cycle start) - never
    ///    throttled by the performance ratio, since it is a spent balance, not a continuous draw.
    /// </summary>
    public sealed class ComputeSystem
    {
        public const float ReserveCap = 25000f;

        float _pendingDemand;
        float _pendingSupply;

        public float SettledDemand { get; private set; }
        public float SettledSupply { get; private set; }
        public float Reserve { get; private set; } = ReserveCap;

        public void ReportDemand(float cuPerSecond) => _pendingDemand += cuPerSecond;
        public void ReportSupply(float cuPerSecond) => _pendingSupply += cuPerSecond;

        public float GetPerformanceRatio()
        {
            if (SettledDemand <= 0f) return 1f;
            float ratio = SettledSupply / SettledDemand;
            return ratio > 1f ? 1f : ratio;
        }

        public bool CanSpend(float cost) => cost <= Reserve;

        /// <summary>Deducts cost from the reserve. Caller must have checked CanSpend first.</summary>
        public void Spend(float cost) => Reserve -= cost;

        /// <summary>Grows the reserve by settled supply * deltaTime, capped at ReserveCap. Call once per frame from GameRuntime.Update().</summary>
        public void GrowReserve(float deltaTime)
        {
            Reserve += SettledSupply * deltaTime;
            if (Reserve > ReserveCap) Reserve = ReserveCap;
        }

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

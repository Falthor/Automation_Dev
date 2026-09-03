namespace Game.Gameplay.Compute
{
    /// <summary>
    /// Global compute pool (CONTRACTS.md §10). CU is a currency, not a flow: it is credited into
    /// a capped reserve - the Core grants a fixed amount at a fixed interval, a Data Center
    /// credits its own output as it produces it - and spent in one-shot chunks the moment a
    /// production cycle starts. There is no continuous per-second draw, and therefore no
    /// throttling ratio: a building either can afford the cycle it is about to start or waits.
    /// </summary>
    public sealed class ComputeSystem
    {
        public const float ReserveCap = 25000f;

        /// <summary>Length of the window IncomePerSecond is averaged over - long enough that a Core grant arriving every few seconds reads as a steady rate rather than a spike.</summary>
        const float IncomeWindowSeconds = 5f;

        float _grantedInWindow;
        float _windowTimer;

        public float Reserve { get; private set; } = ReserveCap;

        /// <summary>CU actually credited per second, averaged over the last window - what the UI shows as production.</summary>
        public float IncomePerSecond { get; private set; }

        /// <summary>Credits CU into the reserve, clamped at ReserveCap. Anything over the cap is lost, not banked.</summary>
        public void Grant(float amount)
        {
            if (amount <= 0f) return;

            float before = Reserve;
            Reserve = System.Math.Min(Reserve + amount, ReserveCap);
            _grantedInWindow += Reserve - before;
        }

        public bool CanSpend(float cost) => cost <= Reserve;

        /// <summary>Deducts cost from the reserve. Caller must have checked CanSpend first.</summary>
        public void Spend(float cost) => Reserve -= cost;

        /// <summary>Advances the income-rate window. Call once per frame from GameRuntime.Update().</summary>
        public void Tick(float deltaTime)
        {
            _windowTimer += deltaTime;
            if (_windowTimer < IncomeWindowSeconds) return;

            IncomePerSecond = _grantedInWindow / _windowTimer;
            _grantedInWindow = 0f;
            _windowTimer = 0f;
        }
    }
}

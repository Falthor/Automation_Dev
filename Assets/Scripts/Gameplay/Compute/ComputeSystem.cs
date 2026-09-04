namespace Game.Gameplay.Compute
{
    /// <summary>
    /// Global compute pool (CONTRACTS.md §10). CU is a currency, not a flow for every spender but
    /// one: a production cycle (recipe-based building, Extractor, Laboratory-successor, Gas
    /// Powerplant) still pays in a single one-shot chunk the moment it starts, via
    /// CanSpend/Spend - there is no throttling ratio for those, a cycle either can afford itself
    /// or waits. Research absorption (Game.Gameplay.Research) is the one continuous per-second
    /// draw, via SpendUpTo - it never takes more than the reserve currently holds, so it floors
    /// at zero instead of going negative.
    /// </summary>
    public sealed class ComputeSystem
    {
        public const float ReserveCap = 60000f;

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

        /// <summary>
        /// Withdraws up to maxAmount from the reserve - less if the reserve holds less - and
        /// returns how much was actually taken. The one continuous per-second draw CONTRACTS.md
        /// §10 allows (research absorption); every other spender still uses the one-shot
        /// CanSpend/Spend pair above. Never drives the reserve below zero and never throws when
        /// there isn't enough - the caller (ResearchSystem) treats a partial or zero return as
        /// the research simply progressing slower, or pausing, that tick.
        /// </summary>
        public float SpendUpTo(float maxAmount)
        {
            if (maxAmount <= 0f) return 0f;

            float taken = System.Math.Min(maxAmount, Reserve);
            Reserve -= taken;
            return taken;
        }

        /// <summary>Advances the income-rate window. Call once per frame from GameRuntime.Update().</summary>
        public void Tick(float deltaTime)
        {
            _windowTimer += deltaTime;
            if (_windowTimer < IncomeWindowSeconds) return;

            IncomePerSecond = _grantedInWindow / _windowTimer;
            _grantedInWindow = 0f;
            _windowTimer = 0f;
        }

        /// <summary>Restores a previously-captured reserve (CONTRACTS.md §14), clamped to ReserveCap. Used only by the save/load system.</summary>
        public void RestoreReserve(float reserve)
        {
            Reserve = System.Math.Min(System.Math.Max(reserve, 0f), ReserveCap);
        }
    }
}

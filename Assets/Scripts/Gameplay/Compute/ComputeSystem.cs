namespace Game.Gameplay.Compute
{
    /// <summary>
    /// Global compute pool (CALCUL.md). CU is a currency, not a flow for every spender but
    /// two: a production cycle (recipe-based building, Extractor, Gas Powerplant) still pays in a
    /// single one-shot chunk the moment it starts, via CanSpend/Spend - there is no throttling
    /// ratio for those, a cycle either can afford itself or waits. Research absorption
    /// (Game.Gameplay.Research.ResearchSystem) and Data Center priming
    /// (Game.Gameplay.Buildings.DataCenterRuntime) are the two continuous per-second draws, both
    /// via SpendUpTo - it never takes more than the reserve currently holds, so it floors at zero
    /// instead of going negative.
    /// </summary>
    public sealed class ComputeSystem
    {
        public const float ReserveCap = 70000f;

        /// <summary>
        /// The reserve level at which the explorer fleet arrives (MAP.md), as a fraction of the
        /// cap. <b>It lives beside the cap, and is never an absolute.</b>
        ///
        /// An absolute is left behind whenever the cap moves, which turns an emergency trigger into
        /// an introduction trigger with nothing to signal it; a fraction has no second number to
        /// forget. And it belongs to the reserve it is read against rather than to whatever reads
        /// it - carried on the robots' own settings, it drifts away with them.
        /// </summary>
        public const float ExplorerFleetArrivalFraction = 0.285714f;

        /// <summary>What the reserve has to have fallen to for the fleet to arrive. Derived, so it follows the cap on its own and there is no second value anyone could set.</summary>
        public static float ExplorerFleetArrivalReserve => ReserveCap * ExplorerFleetArrivalFraction;

        /// <summary>Length of the window IncomePerSecond is averaged over - long enough that a Core grant arriving every few seconds reads as a steady rate rather than a spike.</summary>
        const float IncomeWindowSeconds = 5f;

        float _grantedInWindow;
        float _windowTimer;

        public float Reserve { get; private set; }

        /// <summary>Building Compute and Armament Compute start full, like the single reserve this class used to be the only instance of. Research Compute starts empty - see GameRuntime.</summary>
        public ComputeSystem(float startingReserve = ReserveCap)
        {
            Reserve = System.Math.Min(System.Math.Max(startingReserve, 0f), ReserveCap);
        }

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
        /// returns how much was actually taken. The only continuous per-second draw CALCUL.md
        /// allows, and it has exactly two callers - research absorption and Data Center priming;
        /// every other spender still uses the one-shot CanSpend/Spend pair above. Never drives the
        /// reserve below zero and never throws when there isn't enough - a caller treats a partial
        /// or zero return as its own progress simply slowing, or pausing, that tick.
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

        /// <summary>Restores a previously-captured reserve (SAUVEGARDE.md), clamped to ReserveCap. Used only by the save/load system.</summary>
        public void RestoreReserve(float reserve)
        {
            Reserve = System.Math.Min(System.Math.Max(reserve, 0f), ReserveCap);
        }
    }
}

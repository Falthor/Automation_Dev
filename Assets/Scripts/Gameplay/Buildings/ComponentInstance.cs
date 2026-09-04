using Game.Data;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Runtime state of one installed CPU/Memory component in a Data Center slot. Each installed
    /// component needs independent state, so this is a plain mutable instance, never a shared
    /// ScriptableObject (TASK_03_DATACENTER.md §4).
    ///
    /// Wear (100 = new, 0 = dead) now drives both Stability and the fluctuation floor instead of
    /// living beside a fixed 80% constant, and decays continuously at a rate that itself
    /// accelerates as Wear drops - a component gets visibly shaky before it dies, instead of
    /// staying silently perfect until it suddenly isn't. The nominal lifetime a fresh component
    /// draws is dispersed ±25% via a seeded generator (DataCenterRuntime owns the System.Random,
    /// for the project's own determinism rule - same seed and parameters, same result) so
    /// components installed together don't all fail together.
    /// </summary>
    public sealed class ComponentInstance
    {
        public string ItemId { get; }

        /// <summary>Nominal CU/s, snapshotted once at construction from ItemDefinition.CuOutput - independent of later registry balance changes.</summary>
        public float BaseCu { get; }

        /// <summary>kW drawn while installed and active, snapshotted once at construction from ItemDefinition.PowerKw.</summary>
        public float PowerKw { get; }

        /// <summary>This instance's own drawn lifetime (seconds), ±25% around ItemDefinition.NominalLifetimeSeconds - see DrawLifetimeSeconds.</summary>
        public float NominalLifetimeSeconds { get; private set; }

        /// <summary>
        /// Wear lost per second at Wear=100 (before the acceleration multiplier in DecayWear).
        /// Derived once at construction so that, left undisturbed, Wear reaches the replacement
        /// threshold in effect at that moment exactly at NominalLifetimeSeconds - see DecayWear.
        /// </summary>
        public float BaseLossPerSecond { get; private set; }

        /// <summary>Percent, 0..100. Starts fully unworn, decays via DecayWear(). This is the "usure" of TASK_03_DATACENTER.md's formulas - 100 at install, 0 at death.</summary>
        public float Wear { get; private set; } = 100f;

        /// <summary>95% at Wear=100 (new), 30% at Wear=0 (dead) - TASK_03_DATACENTER.md §4.1. The probability RecalculatePerformance() uses for a full-performance roll, not itself the output multiplier.</summary>
        public float Stability => 95f - 65f * (1f - Wear / 100f);

        /// <summary>
        /// Lower bound of the fluctuation roll when Stability's own coin flip misses: 0.70 at
        /// Wear=100 (new), 0.30 at Wear=0 (dead) - TASK_03_DATACENTER.md §4.2 ("un composant neuf
        /// fluctue entre 70% et 100%, un composant en fin de vie entre 30% et 100%"). The ceiling
        /// is always 1.0.
        /// </summary>
        public float FluctuationFloor => 0.70f - 0.40f * (1f - Wear / 100f);

        /// <summary>0..1 multiplier actually applied to BaseCu. Starts at 1 until the first 5s recalculation.</summary>
        public float EffectivePerformance { get; private set; } = 1f;

        /// <summary>True from the moment Wear crosses the replacement threshold until swapped or hard-removed. While true, EffectiveCu() is forced to 0.</summary>
        public bool IsReplacing { get; set; }

        /// <summary>Seconds elapsed since IsReplacing became true.</summary>
        public float ReplacementElapsed { get; set; }

        /// <summary>Fresh install: draws this instance's own dispersed lifetime and derives BaseLossPerSecond from the replacement threshold in effect right now (DataCenterRuntime's own per-type setting) - a later change to that threshold reshapes when HasCrossedReplacementThreshold trips, but never retroactively reshapes an already-derived decay curve.</summary>
        public ComponentInstance(string itemId, ItemDatabase itemDatabase, System.Random lifetimeRandom, float replacementThresholdPercentAtInstall)
        {
            ItemId = itemId;
            ItemDefinition item = itemDatabase.Get(itemId);
            BaseCu = item != null ? item.CuOutput : 0f;
            PowerKw = item != null ? item.PowerKw : 0f;

            float nominal = item != null ? item.NominalLifetimeSeconds : 0f;
            NominalLifetimeSeconds = DrawLifetimeSeconds(nominal, lifetimeRandom);
            BaseLossPerSecond = DeriveBaseLossPerSecond(NominalLifetimeSeconds, replacementThresholdPercentAtInstall);
        }

        /// <summary>Restore path only (CONTRACTS.md §14): reconstructs a component with its already-drawn lifetime/decay curve verbatim, instead of drawing a new one - a save must reproduce exactly what existed, not a fresh roll.</summary>
        public ComponentInstance(string itemId, ItemDatabase itemDatabase, float nominalLifetimeSeconds, float baseLossPerSecond)
        {
            ItemId = itemId;
            ItemDefinition item = itemDatabase.Get(itemId);
            BaseCu = item != null ? item.CuOutput : 0f;
            PowerKw = item != null ? item.PowerKw : 0f;
            NominalLifetimeSeconds = nominalLifetimeSeconds;
            BaseLossPerSecond = baseLossPerSecond;
        }

        /// <summary>±25% around the item's nominal lifetime - TASK_03_DATACENTER.md §4.4. Uniform draw via the caller-owned seeded generator, not UnityEngine.Random, so the sequence is reproducible for a given seed and installation order.</summary>
        static float DrawLifetimeSeconds(float nominalSeconds, System.Random random)
        {
            return nominalSeconds * (0.75f + (float)random.NextDouble() * 0.50f);
        }

        /// <summary>
        /// Solves dWear/dt = -baseLoss * (1 + 2*(1 - Wear/100)) for the baseLoss that carries Wear
        /// from 100 to thresholdPercent in exactly lifetimeSeconds (TASK_03_DATACENTER.md §4.4's
        /// calibration rule). Closed form from integrating the linear ODE: with w = Wear/100,
        /// dw/dt = -k*(3-2w) (k = baseLoss/100) gives 3-2w = e^(2kt), so
        /// k = ln(3 - 2*thresholdFraction) / (2*lifetimeSeconds).
        /// </summary>
        /// <summary>Exposed (not just used internally) so DataCenterRuntime's Restore fallback can rederive a plausible BaseLossPerSecond for a blob missing that key, without duplicating the formula.</summary>
        public static float DeriveBaseLossPerSecond(float lifetimeSeconds, float thresholdPercent)
        {
            if (lifetimeSeconds <= 0f) return 0f;

            float thresholdFraction = UnityEngine.Mathf.Clamp01(thresholdPercent / 100f);
            float k = UnityEngine.Mathf.Log(3f - 2f * thresholdFraction) / (2f * lifetimeSeconds);
            return 100f * k;
        }

        /// <summary>One stability roll: Stability% chance of 100% performance, otherwise a fluctuation uniformly between FluctuationFloor and 100%. Never touches BaseCu.</summary>
        public void RecalculatePerformance()
        {
            EffectivePerformance = UnityEngine.Random.value < Stability / 100f ? 1f : UnityEngine.Random.Range(FluctuationFloor, 1.0f);
        }

        /// <summary>
        /// Continuous decay at baseLoss * (1 + 2*(1 - Wear/100)) percentage points per second -
        /// TASK_03_DATACENTER.md §4.3: accelerates as Wear drops, floored at 0. Called every tick
        /// with the real deltaTime (not on a fixed interval) since the rate itself depends on the
        /// current Wear.
        /// </summary>
        public void DecayWear(float deltaTime)
        {
            float lossPerSecond = BaseLossPerSecond * (1f + 2f * (1f - Wear / 100f));
            Wear = UnityEngine.Mathf.Max(0f, Wear - lossPerSecond * deltaTime);
        }

        /// <summary>Compares Wear against the CURRENT threshold setting (not one snapshotted at install) - the player can move the slider at any time and have it take effect immediately.</summary>
        public bool HasCrossedReplacementThreshold(float currentThresholdPercent) => Wear <= currentThresholdPercent;

        public float EffectiveCu() => IsReplacing ? 0f : BaseCu * EffectivePerformance;

        /// <summary>Flat draw (unlike EffectiveCu, not scaled by stability) - 0 while being replaced, same rule as EffectiveCu().</summary>
        public float ActivePowerKw() => IsReplacing ? 0f : PowerKw;

        /// <summary>Restores Wear/EffectivePerformance to a previously-captured value. Used only by the save/load system (CONTRACTS.md §14) - IsReplacing/ReplacementElapsed already have public setters and don't need this.</summary>
        public void RestoreWearAndPerformance(float wear, float effectivePerformance)
        {
            Wear = wear;
            EffectivePerformance = effectivePerformance;
        }
    }
}

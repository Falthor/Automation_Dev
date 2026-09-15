using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Aggregates installed CPU/Memory components into Compute supply and Power demand
    /// (DATACENTER.md). Bays are physically universal: a fresh bay is Unassigned and empty, the
    /// player chooses CPU or Memory per bay, and only then does the standard Building/Inventory
    /// pooled input (cpu_mkI/Memory_MK1) auto-install into it - see ComponentInstance for the
    /// per-slot wear/stability/replacement rules.
    ///
    /// A freshly placed Data Center primes for 90s (1500 CU consumed from Building Compute, no
    /// production, no wear - GDD §2.3) before any of that applies; priming is a second continuous
    /// per-second CU draw alongside research's own (CALCUL.md), and pauses at zero CU exactly like
    /// research does. Once primed, its raw output splits across two axes (research/buildings) via
    /// a concentration-based yield curve, recoverable by Memory coverage (DATACENTER.md §"Baies
    /// universelles"), each crediting its own reserve - Research Compute and Building Compute
    /// (CALCUL.md).
    /// </summary>
    public sealed class DataCenterRuntime : BuildingRuntime
    {
        const int InitialBaySlots = 2;

        // Hard cap, deliberately above what the shipped bay effects reach today: starting at 2,
        // Extension I/II bring a Data Center to 6. The remaining headroom exists for Extension
        // III and guards the restore path meanwhile.
        const int MaxBaySlots = 8;

        /// <summary>
        /// How often each installed component draws its performance again.
        ///
        /// <b>Two seconds, down from five.</b> The panel draws that roll as a tick inside the band
        /// it can land in, and the movement of that tick is how a worn bay is read before any
        /// percentage is: at five seconds it sat still long enough to look frozen rather than
        /// unsteady. It changes nothing about what a component produces on average - the draw itself
        /// is untouched, only how often it happens, so the variance over a minute narrows and the
        /// mean does not move.
        ///
        /// Not to be confused with ReplacementDuration, which is also five seconds and stays there.
        /// </summary>
        const float StabilityInterval = 2f;
        const float ReplacementDuration = 5f;

        /// <summary>What a CPU bay takes. Public because the panel shows how many spares are in stock, and it has to be able to ask while the bay is empty - which is when that count matters most.</summary>
        public const string CpuItemId = "cpu_mkI";

        /// <summary>What a Memory bay takes - see <see cref="CpuItemId"/>.</summary>
        public const string MemoryItemId = "Memory_MK1";

        const float PrimingCostCu = 1500f;
        const float PrimingDurationSeconds = 90f;
        const float PrimingAbsorptionRatePerSecond = PrimingCostCu / PrimingDurationSeconds;

        public const float MinReplacementThresholdPercent = 5f;
        public const float MaxReplacementThresholdPercent = 60f;
        public const float DefaultReplacementThresholdPercent = 25f;

        /// <summary>How many CPUs one active Memory bay can assist, at the starting research tier. A target read from MemoryAssistCapacityTenths effects, the highest completed wins - see UnlockedMemoryAssistCapacityTenths.</summary>
        public const float DefaultMemoryAssistCapacity = 1.0f;

        /// <summary>Fraction of the yield lost to axis concentration that full Memory coverage claws back (DATACENTER.md). A balancing constant, not exposed to research in this pass.</summary>
        const float MemoryPenaltyRecovery = 0.75f;

        readonly DataCenterDefinition _definition;
        readonly ItemDatabase _itemDatabase;

        /// <summary>Where priming spends from, and where GetBuildingsAxisProduction() credits - the reserve Building Compute is (CALCUL.md).</summary>
        readonly ComputeSystem _buildingCompute;

        /// <summary>Where GetResearchAxisProduction() credits - the reserve Research Compute is (CALCUL.md).</summary>
        readonly ComputeSystem _researchCompute;

        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly PooledItemStock _input;
        readonly List<DataCenterBay> _bays;
        readonly System.Action<string> _onResearchCompleted;

        /// <summary>Owns every per-component lifetime draw for this Data Center's whole lifetime - one seeded stream, not re-seeded per install, so a fixed seed plus a fixed installation sequence always reproduces the same drawn lifetimes (DEVELOPMENT_RULES.md).</summary>
        readonly System.Random _lifetimeRandom;

        float _stabilityTimer;
        float _previousPowerDemand;
        float _primingAbsorbedCu;

        /// <summary>Whether this instance has granted the cores yet. Not saved: Grant is silent for an id already unlocked, so a reload simply asks again.</summary>
        bool _coresPowered;

        public IReadOnlyList<DataCenterBay> Bays => _bays;

        /// <summary>5..60, default 25 - adjustable at any time, for free (DATACENTER.md).</summary>
        public float CpuReplacementThresholdPercent { get; private set; } = DefaultReplacementThresholdPercent;

        /// <summary>5..60, default 25 - independent of the CPU setting.</summary>
        public float MemoryReplacementThresholdPercent { get; private set; } = DefaultReplacementThresholdPercent;

        /// <summary>Fraction of installed output aimed at the research axis, in [0,1]; the buildings axis gets the complement. Default 0.5 (50/50). Free and instantaneous to change (§7) - there is no armament axis yet.</summary>
        public float ResearchAxisShare { get; private set; } = 0.5f;

        /// <summary>How many CPUs one active Memory bay currently assists - a target from MemoryAssistCapacityTenths research effects, the highest completed wins, same shape as CoreRuntime.ActionRadiusCells.</summary>
        public float MemoryAssistCapacity { get; private set; } = DefaultMemoryAssistCapacity;

        /// <summary>True from placement until PrimingCostCu has been absorbed - no production, no wear while true (GDD §2.3).</summary>
        public bool IsPriming => _primingAbsorbedCu < PrimingCostCu;

        /// <summary>0..1 fraction of priming absorbed so far.</summary>
        public float PrimingProgress => UnityEngine.Mathf.Clamp01(_primingAbsorbedCu / PrimingCostCu);

        /// <summary>Best-case seconds remaining in priming at its fixed absorption rate - 0 once primed.</summary>
        public float GetPrimingSecondsRemaining() => IsPriming ? (PrimingCostCu - _primingAbsorbedCu) / PrimingAbsorptionRatePerSecond : 0f;

        public DataCenterRuntime(DataCenterDefinition definition, GridCoord cell, Direction facingRotation,
            ItemDatabase itemDatabase, ComputeSystem buildingCompute, ComputeSystem researchCompute, PowerSystem powerSystem, ResearchSystem researchSystem)
            : base(definition, cell, facingRotation)
        {
            _definition = definition;
            _itemDatabase = itemDatabase;
            _buildingCompute = buildingCompute;
            _researchCompute = researchCompute;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            _input = new PooledItemStock(definition.MaxStackPerItem);
            _lifetimeRandom = new System.Random(definition.ComponentLifetimeSeed);

            _bays = new List<DataCenterBay>();
            for (int i = 0; i < InitialBaySlots; i++) _bays.Add(new DataCenterBay());
            AddBaySlots(UnlockedBaySlots(researchSystem));
            MemoryAssistCapacity = UnlockedMemoryAssistCapacity(researchSystem);

            _onResearchCompleted = OnResearchCompleted;
            researchSystem.ResearchCompleted += _onResearchCompleted;
        }

        /// <summary>Each DataCenterBayPairs effect adds twice its value in fresh Unassigned bays, capped at MaxBaySlots - usable by the same install/wear/replacement code, no separate mechanism. Also re-reads MemoryAssistCapacityTenths, a target that only ever grows.</summary>
        void OnResearchCompleted(string researchId)
        {
            ResearchDefinition research = _researchSystem.Definition(researchId);
            AddBaySlots(BaySlotsOf(research));
            MemoryAssistCapacity = UnityEngine.Mathf.Max(MemoryAssistCapacity, MemoryAssistCapacityOf(research));
        }

        /// <summary>The bay slots every research already completed grants - what a Datacenter built after them starts with.</summary>
        static int UnlockedBaySlots(ResearchSystem research)
        {
            int slots = 0;
            foreach (string id in research.GetUnlockedIds()) slots += BaySlotsOf(research.Definition(id));
            return slots;
        }

        static float UnlockedMemoryAssistCapacity(ResearchSystem research)
        {
            float highest = DefaultMemoryAssistCapacity;
            foreach (string id in research.GetUnlockedIds()) highest = UnityEngine.Mathf.Max(highest, MemoryAssistCapacityOf(research.Definition(id)));
            return highest;
        }

        static int BaySlotsOf(ResearchDefinition research)
        {
            if (research == null) return 0;

            int slots = 0;
            IReadOnlyList<ResearchEffect> effects = research.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Kind == ResearchEffectKind.DataCenterBayPairs) slots += effects[i].Value * 2;
            }
            return slots;
        }

        static float MemoryAssistCapacityOf(ResearchDefinition research)
        {
            if (research == null) return DefaultMemoryAssistCapacity;

            float highest = DefaultMemoryAssistCapacity;
            IReadOnlyList<ResearchEffect> effects = research.Effects;
            for (int i = 0; i < effects.Count; i++)
            {
                if (effects[i].Kind == ResearchEffectKind.MemoryAssistCapacityTenths) highest = UnityEngine.Mathf.Max(highest, effects[i].Value / 10f);
            }
            return highest;
        }

        void AddBaySlots(int count)
        {
            for (int i = 0; i < count && _bays.Count < MaxBaySlots; i++) _bays.Add(new DataCenterBay());
        }

        /// <summary>Unsubscribes from ResearchSystem so a demolished Data Center doesn't keep growing slots forever.</summary>
        public override void OnUnregistered()
        {
            _researchSystem.ResearchCompleted -= _onResearchCompleted;
        }

        public void SetCpuReplacementThreshold(float percent) => CpuReplacementThresholdPercent = UnityEngine.Mathf.Clamp(percent, MinReplacementThresholdPercent, MaxReplacementThresholdPercent);
        public void SetMemoryReplacementThreshold(float percent) => MemoryReplacementThresholdPercent = UnityEngine.Mathf.Clamp(percent, MinReplacementThresholdPercent, MaxReplacementThresholdPercent);
        public void SetResearchAxisShare(float share) => ResearchAxisShare = UnityEngine.Mathf.Clamp01(share);

        float ThresholdFor(DataCenterBayType type) => type == DataCenterBayType.Memory ? MemoryReplacementThresholdPercent : CpuReplacementThresholdPercent;
        static string ItemIdFor(DataCenterBayType type) => type == DataCenterBayType.Memory ? MemoryItemId : CpuItemId;

        /// <summary>
        /// The one entry point the panel calls, for both a first assignment and a later
        /// reconfiguration (DATACENTER.md). An empty bay (Unassigned or already typed) takes the
        /// new type instantly. An occupied bay starts a 5s reconfiguration - the installed
        /// component stops producing immediately, reusing ComponentInstance.IsReplacing/
        /// ReplacementElapsed rather than a parallel timer. Asking for the type it already is (or
        /// already targets) while one is in flight cancels it, for free unless the component had
        /// already crossed its own replacement threshold on its own - that one was coming out
        /// regardless.
        /// </summary>
        public void SetBayAssignment(int bayIndex, DataCenterBayType type)
        {
            DataCenterBay bay = _bays[bayIndex];

            if (bay.Component == null)
            {
                bay.Assignment = type;
                bay.ReconfigureTarget = null;
                return;
            }

            if (type == bay.Assignment)
            {
                bay.ReconfigureTarget = null;
                if (!bay.Component.HasCrossedReplacementThreshold(ThresholdFor(bay.Assignment)))
                {
                    bay.Component.IsReplacing = false;
                    bay.Component.ReplacementElapsed = 0f;
                }
                return;
            }

            bay.ReconfigureTarget = type;
            if (!bay.Component.IsReplacing)
            {
                bay.Component.IsReplacing = true;
                bay.Component.ReplacementElapsed = 0f;
            }
        }

        // No fromDirection == ExitDirection guard here: that rule protects a building's real
        // physical output side (Foundry, Powerplant...), and the Data Center has none - its
        // ExitDirection is FacingRotation left over from the base class with nothing behind it.
        // Guarding on it anyway silently refused any belt feeding from the default-placement
        // North side, including both of its corners.
        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (System.Array.IndexOf(_definition.AcceptedItemIds, itemId) < 0) return false;
            return _input.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _input.Add(itemId, amount);
        public override int GetInputAmount(string itemId) => _input.GetAmount(itemId);

        /// <summary>
        /// Installed capacity (CU/s) <b>after each component's own stability roll</b> and before the
        /// axis yield - not what actually gets credited; see GetResearchAxisProduction/
        /// GetBuildingsAxisProduction for that. Not untouched by wear either: it sums EffectiveCu(),
        /// which is BaseCu times the StabilityInterval performance roll, and zero for a component
        /// being replaced or reconfigured. See <see cref="GetNominalComputeOutput"/> for the figure
        /// that really is untouched.
        /// </summary>
        public float GetTotalComputeOutput() => TotalComputeOutput();

        /// <summary>
        /// What the installed components would produce new, at full concentration: the sum of their
        /// <c>BaseCu</c>, untouched by wear, by the stability roll, by a replacement/reconfiguration
        /// in progress or by the axis split.
        ///
        /// Exists for the panel to show the chain the player cannot otherwise see - nominal, then
        /// the factor the bays' condition and the axis split apply to it, then what is actually
        /// produced. A component being replaced is counted: its bay is occupied and its capacity is
        /// installed, so leaving it out would make the factor jump to 1 at the moment production
        /// drops to nothing.
        /// </summary>
        public float GetNominalComputeOutput()
        {
            float total = 0f;
            foreach (DataCenterBay bay in _bays) if (bay.Component != null) total += bay.Component.BaseCu;
            return total;
        }
        public float GetTotalPowerDemand() => TotalPowerDemand();

        /// <summary>Bays assigned Cpu, occupied, and not currently replacing/reconfiguring - the count DATACENTER.md's "at least one active CPU" gate and Memory coverage both read.</summary>
        public int ActiveCpuCount => CountActive(DataCenterBayType.Cpu);

        /// <summary>Bays assigned Memory, occupied, and not currently replacing/reconfiguring.</summary>
        public int ActiveMemoryCount => CountActive(DataCenterBayType.Memory);

        int CountActive(DataCenterBayType type)
        {
            int count = 0;
            foreach (DataCenterBay bay in _bays)
            {
                if (bay.Assignment == type && bay.Component != null && !bay.Component.IsReplacing) count++;
            }
            return count;
        }

        /// <summary>
        /// Fraction, in [0,1], of the installed CPUs an active Memory bay's assist capacity can
        /// cover - min(1, memory·capacity/cpu). 0 with no active CPU, never a division by zero
        /// (DATACENTER.md).
        /// </summary>
        public float GetMemoryCoverage()
        {
            int cpu = ActiveCpuCount;
            if (cpu == 0) return 0f;
            return UnityEngine.Mathf.Min(1f, ActiveMemoryCount * MemoryAssistCapacity / cpu);
        }

        /// <summary>Σ(share²) of the two axes - DATACENTER.md. 1.0 at either extreme (100/0), lowest at an even split.</summary>
        public float GetConcentration()
        {
            float research = ResearchAxisShare;
            float buildings = 1f - research;
            return research * research + buildings * buildings;
        }

        /// <summary>floor + (1-floor) * concentration - the fraction of installed capacity produced before Memory coverage claws any of it back. floor is DataCenterDefinition.AxisYieldFloor (a parameter, not a buried constant - see there).</summary>
        public float GetYield() => _definition.AxisYieldFloor + (1f - _definition.AxisYieldFloor) * GetConcentration();

        /// <summary>
        /// GetYield() plus the share of what it lost that Memory coverage recovers -
        /// (1-GetYield())·coverage·MemoryPenaltyRecovery (DATACENTER.md). Full coverage never
        /// reaches 1: a 50/50 split at 100% coverage lands at 0.90, not 1.00.
        /// </summary>
        public float GetFinalYield()
        {
            float baseYield = GetYield();
            return baseYield + (1f - baseYield) * GetMemoryCoverage() * MemoryPenaltyRecovery;
        }

        /// <summary>Actual CU/s the research axis currently produces - installed capacity * final yield * its own share. 0 with no active CPU, whatever Memory is installed (DATACENTER.md).</summary>
        public float GetResearchAxisProduction() => ActiveCpuCount > 0 ? TotalComputeOutput() * GetFinalYield() * ResearchAxisShare : 0f;

        /// <summary>Actual CU/s the buildings axis currently produces - see GetResearchAxisProduction.</summary>
        public float GetBuildingsAxisProduction() => ActiveCpuCount > 0 ? TotalComputeOutput() * GetFinalYield() * (1f - ResearchAxisShare) : 0f;

        public override void Tick(float deltaTime)
        {
            InstallInto();

            if (IsPriming)
            {
                float wanted = UnityEngine.Mathf.Min(PrimingCostCu - _primingAbsorbedCu, PrimingAbsorptionRatePerSecond * deltaTime);
                _primingAbsorbedCu += _buildingCompute.SpendUpTo(wanted);
                _previousPowerDemand = TotalPowerDemand();
                return; // no production, no wear while priming (GDD §2.3)
            }

            if (!_coresPowered)
            {
                IReadOnlyList<ResearchDefinition> cores = _definition.PoweredCores;
                for (int i = 0; i < cores.Count; i++)
                {
                    if (cores[i] != null) _researchSystem.Grant(cores[i].Id);
                }
                _coresPowered = true;
            }

            // delta already carries the Power gate (0 whenever unpowered, from last frame's
            // reported demand - same one-frame lag as every other Compute/Power report),
            // freezing the wear/stability timers below while unpowered.
            float performance = ComputeEffectivePerformance(_previousPowerDemand, powerActive: true, _powerSystem);
            float effectiveDelta = deltaTime * performance;

            DecayWear(effectiveDelta);
            ProcessBays(effectiveDelta);

            _stabilityTimer += effectiveDelta;
            if (_stabilityTimer >= StabilityInterval)
            {
                _stabilityTimer = 0f;
                RecalculateStability();
            }

            // Gated on this building's own draw rather than on a global answer (an instantaneous
            // rate, not something effectiveDelta=0 would zero out on its own) - shutdown must
            // silence it immediately, not just freeze its progression. `performance` is what the
            // power gate above returned for the datacenter group, so a Data Center the player put
            // first keeps producing CU while the rest of the base waits - which is the whole point
            // of the priority order. It is a CU/s rate, so what lands in each reserve is that rate
            // times this tick's own duration - through the same public per-axis methods the UI
            // reads, so crediting can never duplicate or disagree with the yield calculation.
            if (performance > 0f)
            {
                _researchCompute.Grant(GetResearchAxisProduction() * deltaTime);
                _buildingCompute.Grant(GetBuildingsAxisProduction() * deltaTime);
            }
            _previousPowerDemand = TotalPowerDemand();
        }

        void InstallInto()
        {
            foreach (DataCenterBay bay in _bays)
            {
                if (bay.Assignment == DataCenterBayType.Unassigned) continue;
                if (bay.Component != null) continue;

                string itemId = ItemIdFor(bay.Assignment);
                if (_input.GetAmount(itemId) <= 0) continue;

                _input.Take(itemId, 1);
                bay.Component = new ComponentInstance(itemId, _itemDatabase, _lifetimeRandom);
            }
        }

        void DecayWear(float deltaTime)
        {
            foreach (DataCenterBay bay in _bays)
            {
                // Frozen while replacing/reconfiguring (DATACENTER.md §13): the bay already
                // produces nothing, so the part stops ageing too - it no longer keeps decaying
                // toward 0% wear during the five seconds it sits out, which used to be able to
                // race past a very low replacement threshold before the swap ever completed.
                if (bay.Component == null || bay.Component.IsReplacing) continue;
                bay.Component.DecayWear(deltaTime);
            }
        }

        void RecalculateStability()
        {
            foreach (DataCenterBay bay in _bays) bay.Component?.RecalculatePerformance();
        }

        /// <summary>
        /// Starts/advances/resolves replacement and reconfiguration for every bay in one pass. A
        /// bay enters replacement the instant its component's wear crosses the CURRENT threshold
        /// for its assigned type; hard removal at 0% wear takes priority over completing the timer.
        /// Reconfiguration (DataCenterBay.ReconfigureTarget set by SetBayAssignment) reuses the same
        /// timer; on completion the bay's Assignment flips to the target instead of looking for a
        /// spare of the same type.
        /// </summary>
        void ProcessBays(float deltaTime)
        {
            foreach (DataCenterBay bay in _bays)
            {
                ComponentInstance component = bay.Component;
                if (component == null) continue;

                if (!component.IsReplacing)
                {
                    if (component.HasCrossedReplacementThreshold(ThresholdFor(bay.Assignment)))
                    {
                        component.IsReplacing = true;
                        component.ReplacementElapsed = 0f;
                    }
                    continue;
                }

                if (component.Wear <= 0f)
                {
                    CompleteBay(bay);
                    continue;
                }

                component.ReplacementElapsed += deltaTime;
                if (component.ReplacementElapsed < ReplacementDuration) continue;

                CompleteBay(bay);
            }
        }

        static void CompleteBay(DataCenterBay bay)
        {
            if (bay.ReconfigureTarget != null)
            {
                bay.Assignment = bay.ReconfigureTarget.Value;
                bay.ReconfigureTarget = null;
            }
            bay.Component = null; // discarded either way - InstallInto picks a fresh one up later
        }

        float TotalComputeOutput()
        {
            float total = 0f;
            foreach (DataCenterBay bay in _bays) if (bay.Component != null) total += bay.Component.EffectiveCu();
            return total;
        }

        float TotalPowerDemand()
        {
            float total = 0f;
            foreach (DataCenterBay bay in _bays) if (bay.Component != null) total += bay.Component.ActivePowerKw();
            return total;
        }

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["stabilityTimer"] = _stabilityTimer,
                ["previousPowerDemand"] = _previousPowerDemand,
                ["primingAbsorbedCu"] = _primingAbsorbedCu,
                ["cpuReplacementThresholdPercent"] = CpuReplacementThresholdPercent,
                ["memoryReplacementThresholdPercent"] = MemoryReplacementThresholdPercent,
                ["researchAxisShare"] = ResearchAxisShare,
                ["input"] = JObject.FromObject(_input.Contents),
                ["bays"] = CaptureBays()
            };
        }

        JArray CaptureBays()
        {
            var array = new JArray();
            foreach (DataCenterBay bay in _bays)
            {
                var entry = new JObject { ["assignment"] = (int)bay.Assignment };
                if (bay.ReconfigureTarget != null) entry["reconfigureTarget"] = (int)bay.ReconfigureTarget.Value;

                ComponentInstance component = bay.Component;
                if (component != null)
                {
                    entry["component"] = new JObject
                    {
                        ["itemId"] = component.ItemId,
                        ["wear"] = component.Wear,
                        ["effectivePerformance"] = component.EffectivePerformance,
                        ["isReplacing"] = component.IsReplacing,
                        ["replacementElapsed"] = component.ReplacementElapsed,
                        ["nominalLifetimeSeconds"] = component.NominalLifetimeSeconds,
                        ["baseLossPerSecond"] = component.BaseLossPerSecond
                    };
                }
                array.Add(entry);
            }
            return array;
        }

        /// <summary>
        /// Every key is read with a fallback (SAUVEGARDE.md): a
        /// blob missing a key falls back to a reasonable default instead of throwing, so this
        /// shape can keep growing without breaking an earlier save of the same Version.
        /// </summary>
        public override void RestoreState(JObject state)
        {
            _stabilityTimer = state.Value<float?>("stabilityTimer") ?? 0f;
            _previousPowerDemand = state.Value<float?>("previousPowerDemand") ?? 0f;
            // Absent means this blob predates priming - assume already primed rather than
            // freezing an established playthrough's production on load.
            _primingAbsorbedCu = state.Value<float?>("primingAbsorbedCu") ?? PrimingCostCu;
            CpuReplacementThresholdPercent = state.Value<float?>("cpuReplacementThresholdPercent") ?? DefaultReplacementThresholdPercent;
            MemoryReplacementThresholdPercent = state.Value<float?>("memoryReplacementThresholdPercent") ?? DefaultReplacementThresholdPercent;
            ResearchAxisShare = state.Value<float?>("researchAxisShare") ?? 0.5f;
            _input.RestoreContents(state["input"]?.ToObject<Dictionary<string, int>>());

            _bays.Clear();
            if (state["bays"] is JArray bays) RestoreBays(bays);
            else RestoreLegacySlots(state["cpuSlots"] as JArray, state["memorySlots"] as JArray);
        }

        void RestoreBays(JArray saved)
        {
            foreach (JToken entry in saved)
            {
                var bay = new DataCenterBay
                {
                    Assignment = (DataCenterBayType)(entry.Value<int?>("assignment") ?? 0)
                };

                int? reconfigureTarget = entry.Value<int?>("reconfigureTarget");
                if (reconfigureTarget != null) bay.ReconfigureTarget = (DataCenterBayType)reconfigureTarget.Value;

                if (entry["component"] is JObject component) bay.Component = RestoreComponent(component);
                _bays.Add(bay);
            }
        }

        /// <summary>Pre-universal-bays saves (SAUVEGARDE.md §2 - a structural key change inside one building's own blob, not a Version bump): cpuSlots/memorySlots become Cpu/Memory bays one-for-one. Every research tier gave the same total count either way (1+1, then +2 per pair), so no extra bay needs inventing here.</summary>
        void RestoreLegacySlots(JArray cpuSlots, JArray memorySlots)
        {
            RestoreLegacySlotList(cpuSlots, DataCenterBayType.Cpu);
            RestoreLegacySlotList(memorySlots, DataCenterBayType.Memory);
        }

        void RestoreLegacySlotList(JArray saved, DataCenterBayType type)
        {
            if (saved == null) return;

            foreach (JToken entry in saved)
            {
                var bay = new DataCenterBay { Assignment = type };
                if (entry.Type == JTokenType.Object) bay.Component = RestoreComponent((JObject)entry);
                _bays.Add(bay);
            }
        }

        ComponentInstance RestoreComponent(JObject entry)
        {
            string itemId = entry.Value<string>("itemId");
            float nominalLifetime = entry.Value<float?>("nominalLifetimeSeconds") ?? (_itemDatabase.Get(itemId)?.NominalLifetimeSeconds ?? 120f);
            float baseLoss = entry.Value<float?>("baseLossPerSecond") ?? ComponentInstance.DeriveBaseLossPerSecond(nominalLifetime);

            var component = new ComponentInstance(itemId, _itemDatabase, nominalLifetime, baseLoss);
            component.RestoreWearAndPerformance(entry.Value<float?>("wear") ?? 100f, entry.Value<float?>("effectivePerformance") ?? 1f);
            component.IsReplacing = entry.Value<bool?>("isReplacing") ?? false;
            component.ReplacementElapsed = entry.Value<float?>("replacementElapsed") ?? 0f;
            return component;
        }
    }
}

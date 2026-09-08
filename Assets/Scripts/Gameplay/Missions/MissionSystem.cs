using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Sectors;
using Game.Grid;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Gameplay.Missions
{
    /// <summary>
    /// The expedition process: robots, missions in flight, and what a mission does when it lands.
    ///
    /// <b>No interface, on purpose.</b> A mission is launched by a call, advances with the game clock,
    /// resolves and produces a report - all of it testable with no screen. The zoomed-out map and the
    /// launch screen come afterwards and must not get to shape the model.
    ///
    /// <b>Crew is carried at 1 rather than absent.</b> Units do not exist yet, so every mission has one
    /// executant and §5.4's brake on over-committing has nothing to act on. Keeping the parameter costs
    /// almost nothing now and stops the code organising itself around "a mission has one robot", which
    /// is what would have to be undone when squads arrive.
    ///
    /// <b>Launching never costs CU</b> (§1). That is what keeps the zero-CU floor an exit rather than
    /// a dead end, and it is a rule rather than a balance value - there is no setting for it here.
    /// </summary>
    public sealed class MissionSystem
    {
        // Distinct salts so a mission's outcome and its reward are independent draws rather than two
        // views of one number.
        const uint OutcomeSalt = 0x7FEB352D;
        const uint FindingSalt = 0x846CA68B;

        readonly MissionSettings _settings;
        readonly SectorGrid _grid;
        readonly DiscoveryRuntime _discovery;
        readonly SectorCatalog _catalog;
        readonly ComputeSystem _compute;

        /// <summary>The two reconnaissance bands. Optional: null enforces no band at all, which is what a headless test uninterested in geometry wants.</summary>
        readonly SectorMissionRange _range;

        readonly int _seed;

        readonly List<MissionRuntime> _inFlight = new List<MissionRuntime>();
        readonly List<MissionRuntime> _reports = new List<MissionRuntime>();

        /// <summary>Charges left on each robot. A robot is an index, not an object: it has no state beyond this.</summary>
        readonly List<int> _robotCharges = new List<int>();

        /// <summary>Sectors whose point of interest has been exploited. Finite by §8 - a recovered site is not offered again.</summary>
        readonly HashSet<int> _consumedSites = new HashSet<int>();

        int _nextMissionId = 1;
        int _paidReconnaissances;
        int _paidRecoveries;
        float _regeneratingCooldownLeft;

        public MissionSystem(MissionSettings settings, SectorGrid grid, DiscoveryRuntime discovery,
            SectorCatalog catalog, ComputeSystem compute, SectorMissionRange range, int seed)
        {
            _settings = settings;
            _grid = grid;
            _discovery = discovery;
            _catalog = catalog;
            _compute = compute;
            _range = range;
            _seed = seed;
        }

        /// <summary>
        /// Turns a reported sector's derived contents into real deposits. Set after construction
        /// rather than injected, because it needs the ore definitions that world generation owns and
        /// this system is built before them.
        ///
        /// Optional: null means a mission reveals the map and materialises nothing, which is what a
        /// headless test of the process itself wants.
        /// </summary>
        public SectorMaterialisation Materialisation { get; set; }

        /// <summary>Whether the robots have arrived. Once true it never goes back: a robot that has appeared has appeared.</summary>
        public bool RobotsHaveAppeared { get; private set; }

        public int ExplorerRobotCount => _robotCharges.Count;

        /// <summary>Missions currently out. Never more than the configured maximum.</summary>
        public IReadOnlyList<MissionRuntime> InFlight => _inFlight;

        /// <summary>How many may be out at once. Read by the Top Bar's permanent counter, which §5.3 requires so the player never has to open the map to know.</summary>
        public int MaxConcurrentMissions => _settings.MaxConcurrentMissions;

        /// <summary>Reports waiting to be read. A mission sits here until the caller closes it.</summary>
        public IReadOnlyList<MissionRuntime> Reports => _reports;

        public int ConsumedSiteCount => _consumedSites.Count;

        /// <summary>Charges left on one robot, or 0 for an index that is not a robot.</summary>
        public int ChargesOf(int robotIndex)
            => robotIndex >= 0 && robotIndex < _robotCharges.Count ? _robotCharges[robotIndex] : 0;

        /// <summary>Total charges left across every robot. What tells a caller the fleet is spent.</summary>
        public int TotalChargesLeft
        {
            get
            {
                int total = 0;
                foreach (int charges in _robotCharges) total += charges;
                return total;
            }
        }

        // ---- The clock ----

        /// <summary>
        /// Advances every mission, and brings the robots out when the reserve has fallen far enough.
        ///
        /// The reserve and its cap are passed in rather than read: the threshold is a fraction of the
        /// cap, and a test has to be able to move the cap to prove the threshold follows it.
        /// </summary>
        public void Tick(float deltaSeconds, float reserve, float reserveCap)
        {
            if (_settings == null) return;

            AppearIfReserveHasFallen(reserve, reserveCap);

            if (_regeneratingCooldownLeft > 0f) _regeneratingCooldownLeft -= deltaSeconds;

            for (int i = _inFlight.Count - 1; i >= 0; i--)
            {
                MissionRuntime mission = _inFlight[i];
                MissionState before = mission.State;
                MissionState after = mission.Advance(deltaSeconds);

                if (before != MissionState.Resolution && after == MissionState.Resolution) Resolve(mission);

                if (after == MissionState.Rapport)
                {
                    Deliver(mission);
                    _inFlight.RemoveAt(i);
                    _reports.Add(mission);
                }
            }
        }

        void AppearIfReserveHasFallen(float reserve, float reserveCap)
        {
            if (RobotsHaveAppeared) return;
            if (reserve > _settings.RobotThresholdCu(reserveCap)) return;

            RobotsHaveAppeared = true;
            for (int i = 0; i < _settings.ExplorerRobotCount; i++) _robotCharges.Add(_settings.MissionsPerRobot);
        }

        // ---- Launching ----

        /// <summary>Why a launch was refused. Named rather than boolean so a caller can say what is wrong instead of only greying a button out.</summary>
        public enum LaunchRefusal
        {
            None,
            RobotsHaveNotArrived,
            AllSlotsBusy,
            NoRobotAvailable,
            NotASector,
            AlreadyRecovered,

            /// <summary>The target is in the other reconnaissance's band - inside the Core's reach, or across the exploration threshold.</summary>
            WrongBand,

            /// <summary>A reconnaissance aimed at ground already seen. It would reveal a disc that is already revealed.</summary>
            AlreadyReconnoitred,

            /// <summary>A recovery aimed at a sector no robot has reported on. The player cannot exploit what they have not found.</summary>
            NotYetReconnoitred,

            /// <summary>A recovery aimed at a sector whose derivation put no point of interest in it.</summary>
            NothingToRecover
        }

        /// <summary>
        /// Whether a mission could be launched right now, and why not when it cannot.
        ///
        /// <b>The band is checked here, and that is the whole point of this method.</b>
        /// `SectorMissionRange` was built, tested and documented, and for one brick nothing called it:
        /// both sides were right and the seam between them did not exist, so a prospection could be
        /// aimed at ground already revealed or across the exploration threshold. No unit test could
        /// see it - each half passed on its own. The test that catches this kind of defect always
        /// starts from the real entry point, which is this method and not the predicate it calls.
        ///
        /// <b>There is no adjacency rule</b>, deliberately: the specification recommended one and the
        /// two bands replaced it (SPEC_EXPEDITIONS.md §10). Requiring a target to touch known ground
        /// as well as sit in the right band would constrain the same thing twice and turn exploration
        /// into a concentric crawl - the opposite of what six secondary Core sites at the threshold
        /// assume. Distance already costs travel time.
        ///
        /// The Core's current radius is passed in rather than remembered, exactly as
        /// `SectorMissionRange` takes it: a radius held here would be a second copy that goes stale
        /// the moment research extends the real one.
        /// </summary>
        public LaunchRefusal CanLaunch(MissionKind kind, int targetSector, float coreRadiusCells)
        {
            if (!RobotsHaveAppeared) return LaunchRefusal.RobotsHaveNotArrived;
            if (_inFlight.Count >= _settings.MaxConcurrentMissions) return LaunchRefusal.AllSlotsBusy;
            if (FreeRobot() < 0) return LaunchRefusal.NoRobotAvailable;
            if (_grid == null || !_grid.ContainsIndex(targetSector)) return LaunchRefusal.NotASector;

            // A recovery has no band: it exploits a point of interest inside ground a reconnaissance
            // already opened, so its rules are the mirror of a reconnaissance's.
            if (kind == MissionKind.Recuperation)
            {
                if (_consumedSites.Contains(targetSector)) return LaunchRefusal.AlreadyRecovered;
                if (_grid.IsWhollyUnknown(targetSector, _discovery)) return LaunchRefusal.NotYetReconnoitred;

                return _catalog != null && _catalog.ContentsOf(targetSector).Feature == SectorFeature.None
                    ? LaunchRefusal.NothingToRecover
                    : LaunchRefusal.None;
            }

            if (_range == null) return LaunchRefusal.None;

            switch (_range.EligibilityOf(kind, _grid, _discovery, CoreCentre, coreRadiusCells, targetSector))
            {
                case SectorEligibility.Eligible: return LaunchRefusal.None;
                case SectorEligibility.TooClose:
                case SectorEligibility.TooFar: return LaunchRefusal.WrongBand;
                case SectorEligibility.AlreadyKnown: return LaunchRefusal.AlreadyReconnoitred;
                default: return LaunchRefusal.NotASector;
            }
        }

        Vector2 CoreCentre => _catalog?.CoreCenterCells ?? Vector2.zero;

        /// <summary>
        /// Sends a mission, or answers why it could not go.
        ///
        /// The outcome and the reward are drawn here and carried by the mission - see MissionRuntime's
        /// summary for why that rather than drawing on arrival. The robot's charge is spent here too:
        /// a launched mission cannot be taken back.
        /// </summary>
        public LaunchRefusal TryLaunch(MissionKind kind, int targetSector, float coreRadiusCells,
            out MissionRuntime mission, int crew = 1)
        {
            mission = null;

            LaunchRefusal refusal = CanLaunch(kind, targetSector, coreRadiusCells);
            if (refusal != LaunchRefusal.None) return refusal;

            int robot = FreeRobot();
            int id = _nextMissionId++;

            MissionOutcome outcome = DrawOutcome(kind, targetSector, id);
            float reward = DrawReward(kind, outcome);

            mission = new MissionRuntime(id, kind, targetSector, DurationOf(kind, targetSector, crew),
                outcome, reward, robot, crew);

            _robotCharges[robot]--;

            // Committed at launch, not at landing: the budget a mission was promised must not move
            // because another one landed first.
            if (reward > 0f) CommitReward(kind, outcome);

            _inFlight.Add(mission);
            return LaunchRefusal.None;
        }

        int FreeRobot()
        {
            for (int i = 0; i < _robotCharges.Count; i++)
            {
                if (_robotCharges[i] > 0) return i;
            }

            return -1;
        }

        /// <summary>
        /// How long the round trip takes: the kind's own duration, plus the distance to the target.
        ///
        /// Distance is what separates a prospection from a far exploration in the playing of it, beyond
        /// what they pay (§10, settled). Crew lengthens it too - a bigger squad moves at the pace of
        /// its slowest member - which is inert while crew is 1 but is the shape §5.4 needs.
        /// </summary>
        public float DurationOf(MissionKind kind, int targetSector, int crew = 1)
        {
            float baseSeconds =
                kind == MissionKind.Prospection ? _settings.ProspectionSeconds :
                kind == MissionKind.ExplorationLointaine ? _settings.ExplorationSeconds :
                _settings.RecoverySeconds;

            float distance = 0f;
            if (_grid != null && _grid.ContainsIndex(targetSector) && _catalog != null)
            {
                distance = Vector2.Distance(_grid.CenterCells(targetSector), _catalog.CoreCenterCells);
            }

            return baseSeconds + distance * _settings.SecondsPerDistanceCell + (crew - 1) * baseSeconds * 0.1f;
        }

        // ---- The draw ----

        /// <summary>
        /// <b>With a robot the map never fails</b> (§7.1). Only the harvest can, and only a recovery
        /// harvests - so a reconnaissance can come back empty but never blind.
        /// </summary>
        MissionOutcome DrawOutcome(MissionKind kind, int targetSector, int missionId)
        {
            if (kind == MissionKind.Recuperation)
            {
                // One in four recoveries comes back without the goods. The site is not consumed.
                return DeterministicHash.Mix(_seed, missionId, OutcomeSalt) % 4u == 0u
                    ? MissionOutcome.RecolteManquee
                    : MissionOutcome.Reussite;
            }

            // A prospection either finds something or answers "nothing here" - both reveal the disc.
            SectorFeature feature = _catalog != null ? _catalog.ContentsOf(targetSector).Feature : SectorFeature.None;
            return feature == SectorFeature.None ? MissionOutcome.DecouverteVide : MissionOutcome.Reussite;
        }

        /// <summary>
        /// What the mission will pay. Zero once the introduction's budget is spent, except for the
        /// regenerating payout - which is what keeps a player at zero CU from being stuck.
        /// </summary>
        float DrawReward(MissionKind kind, MissionOutcome outcome)
        {
            if (kind == MissionKind.Recuperation)
            {
                if (outcome == MissionOutcome.RecolteManquee) return 0f;
                if (_paidRecoveries < _settings.PaidRecoveries) return _settings.RecoveryReward;
            }
            else if (_paidReconnaissances < _settings.PaidReconnaissances)
            {
                return _settings.ReconnaissanceReward;
            }

            return _regeneratingCooldownLeft <= 0f ? _settings.RegeneratingReward : 0f;
        }

        void CommitReward(MissionKind kind, MissionOutcome outcome)
        {
            if (kind == MissionKind.Recuperation && outcome != MissionOutcome.RecolteManquee
                && _paidRecoveries < _settings.PaidRecoveries)
            {
                _paidRecoveries++;
                return;
            }

            if (kind != MissionKind.Recuperation && _paidReconnaissances < _settings.PaidReconnaissances)
            {
                _paidReconnaissances++;
                return;
            }

            _regeneratingCooldownLeft = _settings.RegeneratingCooldownSeconds;
        }

        // ---- Landing ----

        /// <summary>
        /// The robot reaches its target and gathers what it came for. <b>Nothing reaches the Core
        /// here</b>, and that is the point.
        ///
        /// The Core cannot communicate beyond its own action radius - it is blind and mute out there,
        /// which is the whole reason expeditions exist. A robot in the field therefore has nobody to
        /// transmit to: it carries the data home. So this state changes nothing the player can see,
        /// and the map moves only when the robot docks.
        ///
        /// It is kept as a state because §6 names it, and because it is the moment the outcome stops
        /// being revocable - not because anything observable happens.
        /// </summary>
        void Resolve(MissionRuntime mission)
        {
        }

        /// <summary>
        /// The robot is home and back inside the radius, which is the first moment the Core can hear
        /// it. Everything the mission brought back lands here at once: the map, the site, the CU.
        ///
        /// The revelation goes through SectorGrid, which already owns the inscribed-disc shape - a
        /// mission asks for it, it does not re-implement it.
        /// </summary>
        void Deliver(MissionRuntime mission)
        {
            if (mission.Kind == MissionKind.Recuperation)
            {
                if (mission.Outcome != MissionOutcome.RecolteManquee) _consumedSites.Add(mission.TargetSector);
            }
            else
            {
                _grid?.RevealInscribedDisc(mission.TargetSector, _discovery);

                // Straight after the revelation, and from the same place: a sector's contents become
                // real the moment a robot reports on it. Placed content wins - see
                // SectorMaterialisation for the rule and the ordering constraint behind it.
                Materialisation?.Materialise(mission.TargetSector);
            }

            // The robot's charge was spent at launch, so there is nothing to return here - a robot
            // that went out has used its charge whatever came of the trip.
            if (mission.RewardCu > 0f) _compute?.Grant(mission.RewardCu);
        }

        /// <summary>Marks a report read and drops it. Reports are not saved: an unread one is delivered again at load, which is better than losing it.</summary>
        public void CloseReport(MissionRuntime mission)
        {
            if (mission == null) return;

            mission.Close();
            _reports.Remove(mission);
        }

        // ---- Save / Restore (CONTRACTS.md §14) ----

        /// <summary>
        /// Missions in flight with their clock and their drawn outcome, the charges left on each robot,
        /// and the sites already recovered. Everything else is derived.
        /// </summary>
        public JObject CaptureState()
        {
            var missions = new JArray();
            foreach (MissionRuntime mission in _inFlight)
            {
                missions.Add(new JObject
                {
                    ["id"] = mission.Id,
                    ["kind"] = (int)mission.Kind,
                    ["sector"] = mission.TargetSector,
                    ["total"] = mission.TotalSeconds,
                    ["elapsed"] = mission.ElapsedSeconds,
                    ["state"] = (int)mission.State,
                    ["outcome"] = (int)mission.Outcome,
                    ["reward"] = mission.RewardCu,
                    ["robot"] = mission.RobotIndex,
                    ["crew"] = mission.Crew
                });
            }

            var charges = new JArray();
            foreach (int charge in _robotCharges) charges.Add(charge);

            var consumed = new JArray();
            foreach (int sector in _consumedSites) consumed.Add(sector);

            return new JObject
            {
                ["appeared"] = RobotsHaveAppeared,
                ["nextId"] = _nextMissionId,
                ["charges"] = charges,
                ["missions"] = missions,
                ["consumed"] = consumed,
                ["paidReconnaissances"] = _paidReconnaissances,
                ["paidRecoveries"] = _paidRecoveries,
                ["regeneratingCooldown"] = _regeneratingCooldownLeft
            };
        }

        /// <summary>Tolerant like every other Restore: a null or an absent key restores as a game where the robots have not arrived, rather than throwing.</summary>
        public void RestoreState(JObject state)
        {
            _inFlight.Clear();
            _reports.Clear();
            _robotCharges.Clear();
            _consumedSites.Clear();

            RobotsHaveAppeared = false;
            _nextMissionId = 1;
            _paidReconnaissances = 0;
            _paidRecoveries = 0;
            _regeneratingCooldownLeft = 0f;

            if (state == null) return;

            RobotsHaveAppeared = state.Value<bool?>("appeared") ?? false;
            _nextMissionId = state.Value<int?>("nextId") ?? 1;
            _paidReconnaissances = state.Value<int?>("paidReconnaissances") ?? 0;
            _paidRecoveries = state.Value<int?>("paidRecoveries") ?? 0;
            _regeneratingCooldownLeft = state.Value<float?>("regeneratingCooldown") ?? 0f;

            if (state["charges"] is JArray charges)
            {
                foreach (JToken charge in charges) _robotCharges.Add(charge.Value<int?>() ?? 0);
            }

            if (state["consumed"] is JArray consumed)
            {
                foreach (JToken sector in consumed)
                {
                    int? index = sector.Value<int?>();
                    if (index.HasValue) _consumedSites.Add(index.Value);
                }
            }

            if (state["missions"] is JArray missions)
            {
                foreach (JToken token in missions)
                {
                    if (!(token is JObject json)) continue;

                    var mission = new MissionRuntime(
                        json.Value<int?>("id") ?? 0,
                        (MissionKind)(json.Value<int?>("kind") ?? 0),
                        json.Value<int?>("sector") ?? 0,
                        json.Value<float?>("total") ?? 1f,
                        (MissionOutcome)(json.Value<int?>("outcome") ?? 0),
                        json.Value<float?>("reward") ?? 0f,
                        json.Value<int?>("robot") ?? 0,
                        json.Value<int?>("crew") ?? 1);

                    mission.RestoreProgress(
                        json.Value<float?>("elapsed") ?? 0f,
                        (MissionState)(json.Value<int?>("state") ?? 0));

                    _inFlight.Add(mission);
                }
            }
        }
    }
}

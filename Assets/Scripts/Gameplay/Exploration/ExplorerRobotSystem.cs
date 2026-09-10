using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Sectors;
using Game.Gameplay.Wrecks;
using Game.Grid;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// Free exploration: robots the player sends out to wander, which uncover ground as they go and
    /// come back when told to.
    ///
    /// <b>The only thing in the game that goes anywhere.</b> It opens the ground, it is what turns
    /// derived deposits into real ones, and the map screen is about it. Exploration is an action the
    /// player starts and interrupts rather than a trip that is aimed and resolves - so there is no
    /// launch, no duration and no report - and a robot is visible the whole time it is working.
    ///
    /// <b>There is no destination, and that is the design rather than a gap.</b> A destination plus
    /// straight-line travel would uncover a radius: three sorties would draw three spokes out of the
    /// Core and the map would fill in as a star. So a robot carries a <i>heading</i> that changes
    /// continuously, and three things bend it - see <see cref="Steer"/>.
    /// </summary>
    public sealed class ExplorerRobotSystem
    {
        /// <summary>Distinct salts so the drift, the departure bearing and a card's threshold are independent draws rather than three views of one number.</summary>
        const uint DriftSalt = 0x1B873593;
        const uint DepartureSalt = 0xCC9E2D51;
        const uint CardSalt = 0x85EBCA6B;

        /// <summary>
        /// Consecutive fruitless reveals before the panel calls it "known ground". A display
        /// hysteresis, not a balance value, which is why it is a constant here rather than a setting:
        /// one barren reveal happens constantly at the edge of a trail, and reporting on it would make
        /// the line flicker while the robot is plainly still working.
        /// </summary>
        const int BarrenRevealsBeforeKnownGround = 4;

        /// <summary>1/phi. Consecutive sorties step by this fraction of a turn - about 137.5 degrees - which is what stops two of them ever leaving on nearly the same bearing.</summary>
        const float GoldenRatioConjugate = 0.6180339887f;

        /// <summary>How far the robot moves between two reveal discs. One cell against a reveal radius of six means they overlap heavily, so the trail is a band rather than a row of beads instead of a row of them.</summary>
        const float RevealStepCells = 1f;

        /// <summary>How close a click has to land to catch a robot, in cells. A little wider than the sprite, because the target is moving.</summary>
        const float PickRadiusCells = 1.2f;

        /// <summary>
        /// The pattern each probe samples, in units of <see cref="ExplorerRobotSettings.ProbeSpreadCells"/>:
        /// its own centre plus four corners. Five lookups a side is enough to read a frontier without
        /// the single-cell flicker that a bare centre sample gives on the ragged edge of a trail the
        /// robot has just written itself.
        /// </summary>
        static readonly Vector2[] ProbePattern =
        {
            new Vector2(0f, 0f),
            new Vector2(-0.7f, -0.7f),
            new Vector2(0.7f, -0.7f),
            new Vector2(-0.7f, 0.7f),
            new Vector2(0.7f, 0.7f)
        };

        readonly ExplorerRobotSettings _settings;
        readonly DiscoveryRuntime _discovery;

        /// <summary>Where a card's worth lands when the robot docks. Optional: null is a world where cards are carried and never cashed, which is what a headless test of the harvest itself wants.</summary>
        readonly ComputeSystem _compute;

        readonly Vector2 _coreCentreCells;
        readonly int _seed;

        readonly List<ExplorerRobotRuntime> _robots = new List<ExplorerRobotRuntime>();

        /// <summary>The measurement instrument, or null when it is switched off - which it is by default. To be deleted with the rest of it; see ExplorerHarvestLog.</summary>
        readonly ExplorerHarvestLog _log;

        /// <summary>Which sector a position falls in. Optional: null means nothing is materialised, which is what a headless test of the wander itself wants.</summary>
        readonly SectorGrid _sectors;

        /// <summary>
        /// Turns a sector's derived deposits into real ones. Set after construction rather than
        /// injected, because it needs the ore definitions world generation owns and this system is
        /// built before them.
        ///
        /// Optional: null means a robot reveals ground and materialises nothing.
        /// </summary>
        public SectorMaterialisation Materialisation { get; set; }

        /// <summary>
        /// The wrecks to be found, or null in a scene without them. Set after construction for the
        /// same reason Materialisation is: the field needs the Core's centre, which world generation
        /// owns.
        /// </summary>
        public WreckField Wrecks { get; set; }

        /// <summary>
        /// Raised the moment a robot's card stock fills, and <b>once per filling</b>: the alert it
        /// drives is a decision put to the player, and repeating it would be nagging about a choice
        /// already made. Cleared when the robot unloads, so the next load can announce itself.
        /// </summary>
        public event Action<ExplorerRobotRuntime> StockFilled;

        public ExplorerRobotSystem(ExplorerRobotSettings settings, DiscoveryRuntime discovery,
            ComputeSystem compute, Vector2 coreCentreCells, Vector2 parkOrigin, int seed,
            SectorGrid sectors = null, ExplorerHarvestLog log = null)
        {
            _settings = settings;
            _discovery = discovery;
            _compute = compute;
            _coreCentreCells = coreCentreCells;
            _seed = seed;
            _sectors = sectors;
            _log = log;

            int count = settings != null ? settings.RobotCount : 0;
            for (int i = 0; i < count; i++)
            {
                // Spread along a row so two robots never stand on the same cell, which would make
                // the pair unclickable as a pair.
                _robots.Add(new ExplorerRobotRuntime(i, parkOrigin + new Vector2(i, 0f)));
            }
        }

        public IReadOnlyList<ExplorerRobotRuntime> Robots => _robots;

        /// <summary>How many cards a robot can carry. Read by the panel so the "3/10" it shows and the cap the harvest enforces are one number.</summary>
        public int MaxCards => _settings != null ? _settings.MaxCards : 0;

        /// <summary>How many are out - either wandering or on their way back. What a caller wants in order to say "2 dehors" without walking the list itself.</summary>
        public int OutCount
        {
            get
            {
                int count = 0;
                foreach (ExplorerRobotRuntime robot in _robots)
                {
                    if (robot.State != ExplorerRobotState.Idle) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// The robot nearest a point in cell space, within <see cref="PickRadiusCells"/> of it, or
        /// null. What makes the fleet clickable.
        ///
        /// A distance test rather than a grid lookup, because a wandering robot is not a grid
        /// occupant: it stands between cells, it is a continuous position rather than a tenant of
        /// one, and occupying a cell would stop a building being placed wherever it happened to be
        /// standing.
        /// </summary>
        public ExplorerRobotRuntime At(Vector2 pointCells)
        {
            ExplorerRobotRuntime nearest = null;
            float nearestDistance = PickRadiusCells * PickRadiusCells;

            foreach (ExplorerRobotRuntime robot in _robots)
            {
                float distance = (robot.Position - pointCells).sqrMagnitude;
                if (distance > nearestDistance) continue;

                nearest = robot;
                nearestDistance = distance;
            }

            return nearest;
        }

        // ---- The action ----

        /// <summary>
        /// The one gesture there is: a click sends an idle robot out, and a second click on one that
        /// is out turns it round. A robot already on its way home sets out again, which is what makes
        /// this a toggle rather than a one-way trip.
        /// </summary>
        public void Toggle(ExplorerRobotRuntime robot)
        {
            // Before the fleet has arrived there is nothing to send: Tick ignores a robot that has
            // not appeared, so accepting the gesture here would leave one marked Exploring and
            // standing still.
            if (robot == null || !RobotsHaveAppeared) return;

            if (robot.State == ExplorerRobotState.Exploring)
            {
                robot.State = ExplorerRobotState.Returning;
                return;
            }

            Depart(robot);
        }

        /// <summary>What the button on the robot panel should say. Here rather than in the panel: the label is a fact about the state, and two screens must not be able to disagree about it.</summary>
        public static string ActionLabel(ExplorerRobotState state)
            => state == ExplorerRobotState.Exploring ? "Rentrer" : "Exploration";

        void Depart(ExplorerRobotRuntime robot)
        {
            robot.HeadingDegrees = DepartureBearing(robot);
            robot.SortieCount++;
            robot.State = ExplorerRobotState.Exploring;

            // The departure itself is marked, so a sortie starts its trace at the base rather than a
            // cell out from it.
            RevealAround(robot);
        }

        /// <summary>
        /// Which way a robot leaves on. <b>Stepped by the golden ratio rather than drawn at random</b>,
        /// because "two successive sorties must not overlap" is the thing this prototype is being
        /// watched for, and a fair draw is perfectly free to put two of them five degrees apart.
        /// Consecutive sorties are about 137.5 degrees apart instead - the most spread any step can
        /// be - while the per-robot offset keeps two robots, and two worlds, from sharing a sequence.
        /// </summary>
        public float DepartureBearing(ExplorerRobotRuntime robot)
        {
            float offset = (float)DeterministicHash.Unit(_seed, robot.Index, DepartureSalt);
            return Frac(offset + robot.SortieCount * GoldenRatioConjugate) * 360f;
        }

        // ---- The clock ----

        public void Tick(float deltaSeconds)
        {
            if (_settings == null || deltaSeconds <= 0f) return;

            AppearIfReserveHasFallen();
            if (!RobotsHaveAppeared) return;

            float step = _settings.SpeedCellsPerSecond * deltaSeconds;

            for (int i = 0; i < _robots.Count; i++)
            {
                ExplorerRobotRuntime robot = _robots[i];

                switch (robot.State)
                {
                    case ExplorerRobotState.Exploring:
                        Wander(robot, step, deltaSeconds);
                        _log?.RecordDistance(step);
                        break;

                    case ExplorerRobotState.Returning:
                        // <b>The way back uncovers exactly like the way out.</b> It used to reveal
                        // nothing, on the reasoning that the return crosses ground already walked -
                        // which is wrong, and visibly so: the outward leg meanders while the return
                        // is a straight line, so it cuts across the gaps between the meanders and the
                        // robot was seen travelling through pure black. Ground under a robot is ground
                        // it can see.
                        bool arrived = robot.StepTowards(robot.HomePosition, step);
                        RevealIfMoved(robot);
                        _log?.RecordDistance(step);

                        if (arrived) Dock(robot);
                        break;
                }
            }

            _log?.Tick(deltaSeconds);
        }

        // ---- The fleet arriving ----

        /// <summary>Whether the robots exist yet. Once true it never goes back: a robot that has appeared has appeared - and it travels in the save.</summary>
        public bool RobotsHaveAppeared { get; private set; }

        /// <summary>
        /// Brings the robots out the moment the CU reserve has fallen far enough.
        ///
        /// <b>A fall, not a rise.</b> The introduction drains CU, and the fleet turning up when the
        /// reserve gets low is what makes it a way out rather than a reward.
        /// </summary>
        void AppearIfReserveHasFallen()
        {
            if (RobotsHaveAppeared || _compute == null) return;
            if (_compute.Reserve > _settings.AppearAtReserveCu) return;

            RobotsHaveAppeared = true;
        }

        /// <summary>
        /// Brings them out now, whatever the reserve holds - the one bypass of the threshold, and it
        /// exists for development: an introduction that has to be played through before the map opens
        /// is a tax on every test of what the map does.
        ///
        /// Idempotent, and it only ever grants: a run whose fleet has already arrived is untouched.
        /// </summary>
        public void MakeRobotsAppear() => RobotsHaveAppeared = true;

        /// <summary>
        /// The robot is home. <b>Cards are spent here and instantly</b> - they are never an item and
        /// never enter a container, so there is nothing to unload and nothing for the transport
        /// network to carry.
        ///
        /// Unloading is also what re-arms the alert: the stock that announced itself is gone, so the
        /// next one may announce itself in turn.
        /// </summary>
        void Dock(ExplorerRobotRuntime robot)
        {
            robot.State = ExplorerRobotState.Idle;

            if (robot.Cards > 0) _compute?.Grant(robot.Cards * _settings.CardValueCu);

            robot.Cards = 0;
            robot.StockAlertRaised = false;
        }

        void Wander(ExplorerRobotRuntime robot, float stepCells, float deltaSeconds)
        {
            robot.DriftPhase += _settings.DriftCyclesPerSecond * deltaSeconds;
            robot.HeadingDegrees = Mathf.Repeat(Steer(robot, deltaSeconds), 360f);

            robot.StepForward(stepCells);
            RevealIfMoved(robot);
        }

        /// <summary>
        /// Writes a reveal disc once the robot has travelled a cell since the last one. Shared by both
        /// legs, so "the return reveals like the outward leg" is one call site rather than two copies
        /// that can drift.
        /// </summary>
        void RevealIfMoved(ExplorerRobotRuntime robot)
        {
            if ((robot.Position - robot.LastRevealPosition).sqrMagnitude < RevealStepCells * RevealStepCells) return;

            RevealAround(robot);
        }

        /// <summary>
        /// The heading this robot should be on after <paramref name="deltaSeconds"/>. The whole of
        /// the wander, and the only place the three influences meet.
        ///
        /// They are applied in this order on purpose. The drift and the pull are additive nudges and
        /// commute; the recall comes last and is a <i>turn towards</i> rather than a nudge, so past
        /// the limit it caps what the other two did rather than being averaged with them - a robot
        /// outside the boundary always ends the tick pointing further in than it started.
        ///
        /// Pure, and taking the robot rather than mutating it, so a test can ask what one tick would
        /// do at a chosen position and heading without running a fleet.
        /// </summary>
        public float Steer(ExplorerRobotRuntime robot, float deltaSeconds)
        {
            float heading = robot.HeadingDegrees;

            // 1. The drift. A noise sampled over a phase that advances with time, never a fresh draw
            // per frame: independent draws average to nothing over a second and leave the robot
            // shivering along a straight line, while a noise that evolves makes the path meander.
            heading += SmoothNoise(robot.Index, robot.DriftPhase) * _settings.DriftDegreesPerSecond * deltaSeconds;

            // 2. The pull towards the unknown. Two probes off the current heading, and the robot
            // leans towards whichever side has less behind it. Scaled by the difference rather than
            // by its sign, so open ground barely steers and only a real frontier turns it - which is
            // enough to make it follow the edge of what it has opened instead of crossing back over
            // it, with nothing anywhere that resembles an objective.
            float left = DiscoveredScore(ProbePoint(robot, heading, _settings.ProbeAngleDegrees));
            float right = DiscoveredScore(ProbePoint(robot, heading, -_settings.ProbeAngleDegrees));
            heading += (right - left) * _settings.UnknownTurnDegreesPerSecond * deltaSeconds;

            // 3. The recall. Not a wall and not a stop: past the limit the heading bends inwards, and
            // the ramp is what makes that an arc rather than a snap.
            Vector2 fromCore = robot.Position - _coreCentreCells;
            float overshoot = fromCore.magnitude - _settings.MaxRadiusCells;
            if (overshoot <= 0f) return heading;

            float strength = Mathf.Clamp01(overshoot / _settings.BoundaryRampCells);
            float inward = Mathf.Atan2(-fromCore.y, -fromCore.x) * Mathf.Rad2Deg;

            return Mathf.MoveTowardsAngle(heading, inward,
                _settings.BoundaryTurnDegreesPerSecond * strength * deltaSeconds);
        }

        Vector2 ProbePoint(ExplorerRobotRuntime robot, float heading, float offsetDegrees)
        {
            float radians = (heading + offsetDegrees) * Mathf.Deg2Rad;
            return robot.Position
                + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * _settings.ProbeDistanceCells;
        }

        /// <summary>
        /// How much of the patch around a point is already known, in [0, 1].
        ///
        /// <b>Off the map reads as discovered</b>, which is what keeps the world edge from being the
        /// most attractive thing on it: there is nothing out there to find, so it should repel
        /// exactly like ground already walked. Reading it as unknown would draw every robot straight
        /// at the border.
        /// </summary>
        float DiscoveredScore(Vector2 pointCells)
        {
            if (_discovery == null) return 0f;

            int known = 0;
            for (int i = 0; i < ProbePattern.Length; i++)
            {
                Vector2 sample = pointCells + ProbePattern[i] * _settings.ProbeSpreadCells;
                var cell = new GridCoord(Mathf.FloorToInt(sample.x), Mathf.FloorToInt(sample.y));

                if (!_discovery.Contains(cell) || _discovery.IsDiscovered(cell)) known++;
            }

            return known / (float)ProbePattern.Length;
        }

        void RevealAround(ExplorerRobotRuntime robot)
        {
            robot.LastRevealPosition = robot.Position;

            // RevealDisc already answers how many cells it changed, which is exactly the figure the
            // harvest is paid on. Counting it here rather than re-walking the disc is what makes
            // "new ground, not time or distance" free: the number is a by-product of revealing.
            int newCells = _discovery?.RevealDisc(robot.Position, _settings.RevealRadiusCells) ?? 0;

            robot.RevealsWithoutNewGround = newCells > 0 ? 0 : robot.RevealsWithoutNewGround + 1;
            _log?.RecordNewCells(newCells);

            // On the same beat and against the same radius as the reveal, so a wreck is found exactly
            // when the ground it stands on is uncovered. A separate proximity range would be a second
            // rule, free to let a robot walk over an undiscovered wreck or spot one through the fog.
            // Eight distance checks, no allocation, and nothing at all once they are all found.
            Wrecks?.DiscoverWithin(robot.Position, _settings.RevealRadiusCells);

            MaterialiseAround(robot);
            Harvest(robot, newCells);
        }

        /// <summary>
        /// Turns the derived deposits of the ground around the robot into real ones.
        ///
        /// <b>The robots are what finds new deposits</b> - there is nothing else left that could.
        /// Sectors carry derived contents that only become real when something reports on them, and
        /// with the missions gone this is the one caller; without it a robot would open a map with
        /// nothing on it however far it went.
        ///
        /// <b>Fired when the robot crosses into a new sector, and it materialises the 3x3 block
        /// around it.</b> The block matters: the reveal disc straddles up to four sectors, so
        /// materialising only the one under the robot would leave ore missing from ground it plainly
        /// uncovered. A block 48 cells across covers everything a 12-cell disc can touch while the
        /// robot is anywhere in the middle sector. Once per sector entered rather than once per
        /// reveal, because each call walks a sector's cells to check for placed content.
        /// </summary>
        void MaterialiseAround(ExplorerRobotRuntime robot)
        {
            if (Materialisation == null || _sectors == null) return;

            int sector = _sectors.IndexAt(new GridCoord(
                Mathf.FloorToInt(robot.Position.x), Mathf.FloorToInt(robot.Position.y)));

            if (sector < 0 || sector == robot.LastMaterialisedSector) return;
            robot.LastMaterialisedSector = sector;

            int column = _sectors.ColumnOf(sector);
            int row = _sectors.RowOf(sector);

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int neighbour = _sectors.IndexAt(column + dx, row + dy);
                    if (neighbour >= 0) Materialisation.Materialise(neighbour);
                }
            }
        }

        /// <summary>
        /// Credits newly opened ground towards the next card, and hands over as many cards as that
        /// buys.
        ///
        /// <b>Paid in new ground and nothing else.</b> A robot going back over what it has already
        /// opened reveals nothing, so <paramref name="newCells"/> is zero and it earns nothing -
        /// which is the rule the whole feature rests on: paying for time or distance would pay for
        /// standing still.
        ///
        /// A loop rather than a single test, because one reveal can cross a threshold outright at a
        /// wide radius, and the remainder carries over rather than being discarded.
        /// </summary>
        void Harvest(ExplorerRobotRuntime robot, int newCells)
        {
            // At the cap it stops earning and keeps wandering - and stops accumulating too, so a
            // full robot does not bank progress it was never paid for.
            if (newCells <= 0 || robot.Cards >= _settings.MaxCards) return;

            robot.NewCellsSinceLastCard += newCells;

            while (robot.Cards < _settings.MaxCards)
            {
                float threshold = CardThreshold(robot);
                if (robot.NewCellsSinceLastCard < threshold) break;

                robot.NewCellsSinceLastCard -= threshold;
                robot.CardsDrawnEver++;
                robot.Cards++;
                _log?.RecordCard();
            }

            if (robot.Cards < _settings.MaxCards || robot.StockAlertRaised) return;

            robot.StockAlertRaised = true;
            StockFilled?.Invoke(robot);
        }

        /// <summary>
        /// How much new ground this robot's next card costs, jittered either side of the setting.
        ///
        /// <b>Drawn from the card's ordinal, not from the clock</b>, so it survives a save: a reload
        /// re-derives the same threshold the robot was already part-way towards, instead of re-rolling
        /// it. Through <see cref="DeterministicHash"/> for the same reason the drift is.
        ///
        /// The jitter is what stops the card being a metronome. Without it every card falls at exactly
        /// the same interval and the player reads a counter rather than a find.
        /// </summary>
        public float CardThreshold(ExplorerRobotRuntime robot)
        {
            float unit = (float)DeterministicHash.Unit(_seed + robot.Index * 7919, robot.CardsDrawnEver, CardSalt);
            float jitter = (unit * 2f - 1f) * _settings.CardThresholdJitterFraction;

            return Mathf.Max(1f, _settings.CellsPerCard * (1f + jitter));
        }

        /// <summary>
        /// What the robot's panel needs in order to say whether it is still earning - the one thing a
        /// player has to know to decide about recalling it. A full robot that has been forgotten
        /// otherwise looks exactly like one at work.
        /// </summary>
        public ExplorerHarvestState HarvestStateOf(ExplorerRobotRuntime robot)
        {
            if (robot == null) return ExplorerHarvestState.AtBase;

            switch (robot.State)
            {
                case ExplorerRobotState.Idle: return ExplorerHarvestState.AtBase;
                case ExplorerRobotState.Returning: return ExplorerHarvestState.Returning;
            }

            if (_settings != null && robot.Cards >= _settings.MaxCards) return ExplorerHarvestState.StockFull;

            return robot.RevealsWithoutNewGround >= BarrenRevealsBeforeKnownGround
                ? ExplorerHarvestState.OverKnownGround
                : ExplorerHarvestState.Harvesting;
        }

        /// <summary>
        /// Value noise in [-1, 1] over a continuous phase: two hashed samples either side, blended
        /// with a smoothstep so the <i>rate of turn</i> is continuous too. A linear blend would put a
        /// kink in the path at every integer phase, which reads as the robot flinching once a cycle.
        ///
        /// Through <see cref="DeterministicHash"/> rather than <c>System.Random</c>, per
        /// DEVELOPMENT_RULES - a path has to replay identically from a save, which means the
        /// arithmetic under it may never change its answer. The index is folded into the seed so two
        /// robots leaving together do not trace the same curve.
        /// </summary>
        public float SmoothNoise(int robotIndex, float phase)
        {
            int whole = Mathf.FloorToInt(phase);
            float t = phase - whole;

            int seed = _seed + robotIndex * 7919;
            float a = (float)(DeterministicHash.Unit(seed, whole, DriftSalt) * 2.0 - 1.0);
            float b = (float)(DeterministicHash.Unit(seed, whole + 1, DriftSalt) * 2.0 - 1.0);

            return Mathf.Lerp(a, b, t * t * (3f - 2f * t));
        }

        static float Frac(float value) => value - Mathf.Floor(value);

        // ---- Save / Restore (CONTRACTS.md §14) ----

        /// <summary>
        /// Position, heading and state, plus the two things that make a restored robot carry on
        /// rather than restart: where its drift had got to, and how many times it has been out.
        /// There is no destination to save, because there is none to have.
        /// </summary>
        public JObject CaptureState()
        {
            var robots = new JArray();
            foreach (ExplorerRobotRuntime robot in _robots)
            {
                robots.Add(new JObject
                {
                    ["x"] = robot.Position.x,
                    ["y"] = robot.Position.y,
                    ["heading"] = robot.HeadingDegrees,
                    ["state"] = (int)robot.State,
                    ["phase"] = robot.DriftPhase,
                    ["sorties"] = robot.SortieCount,

                    // The load and the progress towards the next card. CardsDrawnEver is what the
                    // next threshold is drawn from, so without it a reload re-rolls the threshold the
                    // robot is already part-way towards; alerted keeps a reloaded full robot from
                    // announcing itself a second time for the same load.
                    ["cards"] = robot.Cards,
                    ["cardCells"] = robot.NewCellsSinceLastCard,
                    ["cardsEver"] = robot.CardsDrawnEver,
                    ["alerted"] = robot.StockAlertRaised
                });
            }

            return new JObject
            {
                ["appeared"] = RobotsHaveAppeared,
                ["robots"] = robots
            };
        }

        /// <summary>
        /// Tolerant like every other Restore: a null, an absent key or a shorter list restores as a
        /// fleet standing at the base, which is the truthful default - a robot nobody has sent
        /// anywhere is at home.
        /// </summary>
        public void RestoreState(JObject state)
        {
            RobotsHaveAppeared = false;

            foreach (ExplorerRobotRuntime robot in _robots)
            {
                robot.Position = robot.HomePosition;
                robot.LastRevealPosition = robot.HomePosition;
                robot.HeadingDegrees = 0f;
                robot.State = ExplorerRobotState.Idle;
                robot.DriftPhase = 0f;
                robot.SortieCount = 0;
                robot.Cards = 0;
                robot.NewCellsSinceLastCard = 0f;
                robot.CardsDrawnEver = 0;
                robot.StockAlertRaised = false;
                robot.RevealsWithoutNewGround = 0;
                robot.LastMaterialisedSector = -1;
            }

            if (state == null) return;

            RobotsHaveAppeared = state.Value<bool?>("appeared") ?? false;

            if (!(state["robots"] is JArray saved)) return;

            int count = Mathf.Min(saved.Count, _robots.Count);
            for (int i = 0; i < count; i++)
            {
                if (!(saved[i] is JObject json)) continue;

                ExplorerRobotRuntime robot = _robots[i];
                robot.Position = new Vector2(json.Value<float?>("x") ?? robot.HomePosition.x,
                    json.Value<float?>("y") ?? robot.HomePosition.y);
                robot.LastRevealPosition = robot.Position;
                robot.HeadingDegrees = json.Value<float?>("heading") ?? 0f;
                robot.State = (ExplorerRobotState)(json.Value<int?>("state") ?? 0);
                robot.DriftPhase = json.Value<float?>("phase") ?? 0f;
                robot.SortieCount = json.Value<int?>("sorties") ?? 0;

                // Absent keys restore as an empty robot that has never earned - the truthful default
                // for a save written before cards existed.
                robot.Cards = json.Value<int?>("cards") ?? 0;
                robot.NewCellsSinceLastCard = json.Value<float?>("cardCells") ?? 0f;
                robot.CardsDrawnEver = json.Value<int?>("cardsEver") ?? 0;
                robot.StockAlertRaised = json.Value<bool?>("alerted") ?? false;
            }
        }
    }
}

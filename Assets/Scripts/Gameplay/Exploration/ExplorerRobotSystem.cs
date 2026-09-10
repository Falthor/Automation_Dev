using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Grid;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// Free exploration: robots the player sends out to wander, which uncover ground as they go and
    /// come back when told to.
    ///
    /// <b>Beside the mission system, not part of it.</b> Nothing here reads or writes
    /// <c>MissionSystem</c> or <c>ExpeditionZoneSystem</c>: no charge is spent, no report is
    /// produced, no zone is chosen and no site is involved. The two answer different questions - a
    /// mission is aimed and resolves, this is an action that is started and interrupted - and a
    /// robot out here is visible the whole time it is working, which is the opposite of a mission's
    /// "nothing reaches the Core while a robot is out".
    ///
    /// <b>There is no destination, and that is the design rather than a gap.</b> A destination plus
    /// straight-line travel would uncover a radius: three sorties would draw three spokes out of the
    /// Core and the map would fill in as a star. So a robot carries a <i>heading</i> that changes
    /// continuously, and three things bend it - see <see cref="Steer"/>.
    /// </summary>
    public sealed class ExplorerRobotSystem
    {
        /// <summary>Distinct salts so the drift and the departure bearing are independent draws rather than two views of one number.</summary>
        const uint DriftSalt = 0x1B873593;
        const uint DepartureSalt = 0xCC9E2D51;

        /// <summary>1/phi. Consecutive sorties step by this fraction of a turn - about 137.5 degrees - which is what stops two of them ever leaving on nearly the same bearing.</summary>
        const float GoldenRatioConjugate = 0.6180339887f;

        /// <summary>How far the robot moves between two reveal discs. One cell against a reveal radius of six means they overlap heavily, so the trail is a band rather than a row of beads - the same reasoning MissionSystem.RevealTrail uses for its own step.</summary>
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
        readonly Vector2 _coreCentreCells;
        readonly int _seed;

        readonly List<ExplorerRobotRuntime> _robots = new List<ExplorerRobotRuntime>();

        public ExplorerRobotSystem(ExplorerRobotSettings settings, DiscoveryRuntime discovery,
            Vector2 coreCentreCells, Vector2 parkOrigin, int seed)
        {
            _settings = settings;
            _discovery = discovery;
            _coreCentreCells = coreCentreCells;
            _seed = seed;

            int count = settings != null ? settings.RobotCount : 0;
            for (int i = 0; i < count; i++)
            {
                // Spread along a row so two robots never stand on the same cell, which would make
                // the pair unclickable as a pair.
                _robots.Add(new ExplorerRobotRuntime(i, parkOrigin + new Vector2(i, 0f)));
            }
        }

        public IReadOnlyList<ExplorerRobotRuntime> Robots => _robots;

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
            if (robot == null) return;

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

            float step = _settings.SpeedCellsPerSecond * deltaSeconds;

            foreach (ExplorerRobotRuntime robot in _robots)
            {
                switch (robot.State)
                {
                    case ExplorerRobotState.Exploring:
                        Wander(robot, step, deltaSeconds);
                        break;

                    case ExplorerRobotState.Returning:
                        // Straight home, and it uncovers nothing: the way back is ground the robot
                        // has already walked, and a return leg that revealed would draw a second
                        // corridor across a map the outward leg has already answered for.
                        if (robot.StepTowards(robot.HomePosition, step)) robot.State = ExplorerRobotState.Idle;
                        break;
                }
            }
        }

        void Wander(ExplorerRobotRuntime robot, float stepCells, float deltaSeconds)
        {
            robot.DriftPhase += _settings.DriftCyclesPerSecond * deltaSeconds;
            robot.HeadingDegrees = Mathf.Repeat(Steer(robot, deltaSeconds), 360f);

            robot.StepForward(stepCells);

            if ((robot.Position - robot.LastRevealPosition).sqrMagnitude >= RevealStepCells * RevealStepCells)
            {
                RevealAround(robot);
            }
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
            _discovery?.RevealDisc(robot.Position, _settings.RevealRadiusCells);
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
                    ["sorties"] = robot.SortieCount
                });
            }

            return new JObject { ["robots"] = robots };
        }

        /// <summary>
        /// Tolerant like every other Restore: a null, an absent key or a shorter list restores as a
        /// fleet standing at the base, which is the truthful default - a robot nobody has sent
        /// anywhere is at home.
        /// </summary>
        public void RestoreState(JObject state)
        {
            foreach (ExplorerRobotRuntime robot in _robots)
            {
                robot.Position = robot.HomePosition;
                robot.LastRevealPosition = robot.HomePosition;
                robot.HeadingDegrees = 0f;
                robot.State = ExplorerRobotState.Idle;
                robot.DriftPhase = 0f;
                robot.SortieCount = 0;
            }

            if (!(state?["robots"] is JArray saved)) return;

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
            }
        }
    }
}

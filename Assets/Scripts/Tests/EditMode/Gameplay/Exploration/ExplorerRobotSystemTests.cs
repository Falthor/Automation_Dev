using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Exploration;
using Game.Grid;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Exploration
{
    /// <summary>
    /// Free exploration with no screen anywhere near it.
    ///
    /// <b>The shape of the trace is the deliverable</b>, so it is what most of these measure. Three
    /// things would each kill the prototype quietly: a path that is really a straight line (a
    /// destination in disguise, which fills the map as a star), a path that closes into a circle near
    /// the base (a robot that never gets anywhere), and two sorties leaving on the same bearing (the
    /// second uncovering nothing the first did not). None of the three throws - they just look wrong,
    /// which is exactly the kind of defect a measurement catches and a smoke test does not.
    /// </summary>
    public class ExplorerRobotSystemTests
    {
        const int MapSize = 2000;
        const int ChunkSize = 64;
        const int Seed = 20260910;

        static readonly Vector2 CoreCentre = new Vector2(1000f, 1000f);

        /// <summary>A tick of 1/60 s, so a measured path is the one the game would actually walk.</summary>
        const float Frame = 1f / 60f;

        static ExplorerRobotSettings NewSettings(
            int robotCount = 1,
            float speed = 2f,
            float maxRadius = 330f,
            float revealRadius = 6f,
            float drift = 6f,
            float driftCycles = 0.08f,
            float unknownTurn = 20f,
            float probeDistance = 30f,
            float probeAngle = 45f,
            float boundaryTurn = 30f,
            float boundaryRamp = 40f,
            float cellsPerCard = 2500f,
            float cardValueCu = 250f,
            float cardJitter = 0.3f,
            int maxCards = 10)
        {
            var settings = ScriptableObject.CreateInstance<ExplorerRobotSettings>();
            var so = new SerializedObject(settings);

            so.FindProperty("robotCount").intValue = robotCount;
            so.FindProperty("speedCellsPerSecond").floatValue = speed;
            so.FindProperty("maxRadiusCells").floatValue = maxRadius;
            so.FindProperty("revealRadiusCells").floatValue = revealRadius;
            so.FindProperty("driftDegreesPerSecond").floatValue = drift;
            so.FindProperty("driftCyclesPerSecond").floatValue = driftCycles;
            so.FindProperty("unknownTurnDegreesPerSecond").floatValue = unknownTurn;
            so.FindProperty("probeDistanceCells").floatValue = probeDistance;
            so.FindProperty("probeAngleDegrees").floatValue = probeAngle;
            so.FindProperty("boundaryTurnDegreesPerSecond").floatValue = boundaryTurn;
            so.FindProperty("boundaryRampCells").floatValue = boundaryRamp;
            so.FindProperty("cellsPerCard").floatValue = cellsPerCard;
            so.FindProperty("cardValueCu").floatValue = cardValueCu;
            so.FindProperty("cardThresholdJitterFraction").floatValue = cardJitter;
            so.FindProperty("maxCards").intValue = maxCards;

            so.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        static DiscoveryRuntime NewDiscovery() => new DiscoveryRuntime(MapSize, ChunkSize);

        static ExplorerRobotSystem NewSystem(out DiscoveryRuntime discovery,
            ExplorerRobotSettings settings = null, int seed = Seed)
            => NewSystem(out discovery, out _, settings, seed);

        static ExplorerRobotSystem NewSystem(out DiscoveryRuntime discovery, out ComputeSystem compute,
            ExplorerRobotSettings settings = null, int seed = Seed)
        {
            discovery = NewDiscovery();
            compute = new ComputeSystem();

            var system = new ExplorerRobotSystem(settings ?? NewSettings(), discovery, compute,
                CoreCentre, CoreCentre, seed);

            // The fleet is handed over up front. These tests are about how a robot wanders, not
            // about when it arrives - and the reserve starts at its cap, which is above the
            // threshold, so without this every robot would sit at the base and every measurement
            // below would read zero.
            system.MakeRobotsAppear();
            return system;
        }

        static void RevealWholeMap(DiscoveryRuntime discovery)
        {
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++) discovery.Reveal(new GridCoord(x, y));
            }
        }

        /// <summary>Runs one robot for a while and hands back the path it walked, sampled once a second.</summary>
        static Vector2[] WalkSortie(ExplorerRobotSystem system, ExplorerRobotRuntime robot, float seconds)
        {
            var path = new System.Collections.Generic.List<Vector2>();
            int frames = Mathf.RoundToInt(seconds / Frame);

            for (int i = 0; i < frames; i++)
            {
                system.Tick(Frame);
                if (i % 60 == 0) path.Add(robot.Position);
            }

            path.Add(robot.Position);
            return path.ToArray();
        }

        static float PathLength(Vector2[] path)
        {
            float total = 0f;
            for (int i = 1; i < path.Length; i++) total += Vector2.Distance(path[i - 1], path[i]);
            return total;
        }

        /// <summary>How far the path strays from the straight line between its own two ends. Zero for a ruler.</summary>
        static float MaxDeviationFromChord(Vector2[] path)
        {
            Vector2 from = path[0];
            Vector2 chord = path[path.Length - 1] - from;
            float length = chord.magnitude;
            if (length <= 0.0001f) return 0f;

            Vector2 normal = new Vector2(-chord.y, chord.x) / length;
            float worst = 0f;
            foreach (Vector2 point in path) worst = Mathf.Max(worst, Mathf.Abs(Vector2.Dot(point - from, normal)));
            return worst;
        }

        // ---- The three states ----

        [Test]
        public void ANewFleetStandsAtTheBase()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(robotCount: 2));

            Assert.AreEqual(2, system.Robots.Count);
            foreach (ExplorerRobotRuntime robot in system.Robots)
            {
                Assert.AreEqual(ExplorerRobotState.Idle, robot.State);
                Assert.AreEqual(robot.HomePosition, robot.Position);
            }
            Assert.AreEqual(0, system.OutCount, "Nobody has been sent anywhere yet.");
        }

        /// <summary>
        /// Before the CU reserve has fallen far enough the fleet does not exist yet, and a robot that
        /// does not exist cannot be sent anywhere. Worth pinning because the two halves are in
        /// different places - Toggle refuses, and Tick ignores - and either alone would leave a robot
        /// marked as exploring while standing still.
        /// </summary>
        [Test]
        public void BeforeTheFleetArrives_NothingCanBeSentAnywhere()
        {
            var discovery = NewDiscovery();
            var compute = new ComputeSystem();
            var system = new ExplorerRobotSystem(NewSettings(), discovery, compute, CoreCentre, CoreCentre, Seed);

            Assert.IsFalse(system.RobotsHaveAppeared, "The reserve starts at its cap, well above the threshold.");

            ExplorerRobotRuntime robot = system.Robots[0];
            system.Toggle(robot);
            Assert.AreEqual(ExplorerRobotState.Idle, robot.State, "Toggle refuses.");

            WalkSortie(system, robot, 30f);
            Assert.AreEqual(robot.HomePosition, robot.Position, "And nothing moves.");

            // Spent down past the threshold, they turn up on the next tick.
            compute.Spend(ComputeSystem.ReserveCap - 1000f);
            system.Tick(Frame);
            Assert.IsTrue(system.RobotsHaveAppeared);
        }

        [Test]
        public void TwoRobotsNeverShareACellAtRest()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(robotCount: 2));

            Assert.AreNotEqual(system.Robots[0].Position, system.Robots[1].Position,
                "Robots parked on the same cell could not be told apart by a click.");
        }

        [Test]
        public void OneClickSendsItOut_ASecondTurnsItRound_AThirdSendsItOutAgain()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            Assert.AreEqual(ExplorerRobotState.Exploring, robot.State);
            Assert.AreEqual("Rentrer", ExplorerRobotSystem.ActionLabel(robot.State));

            system.Toggle(robot);
            Assert.AreEqual(ExplorerRobotState.Returning, robot.State);
            Assert.AreEqual("Exploration", ExplorerRobotSystem.ActionLabel(robot.State));

            // A robot already on its way home sets out again - the action is a toggle, not a
            // one-way trip.
            system.Toggle(robot);
            Assert.AreEqual(ExplorerRobotState.Exploring, robot.State);
        }

        [Test]
        public void AReturningRobotReachesHomeAndStops()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            robot.Position = CoreCentre + new Vector2(60f, 0f);
            robot.State = ExplorerRobotState.Returning;

            // 60 cells at 2 cells a second is 30 s; give it 40.
            for (int i = 0; i < Mathf.RoundToInt(40f / Frame); i++) system.Tick(Frame);

            Assert.AreEqual(ExplorerRobotState.Idle, robot.State);
            Assert.AreEqual(robot.HomePosition, robot.Position);
        }

        [Test]
        public void AnIdleRobotDoesNotMoveAndUncoversNothing()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            int before = discovery.Version;
            for (int i = 0; i < 600; i++) system.Tick(Frame);

            Assert.AreEqual(robot.HomePosition, robot.Position);
            Assert.AreEqual(before, discovery.Version, "A robot at rest is not exploring.");
        }

        // ---- The reveal ----

        [Test]
        public void ExploringUncoversGroundAsItGoes()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            int atDeparture = discovery.DiscoveredCount();
            Assert.Greater(atDeparture, 0, "The departure itself is marked, so a trace starts at the base.");

            WalkSortie(system, robot, 60f);

            Assert.Greater(discovery.DiscoveredCount(), atDeparture,
                "The robot uncovers while it advances, not on its return.");
        }

        /// <summary>
        /// The return uncovers exactly like the outward leg, and this test used to assert the
        /// opposite.
        ///
        /// The old rule - "the way back crosses ground already walked, so it writes nothing" - was
        /// wrong for a reason only the screen shows: <b>the outward leg meanders while the return is
        /// a straight line</b>, so the return cuts across the gaps between the meanders and the robot
        /// was seen travelling through pure black. Ground under a robot is ground it can see.
        /// </summary>
        [Test]
        public void ReturningUncoversGroundLikeTheOutwardLegDoes()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            // Far out, on ground nobody has seen - which is exactly the case the straight line home
            // used to cross without opening.
            robot.Position = CoreCentre + new Vector2(120f, 40f);
            robot.State = ExplorerRobotState.Returning;

            int discovered = discovery.DiscoveredCount();

            for (int i = 0; i < Mathf.RoundToInt(90f / Frame); i++) system.Tick(Frame);

            int opened = discovery.DiscoveredCount() - discovered;
            TestContext.WriteLine($"the way home opened {opened} cells");

            Assert.AreEqual(ExplorerRobotState.Idle, robot.State, "It should have got home inside 90 s.");
            Assert.Greater(opened, 0, "A robot travelling over unseen ground has to open it.");
        }

        /// <summary>
        /// And the corridor it opens is continuous rather than a row of beads: the reveal discs are
        /// spaced a cell apart at a radius of six, so consecutive ones overlap heavily. Measured as
        /// "no gap wider than a disc" along the straight line home.
        /// </summary>
        [Test]
        public void TheWayHomeOpensAContinuousCorridor()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            Vector2 from = CoreCentre + new Vector2(60f, 0f);
            robot.Position = from;
            robot.State = ExplorerRobotState.Returning;

            for (int i = 0; i < Mathf.RoundToInt(60f / Frame); i++) system.Tick(Frame);
            Assert.AreEqual(ExplorerRobotState.Idle, robot.State);

            // Every whole cell along the line the robot walked has to be discovered.
            for (int step = 0; step <= 60; step++)
            {
                Vector2 point = Vector2.Lerp(from, robot.HomePosition, step / 60f);
                var cell = new GridCoord(Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y));

                Assert.IsTrue(discovery.IsDiscovered(cell), $"gap at {cell} along the way home");
            }
        }

        // ---- The shape of the trace ----

        [Test]
        public void TheTraceMeandersRatherThanRunningStraight()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            Vector2[] path = WalkSortie(system, robot, 240f);

            float length = PathLength(path);
            float deviation = MaxDeviationFromChord(path);

            TestContext.WriteLine($"path {length:0} cells, max deviation from its own chord {deviation:0.0} cells");

            // A straight line deviates by nothing. Anything that reads as a meander strays a good
            // many cells off the line between where it started and where it ended up.
            Assert.Greater(deviation, 5f,
                $"The path is essentially a ruler ({deviation:0.0} cells of deviation over {length:0}).");
        }

        [Test]
        public void ItGetsSomewhereRatherThanCirclingTheBase()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            Vector2[] path = WalkSortie(system, robot, 120f);

            float length = PathLength(path);
            float displacement = Vector2.Distance(path[0], path[path.Length - 1]);
            float straightness = displacement / length;

            TestContext.WriteLine($"walked {length:0} cells, ended {displacement:0} from the base ({straightness:0.00} of the path)");

            // A robot turning on the spot ends where it started; one that spirals near the base ends
            // a fraction of its path away. Half is a long way from either, and a long way from the
            // 1.0 of a straight line.
            Assert.Greater(straightness, 0.5f,
                $"The robot is going round rather than going out ({displacement:0} cells covered walking {length:0}).");
        }

        [Test]
        public void TwoSuccessiveSortiesLeaveOnBearingsFarApart()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            var bearings = new float[6];
            for (int i = 0; i < bearings.Length; i++)
            {
                bearings[i] = system.DepartureBearing(robot);
                robot.SortieCount++;
            }

            // The brief asks about successive sorties, and that is the pair the golden-ratio step
            // separates hardest: consecutive bearings are always about 137.5 degrees apart.
            float closestSuccessive = 360f;
            for (int i = 1; i < bearings.Length; i++)
            {
                closestSuccessive = Mathf.Min(closestSuccessive,
                    Mathf.Abs(Mathf.DeltaAngle(bearings[i - 1], bearings[i])));
            }

            float closestAnywhere = 360f;
            for (int i = 0; i < bearings.Length; i++)
            {
                for (int j = i + 1; j < bearings.Length; j++)
                {
                    closestAnywhere = Mathf.Min(closestAnywhere, Mathf.Abs(Mathf.DeltaAngle(bearings[i], bearings[j])));
                }
            }

            TestContext.WriteLine($"six sorties: closest successive pair {closestSuccessive:0.0} degrees, "
                + $"closest pair anywhere {closestAnywhere:0.0} degrees");

            Assert.Greater(closestSuccessive, 90f,
                $"Two sorties in a row leave on nearly the same bearing ({closestSuccessive:0.0} degrees apart).");

            // Over six sorties the closest pair is necessarily tighter - the golden ratio spreads N
            // points as evenly as any step can, and for six that floor is about 32 degrees. It is
            // still ample: the robot uncovers a band 12 cells wide, so two traces 32 degrees apart
            // stop overlapping about 22 cells out of the base. What the step buys is that the floor
            // exists at all - a fair draw is free to put two bearings a couple of degrees apart.
            Assert.Greater(closestAnywhere, 30f,
                $"Two of the six sorties nearly coincide ({closestAnywhere:0.0} degrees apart).");
        }

        [Test]
        public void TwoRobotsLeavingTogetherDoNotTraceTheSameCurve()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(robotCount: 2));

            system.Toggle(system.Robots[0]);
            system.Toggle(system.Robots[1]);
            for (int i = 0; i < Mathf.RoundToInt(120f / Frame); i++) system.Tick(Frame);

            float apart = Vector2.Distance(system.Robots[0].Position, system.Robots[1].Position);
            TestContext.WriteLine($"two robots, 120 s out, {apart:0} cells apart");

            Assert.Greater(apart, 20f, "Both robots walked the same path.");
        }

        // ---- The pull towards the unknown ----

        [Test]
        public void ItLeansAwayFromGroundItHasAlreadyOpened()
        {
            ExplorerRobotSettings settings = NewSettings(drift: 0f, boundaryTurn: 0f);
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery, settings);
            ExplorerRobotRuntime robot = system.Robots[0];

            // Everything on the robot's left is already known; the right is untouched. With the
            // drift silenced, the pull is the only thing that can move the heading.
            robot.Position = CoreCentre;
            robot.HeadingDegrees = 0f;
            discovery.RevealDisc(CoreCentre + new Vector2(21f, 21f), 20f);

            float steered = system.Steer(robot, 1f);

            TestContext.WriteLine($"heading 0 -> {steered:0.0} with discovered ground on the left");
            Assert.Less(steered, -1f, "The robot should turn right, away from what it has already seen.");
        }

        [Test]
        public void TheEdgeOfTheWorldRepelsRatherThanAttracts()
        {
            ExplorerRobotSettings settings = NewSettings(drift: 0f, boundaryTurn: 0f);
            ExplorerRobotSystem system = NewSystem(out _, settings);
            ExplorerRobotRuntime robot = system.Robots[0];

            // Heading along the bottom edge: the right probe falls off the map entirely, the left
            // one lands on ordinary undiscovered ground.
            robot.Position = new Vector2(20f, 10f);
            robot.HeadingDegrees = 0f;

            float steered = system.Steer(robot, 1f);

            TestContext.WriteLine($"heading 0 -> {steered:0.0} beside the map edge");
            Assert.Greater(steered, 1f,
                "Off-map has to read as discovered, or every robot drives at the border looking for what is not there.");
        }

        // ---- The boundary ----

        [Test]
        public void PastTheLimitItTurnsBackWithoutStoppingOrBeingClamped()
        {
            ExplorerRobotSettings settings = NewSettings(maxRadius: 330f);
            ExplorerRobotSystem system = NewSystem(out _, settings);
            ExplorerRobotRuntime robot = system.Robots[0];

            robot.Position = CoreCentre + new Vector2(360f, 0f);
            robot.HeadingDegrees = 0f;   // straight out
            robot.State = ExplorerRobotState.Exploring;

            float furthest = 0f;
            float previous = (robot.Position - CoreCentre).magnitude;
            bool everStalled = false;

            for (int i = 0; i < Mathf.RoundToInt(60f / Frame); i++)
            {
                Vector2 before = robot.Position;
                system.Tick(Frame);

                // No wall and no stop: it keeps moving at its own speed the whole time.
                if (Vector2.Distance(before, robot.Position) < settings.SpeedCellsPerSecond * Frame * 0.9f)
                {
                    everStalled = true;
                }

                furthest = Mathf.Max(furthest, (robot.Position - CoreCentre).magnitude);
            }

            float ended = (robot.Position - CoreCentre).magnitude;
            TestContext.WriteLine($"started 360 out, reached {furthest:0}, ended {ended:0} after 60 s");

            Assert.IsFalse(everStalled, "The limit is a bend, not a wall - the robot never stops or is clamped.");
            Assert.Less(ended, previous, "A robot outside the limit has to end up further in than it started.");
            Assert.Less(furthest, 460f, "It should curve back rather than coast far past the limit.");
        }

        [Test]
        public void InsideTheLimitTheBoundaryDoesNothingAtAll()
        {
            ExplorerRobotSettings settings = NewSettings(drift: 0f, boundaryTurn: 30f);
            ExplorerRobotSystem system = NewSystem(out _, settings);
            ExplorerRobotRuntime robot = system.Robots[0];

            robot.Position = CoreCentre + new Vector2(100f, 0f);
            robot.HeadingDegrees = 0f;

            // Nothing discovered anywhere, so the pull is neutral too; with the drift silenced the
            // heading must not move by so much as a degree.
            Assert.AreEqual(0f, system.Steer(robot, 1f), 0.0001f);
        }

        // ---- The drift's noise ----

        [Test]
        public void TheDriftNoiseIsContinuousRatherThanAFreshDrawPerFrame()
        {
            ExplorerRobotSystem system = NewSystem(out _);

            float worstStep = 0f;
            float previous = system.SmoothNoise(0, 0f);
            float lowest = previous, highest = previous;

            for (int i = 1; i <= 4000; i++)
            {
                float phase = i * 0.005f;
                float value = system.SmoothNoise(0, phase);

                worstStep = Mathf.Max(worstStep, Mathf.Abs(value - previous));
                lowest = Mathf.Min(lowest, value);
                highest = Mathf.Max(highest, value);
                previous = value;

                Assert.GreaterOrEqual(value, -1f);
                Assert.LessOrEqual(value, 1f);
            }

            TestContext.WriteLine($"noise over 20 cycles: range [{lowest:0.00}, {highest:0.00}], worst step per 0.005 cycle {worstStep:0.0000}");

            // Independent draws would step by up to 2 between samples; a noise that evolves cannot.
            Assert.Less(worstStep, 0.05f, "The drift is shivering rather than evolving.");

            // And it does have to actually go somewhere, or the drift is a constant.
            Assert.Greater(highest - lowest, 1f, "The noise barely moves - the path would not meander.");
        }

        [Test]
        public void TheSameSeedWalksTheSamePath_ADifferentSeedDoesNot()
        {
            Vector2 EndOfSortie(int seed)
            {
                var system = new ExplorerRobotSystem(NewSettings(), NewDiscovery(), new ComputeSystem(), CoreCentre, CoreCentre, seed);
                system.MakeRobotsAppear();
                ExplorerRobotRuntime robot = system.Robots[0];
                system.Toggle(robot);
                WalkSortie(system, robot, 120f);
                return robot.Position;
            }

            Assert.AreEqual(EndOfSortie(Seed), EndOfSortie(Seed), "A path has to replay identically.");
            Assert.AreNotEqual(EndOfSortie(Seed), EndOfSortie(Seed + 1), "Two worlds should not walk one path.");
        }

        // ---- Clicking one ----

        [Test]
        public void AClickCatchesTheRobotUnderIt_AndNothingElse()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(robotCount: 2));
            ExplorerRobotRuntime first = system.Robots[0];
            ExplorerRobotRuntime second = system.Robots[1];

            Assert.AreSame(first, system.At(first.Position), "A click on a robot finds it.");
            Assert.AreSame(second, system.At(second.Position), "And so does a click on its neighbour.");

            // The catch is a little wider than the sprite, because the target moves. Probed away
            // from the neighbour: the two park a cell apart, so their catch ranges overlap and it is
            // "nearest wins" that separates them - a point 0.8 towards the second robot belongs to
            // the second robot, which is the right answer rather than a near miss.
            Assert.AreSame(first, system.At(first.Position - new Vector2(0.8f, 0f)));
            Assert.AreSame(second, system.At(first.Position + new Vector2(0.8f, 0f)),
                "Between two parked robots, the nearer one wins.");

            Assert.IsNull(system.At(first.Position + new Vector2(40f, 40f)), "Empty ground catches nothing.");
        }

        [Test]
        public void AWanderingRobotIsStillClickable()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 60f);

            Assert.AreSame(robot, system.At(robot.Position), "It has to stay catchable once it is out.");
        }

        // ---- The harvest ----

        /// <summary>
        /// A card costs its own jittered threshold in <b>newly discovered</b> cells, and lands as soon
        /// as that much has been opened. Measured against the real discovery count rather than an
        /// internal counter, and bounded above by one reveal disc - the step that crossed the line
        /// could not have opened more than that.
        /// </summary>
        [Test]
        public void ACardLandsOnceTheThresholdOfNewGroundIsOpened()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            float threshold = system.CardThreshold(robot);

            // Sampled before the departure, not after: setting out writes a reveal disc of its own
            // and that ground is credited like any other. Measured after the Toggle, the count misses
            // those hundred-odd cells and the card looks as though it arrived early.
            int atDeparture = discovery.DiscoveredCount();
            system.Toggle(robot);

            // Enough travel to be sure of one card at any jitter, and it stops the moment one lands.
            for (int i = 0; i < Mathf.RoundToInt(900f / Frame) && robot.Cards == 0; i++) system.Tick(Frame);

            int opened = discovery.DiscoveredCount() - atDeparture;
            TestContext.WriteLine("threshold " + threshold.ToString("0") + ", first card after " + opened + " new cells");

            Assert.AreEqual(1, robot.Cards, "One card, and not two.");
            Assert.GreaterOrEqual(opened, Mathf.FloorToInt(threshold),
                "It cannot be paid before the ground is opened.");
            Assert.Less(opened, threshold + 200f,
                "Nor long after - the reveal that crossed the line opens a disc, not a region.");
        }

        /// <summary>The rule the whole feature rests on: paying for time or distance would pay for standing still.</summary>
        [Test]
        public void GoingBackOverKnownGroundEarnsNothing()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            RevealWholeMap(discovery);
            int discovered = discovery.DiscoveredCount();

            system.Toggle(robot);
            WalkSortie(system, robot, 600f);

            Assert.AreEqual(0, robot.Cards, "Nothing new was opened, so nothing was earned.");
            Assert.AreEqual(discovered, discovery.DiscoveredCount(), "And nothing new was opened.");
            Assert.AreEqual(ExplorerHarvestState.OverKnownGround, system.HarvestStateOf(robot),
                "And the panel has to say so, or a robot earning nothing looks like one at work.");
        }

        [Test]
        public void TheStockStopsAtTheCap_AndTheRobotKeepsWandering()
        {
            // A cheap card, so ten of them arrive inside a reasonable walk.
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 10));
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            Vector2[] path = WalkSortie(system, robot, 600f);

            Assert.AreEqual(10, robot.Cards, "Ten and no more.");
            Assert.AreEqual(ExplorerRobotState.Exploring, robot.State,
                "Full is not a reason to come home - that is the player's call.");
            Assert.AreEqual(ExplorerHarvestState.StockFull, system.HarvestStateOf(robot));
            Assert.Greater(PathLength(path), 100f, "And it went on wandering.");
        }

        [Test]
        public void AFullRobotBanksNoFurtherProgress()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 2));
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 150f);
            Assert.AreEqual(2, robot.Cards);

            float bankedWhenFull = robot.NewCellsSinceLastCard;
            WalkSortie(system, robot, 300f);

            Assert.AreEqual(bankedWhenFull, robot.NewCellsSinceLastCard,
                "A full robot stops accumulating - otherwise it banks ground it could not carry.");
        }

        [Test]
        public void DockingCashesEveryCardAndEmptiesTheRobot()
        {
            ExplorerRobotSystem system = NewSystem(out _, out ComputeSystem compute,
                NewSettings(cellsPerCard: 60f, cardValueCu: 250f, maxCards: 10));
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 200f);

            int cards = robot.Cards;
            Assert.Greater(cards, 0, "It has to have earned something for this to mean anything.");

            // Room to be granted into: the reserve starts at its cap.
            compute.Spend(10000f);
            float before = compute.Reserve;

            system.Toggle(robot);   // recall
            for (int i = 0; i < Mathf.RoundToInt(900f / Frame) && robot.State != ExplorerRobotState.Idle; i++)
            {
                system.Tick(Frame);
            }

            Assert.AreEqual(ExplorerRobotState.Idle, robot.State, "It should have got home.");
            Assert.AreEqual(0, robot.Cards, "Spent the instant it docks - cards are never an item.");
            Assert.AreEqual(before + cards * 250f, compute.Reserve, 0.01f, cards + " cards at 250 CU.");
        }

        [Test]
        public void ADockedRobotWithNoCardsGrantsNothing()
        {
            ExplorerRobotSystem system = NewSystem(out _, out ComputeSystem compute);
            ExplorerRobotRuntime robot = system.Robots[0];

            compute.Spend(10000f);
            float before = compute.Reserve;

            robot.Position = CoreCentre + new Vector2(10f, 0f);
            robot.State = ExplorerRobotState.Returning;
            for (int i = 0; i < Mathf.RoundToInt(30f / Frame); i++) system.Tick(Frame);

            Assert.AreEqual(ExplorerRobotState.Idle, robot.State);
            Assert.AreEqual(before, compute.Reserve, 0.01f);
        }

        // ---- The alert ----

        [Test]
        public void TheStockFullAlertFiresOncePerFilling()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 3));
            ExplorerRobotRuntime robot = system.Robots[0];

            int alerts = 0;
            system.StockFilled += _ => alerts++;

            system.Toggle(robot);
            WalkSortie(system, robot, 200f);
            Assert.AreEqual(3, robot.Cards);
            Assert.AreEqual(1, alerts, "Once when it filled.");

            // Kept wandering, full, for a long time: the alert must not come back.
            WalkSortie(system, robot, 400f);
            Assert.AreEqual(1, alerts, "Repeating it would be nagging about a decision already made.");

            // Home, unloaded, and out again: the next load may announce itself in turn.
            system.Toggle(robot);
            for (int i = 0; i < Mathf.RoundToInt(1200f / Frame) && robot.State != ExplorerRobotState.Idle; i++)
            {
                system.Tick(Frame);
            }
            Assert.AreEqual(0, robot.Cards, "It got home and unloaded.");

            system.Toggle(robot);
            WalkSortie(system, robot, 400f);

            Assert.AreEqual(3, robot.Cards);
            Assert.AreEqual(2, alerts, "A second filling is a second decision.");
        }

        [Test]
        public void TheAlertNamesTheRobotThatFilled()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(robotCount: 2, cellsPerCard: 60f, maxCards: 2));

            ExplorerRobotRuntime announced = null;
            system.StockFilled += robot => announced = robot;

            system.Toggle(system.Robots[1]);
            WalkSortie(system, system.Robots[1], 300f);

            Assert.AreSame(system.Robots[1], announced, "There are two of them, so the alert has to say which.");
        }

        // ---- The threshold and its jitter ----

        [Test]
        public void EachCardDrawsItsOwnThreshold_WithinTheConfiguredBand()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(cellsPerCard: 2500f, cardJitter: 0.3f));
            ExplorerRobotRuntime robot = system.Robots[0];

            float lowest = float.MaxValue;
            float highest = float.MinValue;
            float first = system.CardThreshold(robot);
            bool varied = false;

            for (int i = 0; i < 40; i++)
            {
                robot.CardsDrawnEver = i;
                float threshold = system.CardThreshold(robot);

                lowest = Mathf.Min(lowest, threshold);
                highest = Mathf.Max(highest, threshold);
                if (!Mathf.Approximately(threshold, first)) varied = true;
            }

            TestContext.WriteLine("forty cards: thresholds from " + lowest.ToString("0")
                + " to " + highest.ToString("0") + " around 2500");

            Assert.IsTrue(varied, "A fixed threshold is a metronome, which is what the jitter exists to break.");
            Assert.GreaterOrEqual(lowest, 2500f * 0.7f - 0.5f, "Never below the band.");
            Assert.LessOrEqual(highest, 2500f * 1.3f + 0.5f, "Never above it.");
        }

        [Test]
        public void AThresholdIsDrawnFromTheCardsOrdinal_SoAReloadDoesNotReRollIt()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            robot.CardsDrawnEver = 7;
            float before = system.CardThreshold(robot);

            ExplorerRobotSystem reloaded = NewSystem(out _);
            reloaded.Robots[0].CardsDrawnEver = 7;

            Assert.AreEqual(before, reloaded.CardThreshold(reloaded.Robots[0]), 0.0001f);
        }

        // ---- What the panel reads ----

        [Test]
        public void TheHarvestStateTellsRestAndReturnApartFromWorking()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            Assert.AreEqual(ExplorerHarvestState.AtBase, system.HarvestStateOf(robot));

            system.Toggle(robot);
            WalkSortie(system, robot, 10f);
            Assert.AreEqual(ExplorerHarvestState.Harvesting, system.HarvestStateOf(robot),
                "Fresh out over virgin ground.");

            system.Toggle(robot);
            Assert.AreEqual(ExplorerHarvestState.Returning, system.HarvestStateOf(robot));

            Assert.AreEqual(ExplorerHarvestState.AtBase, system.HarvestStateOf(null),
                "A null robot answers rather than throwing - the panel asks before a selection exists.");
        }

        // ---- Save / Restore ----

        [Test]
        public void ARobotOnItsWayResumesWhereItWasWithTheSameHeading()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 90f);

            Vector2 position = robot.Position;
            float heading = robot.HeadingDegrees;
            float phase = robot.DriftPhase;
            int sorties = robot.SortieCount;

            JObject saved = system.CaptureState();

            // A different fleet entirely, restoring the same blob.
            ExplorerRobotSystem reloaded = NewSystem(out _);
            reloaded.RestoreState(saved);
            ExplorerRobotRuntime restored = reloaded.Robots[0];

            Assert.AreEqual(ExplorerRobotState.Exploring, restored.State);
            Assert.AreEqual(position.x, restored.Position.x, 0.001f);
            Assert.AreEqual(position.y, restored.Position.y, 0.001f);
            Assert.AreEqual(heading, restored.HeadingDegrees, 0.001f);
            Assert.AreEqual(phase, restored.DriftPhase, 0.001f,
                "The drift has to carry on the bend it was in the middle of.");
            Assert.AreEqual(sorties, restored.SortieCount);
        }

        [Test]
        public void AReloadedRobotKeepsItsCardsAndItsProgressTowardsTheNext()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 10));
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 150f);

            int cards = robot.Cards;
            float banked = robot.NewCellsSinceLastCard;
            int drawn = robot.CardsDrawnEver;
            Assert.Greater(cards, 0, "It has to be carrying something for this to mean anything.");

            ExplorerRobotSystem reloaded = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 10));
            reloaded.RestoreState(system.CaptureState());
            ExplorerRobotRuntime restored = reloaded.Robots[0];

            Assert.AreEqual(cards, restored.Cards, "A reloaded robot keeps its cards.");
            Assert.AreEqual(banked, restored.NewCellsSinceLastCard, 0.01f,
                "And the ground it had already opened towards the next one.");
            Assert.AreEqual(drawn, restored.CardsDrawnEver,
                "And the ordinal its next threshold is drawn from, or the reload re-rolls it.");
        }

        [Test]
        public void AFullRobotReloadedDoesNotAnnounceItselfAgain()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 2));
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 300f);
            Assert.AreEqual(2, robot.Cards);

            ExplorerRobotSystem reloaded = NewSystem(out _, NewSettings(cellsPerCard: 60f, maxCards: 2));
            int alerts = 0;
            reloaded.StockFilled += _ => alerts++;
            reloaded.RestoreState(system.CaptureState());

            WalkSortie(reloaded, reloaded.Robots[0], 200f);

            Assert.IsTrue(reloaded.Robots[0].StockAlertRaised, "It is still full and still announced.");
            Assert.AreEqual(0, alerts, "The decision was already put to the player before the save.");
        }

        [Test]
        public void ARestoredRobotCarriesOnTheSamePathRatherThanAFreshOne()
        {
            ExplorerRobotSystem system = NewSystem(out _);
            ExplorerRobotRuntime robot = system.Robots[0];

            system.Toggle(robot);
            WalkSortie(system, robot, 60f);

            JObject saved = system.CaptureState();

            // Carry on in place...
            WalkSortie(system, robot, 60f);
            Vector2 uninterrupted = robot.Position;

            // ...against the same 60 s walked after a reload.
            ExplorerRobotSystem reloaded = NewSystem(out _);
            reloaded.RestoreState(saved);
            WalkSortie(reloaded, reloaded.Robots[0], 60f);

            Assert.AreEqual(uninterrupted.x, reloaded.Robots[0].Position.x, 0.01f);
            Assert.AreEqual(uninterrupted.y, reloaded.Robots[0].Position.y, 0.01f);
        }

        [Test]
        public void AnAbsentBlobRestoresAsAFleetStandingAtTheBase()
        {
            ExplorerRobotSystem system = NewSystem(out _, NewSettings(robotCount: 2));

            system.Toggle(system.Robots[0]);
            WalkSortie(system, system.Robots[0], 30f);

            system.RestoreState(null);

            foreach (ExplorerRobotRuntime robot in system.Robots)
            {
                Assert.AreEqual(ExplorerRobotState.Idle, robot.State,
                    "Nothing saved has to mean nobody was sent anywhere, which is the truthful default.");
                Assert.AreEqual(robot.HomePosition, robot.Position);
                Assert.AreEqual(0, robot.SortieCount);
            }
        }

        [Test]
        public void ABlobWithFewerRobotsThanTheFleetRestoresTheRestAtHome()
        {
            ExplorerRobotSystem one = NewSystem(out _, NewSettings(robotCount: 1));
            one.Toggle(one.Robots[0]);
            WalkSortie(one, one.Robots[0], 30f);

            ExplorerRobotSystem two = NewSystem(out _, NewSettings(robotCount: 2));
            two.RestoreState(one.CaptureState());

            Assert.AreEqual(ExplorerRobotState.Exploring, two.Robots[0].State);
            Assert.AreEqual(ExplorerRobotState.Idle, two.Robots[1].State);
            Assert.AreEqual(two.Robots[1].HomePosition, two.Robots[1].Position);
        }

        [Test]
        public void NoSettingsMeansNoRobots_NotACrash()
        {
            var system = new ExplorerRobotSystem(null, NewDiscovery(), new ComputeSystem(), CoreCentre, CoreCentre, Seed);

            Assert.AreEqual(0, system.Robots.Count);
            Assert.DoesNotThrow(() => system.Tick(Frame));
            Assert.DoesNotThrow(() => system.Toggle(null));
            Assert.IsNull(system.At(CoreCentre));
        }
    }
}

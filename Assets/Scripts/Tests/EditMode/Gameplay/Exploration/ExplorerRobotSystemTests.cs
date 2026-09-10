using Game.Data;
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
            float boundaryRamp = 40f)
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

            so.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        static DiscoveryRuntime NewDiscovery() => new DiscoveryRuntime(MapSize, ChunkSize);

        static ExplorerRobotSystem NewSystem(out DiscoveryRuntime discovery,
            ExplorerRobotSettings settings = null, int seed = Seed)
        {
            discovery = NewDiscovery();
            return new ExplorerRobotSystem(settings ?? NewSettings(), discovery,
                CoreCentre, CoreCentre, seed);
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

        [Test]
        public void ReturningUncoversNothingNew()
        {
            ExplorerRobotSystem system = NewSystem(out DiscoveryRuntime discovery);
            ExplorerRobotRuntime robot = system.Robots[0];

            // Far out, on ground nobody has seen, so anything revealed on the way back would show.
            robot.Position = CoreCentre + new Vector2(120f, 40f);
            robot.State = ExplorerRobotState.Returning;

            int version = discovery.Version;
            int discovered = discovery.DiscoveredCount();

            for (int i = 0; i < Mathf.RoundToInt(90f / Frame); i++) system.Tick(Frame);

            Assert.AreEqual(ExplorerRobotState.Idle, robot.State, "It should have got home inside 90 s.");
            Assert.AreEqual(version, discovery.Version, "The way back writes nothing at all.");
            Assert.AreEqual(discovered, discovery.DiscoveredCount());
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
                var system = new ExplorerRobotSystem(NewSettings(), NewDiscovery(), CoreCentre, CoreCentre, seed);
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
            var system = new ExplorerRobotSystem(null, NewDiscovery(), CoreCentre, CoreCentre, Seed);

            Assert.AreEqual(0, system.Robots.Count);
            Assert.DoesNotThrow(() => system.Tick(Frame));
            Assert.DoesNotThrow(() => system.Toggle(null));
            Assert.IsNull(system.At(CoreCentre));
        }
    }
}

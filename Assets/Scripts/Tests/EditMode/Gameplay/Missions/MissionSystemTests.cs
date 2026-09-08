using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Missions;
using Game.Gameplay.Sectors;
using Game.Grid;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Missions
{
    /// <summary>
    /// The expedition process, with no screen anywhere near it: a mission is launched by a call,
    /// advances with the clock, resolves and reports.
    ///
    /// Three properties carry the design and each fails quietly. The robot threshold must follow the
    /// reserve's cap, or it silently stops meaning what it meant - which has already happened twice.
    /// A mission saved in flight must land identically, or a reload rewrites the player's luck. And a
    /// player at zero CU with no production must still have a way up, or a run can be lost with no
    /// recourse and nothing says so.
    /// </summary>
    public class MissionSystemTests
    {
        const int MapSize = 10000;
        const int SectorSize = 16;
        const int ChunkSize = 64;
        const int Seed = 20260907;

        static readonly Vector2 CoreCenter = new Vector2(5000f, 5000f);

        /// <summary>The Core's current reach, handed to every launch. The mining band starts just outside it.</summary>
        const float CoreRadius = 22f;

        /// <summary>The shipped ceiling and gap, so the fixture's threshold is the shipped 154 cells.</summary>
        static SectorMissionRange NewRange() => new SectorMissionRange(32f, 90f);

        static MissionSettings NewSettings(
            int explorerRobotCount = 2,
            int missionsPerRobot = 10,
            int maxConcurrent = 2,
            int paidReconnaissances = 8,
            int paidRecoveries = 5,
            float regeneratingReward = 100f,
            float regeneratingCooldown = 600f)
        {
            var settings = ScriptableObject.CreateInstance<MissionSettings>();

            var so = new SerializedObject(settings);
            so.FindProperty("explorerRobotCount").intValue = explorerRobotCount;
            so.FindProperty("missionsPerRobot").intValue = missionsPerRobot;
            so.FindProperty("maxConcurrentMissions").intValue = maxConcurrent;
            so.FindProperty("paidReconnaissances").intValue = paidReconnaissances;
            so.FindProperty("paidRecoveries").intValue = paidRecoveries;
            so.FindProperty("regeneratingReward").floatValue = regeneratingReward;
            so.FindProperty("regeneratingCooldownSeconds").floatValue = regeneratingCooldown;
            so.FindProperty("secondsPerDistanceCell").floatValue = 0f;   // distance out of the way unless a test wants it
            so.ApplyModifiedPropertiesWithoutUndo();

            return settings;
        }

        sealed class Fixture
        {
            public MissionSettings Settings;
            public SectorGrid Grid;
            public DiscoveryRuntime Discovery;
            public SectorCatalog Catalog;
            public ComputeSystem Compute;
            public MissionSystem Missions;

            public void Destroy()
            {
                if (Settings != null) Object.DestroyImmediate(Settings);
            }
        }

        static Fixture NewFixture(MissionSettings settings = null)
        {
            var fixture = new Fixture { Settings = settings ?? NewSettings() };

            fixture.Grid = new SectorGrid(MapSize, SectorSize);
            fixture.Discovery = new DiscoveryRuntime(MapSize, ChunkSize);
            fixture.Catalog = new SectorCatalog(fixture.Grid, Seed, CoreCenter, 40f, 250f, 330f, 384);
            fixture.Compute = new ComputeSystem();
            fixture.Missions = new MissionSystem(fixture.Settings, fixture.Grid, fixture.Discovery,
                fixture.Catalog, fixture.Compute, NewRange(), Seed);

            return fixture;
        }

        /// <summary>Brings the robots out without caring how the reserve got there.</summary>
        static void SummonRobots(Fixture fixture)
        {
            fixture.Missions.Tick(0f, 0f, ComputeSystem.ReserveCap);
        }

        /// <summary>
        /// The nth distinct sector that actually sits in the mining band — between the Core's reach
        /// and the 154-cell threshold.
        ///
        /// It used to be a sector at a growing offset along one axis, which walked straight out of
        /// the band after a handful of steps. Nothing noticed while the launch path enforced no band;
        /// wiring it in turned four tests red at once, which is the seam doing its job.
        /// </summary>
        static int NearSector(Fixture fixture, int offset = 0)
        {
            var range = NewRange();
            int found = 0;

            // A ring of sector columns and rows around the Core, in a stable order.
            for (int row = -10; row <= 10; row++)
            {
                for (int column = -10; column <= 10; column++)
                {
                    int index = fixture.Grid.IndexAt(312 + column, 312 + row);
                    if (index < 0) continue;

                    float distance = Vector2.Distance(fixture.Grid.CenterCells(index), CoreCenter);
                    if (distance <= CoreRadius || distance > range.ExplorationMinimumCells) continue;

                    if (found == offset) return index;
                    found++;
                }
            }

            throw new System.InvalidOperationException($"no {offset}th sector in the mining band");
        }

        /// <summary>A sector in the mining band that a robot has already opened - what a recovery needs.</summary>
        static int ReconnoitredSector(Fixture fixture, int offset = 0)
        {
            int index = NearSector(fixture, offset);
            fixture.Grid.RevealInscribedDisc(index, fixture.Discovery);
            return index;
        }

        // ---- The threshold is a fraction ----

        /// <summary>
        /// The test the whole fraction exists for. The cap has gone 25 000 → 60 000 → 70 000 while an
        /// absolute threshold stayed put, turning an emergency trigger into an introduction trigger
        /// with nothing to signal it. Expressed as a fraction, it moves with the cap.
        /// </summary>
        [Test]
        public void MovingTheReserveCap_MovesTheRobotThreshold()
        {
            MissionSettings settings = NewSettings();

            float atShipped = settings.RobotThresholdCu(70000f);
            float atDouble = settings.RobotThresholdCu(140000f);
            float atOld = settings.RobotThresholdCu(25000f);

            Assert.AreEqual(25000f, atShipped, 1f, "0.357143 of the shipped 70 000 cap is 25 000");
            Assert.AreEqual(atShipped * 2f, atDouble, 1f, "doubling the cap must double the threshold");
            Assert.Less(atOld, atShipped, "and a smaller cap must lower it");

            Object.DestroyImmediate(settings);
        }

        [Test]
        public void RobotsArriveWhenTheReserveFallsBelowTheThreshold()
        {
            Fixture fixture = NewFixture();
            float threshold = fixture.Settings.RobotThresholdCu(ComputeSystem.ReserveCap);

            fixture.Missions.Tick(1f, threshold + 1f, ComputeSystem.ReserveCap);
            Assert.IsFalse(fixture.Missions.RobotsHaveAppeared);
            Assert.AreEqual(0, fixture.Missions.ExplorerRobotCount);

            fixture.Missions.Tick(1f, threshold - 1f, ComputeSystem.ReserveCap);
            Assert.IsTrue(fixture.Missions.RobotsHaveAppeared);
            Assert.AreEqual(2, fixture.Missions.ExplorerRobotCount);
            Assert.AreEqual(20, fixture.Missions.TotalChargesLeft, "two robots of ten missions each");

            fixture.Destroy();
        }

        /// <summary>
        /// The development bypass: robots on a full reserve, which the threshold alone would never do.
        /// It hands over the same fleet the trigger does - a debug shortcut that produced a different
        /// fleet would be testing something the game never runs.
        ///
        /// Idempotent, because it is applied once per Awake over a state that may already have them.
        /// </summary>
        [Test]
        public void MakeRobotsAppear_BringsThemOutOnAFullReserve_AndTwiceChangesNothing()
        {
            Fixture fixture = NewFixture();

            fixture.Missions.Tick(1f, ComputeSystem.ReserveCap, ComputeSystem.ReserveCap);
            Assert.IsFalse(fixture.Missions.RobotsHaveAppeared, "Precondition: a full reserve keeps them away.");

            fixture.Missions.MakeRobotsAppear();

            Assert.IsTrue(fixture.Missions.RobotsHaveAppeared);
            Assert.AreEqual(2, fixture.Missions.ExplorerRobotCount);
            Assert.AreEqual(20, fixture.Missions.TotalChargesLeft, "the same fleet the threshold hands over");

            fixture.Missions.MakeRobotsAppear();
            Assert.AreEqual(2, fixture.Missions.ExplorerRobotCount, "asking twice must not double the fleet");

            fixture.Destroy();
        }

        [Test]
        public void RobotsArriveOnce_AndDoNotComeBackWhenTheReserveRises()
        {
            Fixture fixture = NewFixture();

            SummonRobots(fixture);
            fixture.Missions.Tick(1f, ComputeSystem.ReserveCap, ComputeSystem.ReserveCap);

            Assert.AreEqual(2, fixture.Missions.ExplorerRobotCount);

            fixture.Destroy();
        }

        // ---- Launching ----

        [Test]
        public void NothingCanLaunchBeforeTheRobotsArrive()
        {
            Fixture fixture = NewFixture();

            Assert.AreEqual(MissionSystem.LaunchRefusal.RobotsHaveNotArrived,
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture), CoreRadius, out _));

            fixture.Destroy();
        }

        [Test]
        public void TwoMissionsRunAtOnce_AndAThirdIsRefused()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, 0), CoreRadius, out _));
            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, 2), CoreRadius, out _));

            Assert.AreEqual(MissionSystem.LaunchRefusal.AllSlotsBusy,
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, 4), CoreRadius, out _));

            Assert.AreEqual(2, fixture.Missions.InFlight.Count);

            fixture.Destroy();
        }

        [Test]
        public void LaunchingNeverCostsCu()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            fixture.Compute.Spend(fixture.Compute.Reserve);   // flat broke
            Assert.AreEqual(0f, fixture.Compute.Reserve, 0.001f);

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture), CoreRadius, out _),
                "a player at zero CU must still be able to send a mission - it is the whole exit");

            Assert.AreEqual(0f, fixture.Compute.Reserve, 0.001f, "and it must not have cost anything");

            fixture.Destroy();
        }

        [Test]
        public void ARobotSpendsOneChargePerMission_AndStopsWhenEmpty()
        {
            Fixture fixture = NewFixture(NewSettings(explorerRobotCount: 1, missionsPerRobot: 2, maxConcurrent: 1));
            SummonRobots(fixture);

            for (int i = 0; i < 2; i++)
            {
                Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                    fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, i * 2), CoreRadius, out MissionRuntime mission));
                RunToReport(fixture, mission);
            }

            Assert.AreEqual(0, fixture.Missions.TotalChargesLeft);
            Assert.AreEqual(MissionSystem.LaunchRefusal.NoRobotAvailable,
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, 8), CoreRadius, out _),
                "a robot does not die, it runs out - and a spent one cannot be sent");

            fixture.Destroy();
        }

        // ---- The seam: the launch path enforces the bands ----

        /// <summary>
        /// <b>The test this section exists for, and it is not a unit test.</b> `SectorMissionRange`
        /// was built, tested and documented, and for one brick `MissionSystem` never called it: both
        /// halves were right and the seam between them did not exist. No test of either half could
        /// see that - which is why this one starts from `TryLaunch`, the path the game actually uses,
        /// rather than from the predicate it delegates to.
        /// </summary>
        [Test]
        public void AProspectionBeyondTheThreshold_IsRefused()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            // 400 cells out: well past the 154-cell threshold, so this is exploration's ground.
            int far = fixture.Grid.IndexAt(312 + 25, 312);

            Assert.AreEqual(MissionSystem.LaunchRefusal.WrongBand,
                fixture.Missions.TryLaunch(MissionKind.Prospection, far, CoreRadius, out MissionRuntime refused),
                "a prospection was allowed across the exploration threshold");
            Assert.IsNull(refused);

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.ExplorationLointaine, far, CoreRadius, out _),
                "and the same sector is exactly where a far exploration belongs");

            fixture.Destroy();
        }

        [Test]
        public void AnExplorationInsideTheThreshold_IsRefused()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            Assert.AreEqual(MissionSystem.LaunchRefusal.WrongBand,
                fixture.Missions.TryLaunch(MissionKind.ExplorationLointaine, NearSector(fixture), CoreRadius, out _));

            fixture.Destroy();
        }

        [Test]
        public void AProspectionInsideTheCoresReach_IsRefused()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            // One sector off centre: inside a radius of 22, so the Core already sees it.
            int home = fixture.Grid.IndexAt(312, 312);

            Assert.AreEqual(MissionSystem.LaunchRefusal.WrongBand,
                fixture.Missions.TryLaunch(MissionKind.Prospection, home, CoreRadius, out _));

            fixture.Destroy();
        }

        /// <summary>Extending the Core's reach closes the mining band from the inside, and the launch path has to follow - the radius is passed on every call precisely so it cannot go stale.</summary>
        [Test]
        public void ExtendingTheReach_ClosesTheBandOnTheLaunchPathToo()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int near = fixture.Grid.IndexAt(312 + 3, 312);   // ~48 cells out

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.CanLaunch(MissionKind.Prospection, near, 22f));

            Assert.AreEqual(MissionSystem.LaunchRefusal.WrongBand,
                fixture.Missions.CanLaunch(MissionKind.Prospection, near, 80f),
                "inside a reach of 80 there is nothing left for a mission to find there");

            fixture.Destroy();
        }

        [Test]
        public void AReconnaissanceOfGroundAlreadySeen_IsRefused()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int sector = NearSector(fixture);
            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.CanLaunch(MissionKind.Prospection, sector, CoreRadius));

            fixture.Grid.RevealInscribedDisc(sector, fixture.Discovery);

            Assert.AreEqual(MissionSystem.LaunchRefusal.AlreadyReconnoitred,
                fixture.Missions.CanLaunch(MissionKind.Prospection, sector, CoreRadius),
                "a mission there would reveal a disc that is already revealed");

            fixture.Destroy();
        }

        /// <summary>A recovery is the mirror of a reconnaissance: it needs ground already opened, and a point of interest in it.</summary>
        [Test]
        public void ARecoveryNeedsGroundAlreadyOpened()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int sector = NearSector(fixture);

            Assert.AreEqual(MissionSystem.LaunchRefusal.NotYetReconnoitred,
                fixture.Missions.CanLaunch(MissionKind.Recuperation, sector, CoreRadius),
                "the player cannot exploit what no robot has reported on");

            fixture.Grid.RevealInscribedDisc(sector, fixture.Discovery);

            Assert.AreNotEqual(MissionSystem.LaunchRefusal.NotYetReconnoitred,
                fixture.Missions.CanLaunch(MissionKind.Recuperation, sector, CoreRadius));

            fixture.Destroy();
        }

        /// <summary>A recovery has no band at all, so the range must say so rather than answering with one.</summary>
        [Test]
        public void ARecoveryIsNotABandMission()
        {
            Fixture fixture = NewFixture();

            Assert.AreEqual(SectorEligibility.NotABandMission,
                NewRange().EligibilityOf(MissionKind.Recuperation, fixture.Grid, fixture.Discovery,
                    CoreCenter, CoreRadius, NearSector(fixture)));

            fixture.Destroy();
        }

        /// <summary>
        /// No adjacency rule, and this pins that it stays absent. The specification recommended one
        /// (§10) and the two bands replaced it: constraining a target to touch known ground *as well
        /// as* sit in the right band would constrain the same thing twice, and turn exploration into a
        /// concentric crawl - the opposite of what six secondary Core sites at the threshold assume.
        /// Distance already costs travel time.
        /// </summary>
        [Test]
        public void AFarSectorTouchingNothingKnown_IsStillAValidTarget()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            // Right across the map from the Core, touching no revealed ground whatsoever.
            int elsewhere = fixture.Grid.IndexAt(600, 600);

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.CanLaunch(MissionKind.ExplorationLointaine, elsewhere, CoreRadius),
                "adjacency was deliberately not adopted - see SPEC_EXPEDITIONS.md §10");

            fixture.Destroy();
        }

        // ---- The state machine ----

        [Test]
        public void AMissionWalksTheStatesInOrder()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);
            fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture), CoreRadius, out MissionRuntime mission);

            Assert.AreEqual(MissionState.EnRoute, mission.State);

            fixture.Missions.Tick(mission.TotalSeconds * 0.5f, 0f, ComputeSystem.ReserveCap);
            Assert.AreEqual(MissionState.Resolution, mission.State, "half way is when the data arrives");

            fixture.Missions.Tick(1f, 0f, ComputeSystem.ReserveCap);
            Assert.AreEqual(MissionState.Retour, mission.State);

            fixture.Missions.Tick(mission.TotalSeconds, 0f, ComputeSystem.ReserveCap);
            Assert.AreEqual(MissionState.Rapport, mission.State);

            Assert.AreEqual(1, fixture.Missions.Reports.Count);
            Assert.AreEqual(0, fixture.Missions.InFlight.Count);

            fixture.Destroy();
        }

        [Test]
        public void WhileEnRoute_OnlyTheTimeRemainingIsKnown()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);
            fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture), CoreRadius, out MissionRuntime mission);

            float before = mission.RemainingSeconds;
            fixture.Missions.Tick(30f, 0f, ComputeSystem.ReserveCap);

            Assert.AreEqual(before - 30f, mission.RemainingSeconds, 0.01f);
            Assert.AreEqual(MissionState.EnRoute, mission.State);

            fixture.Destroy();
        }

        // ---- The revelation ----

        /// <summary>
        /// The disc inscribed in the square, never the square: the four corners stay in the fog, which
        /// is what keeps the tiling from ever showing on screen. The mission asks SectorGrid for it
        /// rather than re-implementing the shape.
        /// </summary>
        [Test]
        public void AReconnaissanceRevealsTheInscribedDisc_NotTheSquare()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int sector = NearSector(fixture);
            fixture.Missions.TryLaunch(MissionKind.Prospection, sector, CoreRadius, out MissionRuntime mission);

            Assert.AreEqual(SectorDiscovery.Unknown, fixture.Grid.DiscoveryOf(sector, fixture.Discovery));

            RunToReport(fixture, mission);

            Assert.AreEqual(SectorDiscovery.Partial, fixture.Grid.DiscoveryOf(sector, fixture.Discovery),
                "Partial is the resting state of a sector opened by a mission - the disc cannot cover the corners");

            fixture.Destroy();
        }

        /// <summary>
        /// <b>Nothing reaches the Core while the robot is out.</b> The Core cannot communicate beyond
        /// its own action radius - it is blind and mute out there, which is why expeditions exist at
        /// all - so a robot in the field has nobody to transmit to and carries its data home.
        ///
        /// This is what makes §6's "the player sees nothing of the progress" a fact of the world
        /// rather than a rule of the interface, and it is why the map must not move at the halfway
        /// point. It did, until this test was written.
        /// </summary>
        [Test]
        public void TheMapDoesNotMoveWhileTheRobotIsStillOut()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int sector = NearSector(fixture);
            fixture.Missions.TryLaunch(MissionKind.Prospection, sector, CoreRadius, out MissionRuntime mission);

            float reserveAtLaunch = fixture.Compute.Reserve;

            // Right through resolution and the whole way home, one step short of docking.
            for (int i = 0; i < 9; i++)
            {
                fixture.Missions.Tick(mission.TotalSeconds * 0.1f, 0f, ComputeSystem.ReserveCap);

                Assert.AreEqual(SectorDiscovery.Unknown, fixture.Grid.DiscoveryOf(sector, fixture.Discovery),
                    $"the map moved at {(i + 1) * 10}% of the trip - the Core cannot hear a robot outside its radius");
                Assert.AreEqual(reserveAtLaunch, fixture.Compute.Reserve, 0.001f, "and nothing was paid before it docked");
            }

            Assert.AreNotEqual(MissionState.Rapport, mission.State, "the fixture was supposed to stop short of docking");

            fixture.Missions.Tick(mission.TotalSeconds, 0f, ComputeSystem.ReserveCap);

            Assert.AreEqual(MissionState.Rapport, mission.State);
            Assert.AreEqual(SectorDiscovery.Partial, fixture.Grid.DiscoveryOf(sector, fixture.Discovery),
                "everything the robot carried lands at once, when it is back inside the radius");

            fixture.Destroy();
        }

        /// <summary>With a robot the map cannot fail (§7.1). Only a harvest can, and only a recovery harvests.</summary>
        [Test]
        public void WithARobot_TheMapNeverFails()
        {
            Fixture fixture = NewFixture(NewSettings(explorerRobotCount: 1, missionsPerRobot: 30, maxConcurrent: 1));
            SummonRobots(fixture);

            for (int i = 0; i < 20; i++)
            {
                int sector = NearSector(fixture, i * 2);
                fixture.Missions.TryLaunch(MissionKind.Prospection, sector, CoreRadius, out MissionRuntime mission);
                RunToReport(fixture, mission);

                Assert.AreNotEqual(SectorDiscovery.Unknown, fixture.Grid.DiscoveryOf(sector, fixture.Discovery),
                    $"mission {i} came back blind, which a robot cannot do");
            }

            fixture.Destroy();
        }

        // ---- Surviving a save ----

        /// <summary>
        /// The test the launch-time draw exists for: a mission saved mid-flight must land exactly as it
        /// would have. Neither redrawn - which would let a player reload for a better result - nor
        /// lost.
        /// </summary>
        [Test]
        public void AMissionSavedInFlight_LandsIdentically()
        {
            Fixture original = NewFixture();
            SummonRobots(original);

            int sector = ReconnoitredSector(original);
            original.Missions.TryLaunch(MissionKind.Recuperation, sector, CoreRadius, out MissionRuntime flying);

            original.Missions.Tick(flying.TotalSeconds * 0.25f, 0f, ComputeSystem.ReserveCap);
            JObject saved = original.Missions.CaptureState();

            // Finish it in the original.
            RunToReport(original, flying);
            MissionOutcome expectedOutcome = flying.Outcome;
            float expectedReward = flying.RewardCu;
            float expectedReserve = original.Compute.Reserve;

            // And finish the same mission in a world restored from the save.
            Fixture reloaded = NewFixture();
            reloaded.Missions.RestoreState(saved);

            Assert.AreEqual(1, reloaded.Missions.InFlight.Count, "the mission was lost across the save");
            MissionRuntime restored = reloaded.Missions.InFlight[0];

            Assert.AreEqual(flying.Id, restored.Id);
            Assert.AreEqual(expectedOutcome, restored.Outcome, "the outcome was redrawn across the save");
            Assert.AreEqual(expectedReward, restored.RewardCu, 0.001f);

            RunToReport(reloaded, restored);

            Assert.AreEqual(expectedReserve, reloaded.Compute.Reserve, 0.001f,
                "the same mission paid a different amount after a reload");

            original.Destroy();
            reloaded.Destroy();
        }

        [Test]
        public void ChargesAndConsumedSitesSurviveASave()
        {
            Fixture original = NewFixture();
            SummonRobots(original);

            original.Missions.TryLaunch(MissionKind.Prospection, NearSector(original), CoreRadius, out MissionRuntime mission);
            RunToReport(original, mission);

            int chargesBefore = original.Missions.TotalChargesLeft;
            JObject saved = original.Missions.CaptureState();

            Fixture reloaded = NewFixture();
            reloaded.Missions.RestoreState(saved);

            Assert.IsTrue(reloaded.Missions.RobotsHaveAppeared);
            Assert.AreEqual(chargesBefore, reloaded.Missions.TotalChargesLeft);

            original.Destroy();
            reloaded.Destroy();
        }

        [Test]
        public void RestoringNothing_IsAGameWhoseRobotsHaveNotArrived()
        {
            Fixture fixture = NewFixture();

            Assert.DoesNotThrow(() => fixture.Missions.RestoreState(null));
            Assert.IsFalse(fixture.Missions.RobotsHaveAppeared);
            Assert.AreEqual(0, fixture.Missions.ExplorerRobotCount);

            fixture.Destroy();
        }

        // ---- The introduction's finite gisement ----

        [Test]
        public void TheIntroductionsRewardBudgetIsFinite()
        {
            Fixture fixture = NewFixture(NewSettings(explorerRobotCount: 1, missionsPerRobot: 40, maxConcurrent: 1,
                paidReconnaissances: 3, regeneratingReward: 0f));
            SummonRobots(fixture);

            fixture.Compute.Spend(fixture.Compute.Reserve);

            for (int i = 0; i < 3; i++)
            {
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, i * 2), CoreRadius, out MissionRuntime paid);
                RunToReport(fixture, paid);
            }

            float afterBudget = fixture.Compute.Reserve;
            Assert.AreEqual(3 * 500f, afterBudget, 1f, "three paid reconnaissances at 500 CU");

            fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, 20), CoreRadius, out MissionRuntime free);
            RunToReport(fixture, free);

            Assert.AreEqual(afterBudget, fixture.Compute.Reserve, 0.001f,
                "past the budget a reconnaissance still reveals the map but stops paying - otherwise the "
                + "mining band of a 10 000-cell map is a currency fountain");

            fixture.Destroy();
        }

        /// <summary>
        /// The property that makes a run unloseable, and the reason §8 demands a regenerating site: a
        /// player at zero CU with no production still has an action that pays. Without it, a run can
        /// end in a state nothing announces.
        /// </summary>
        [Test]
        public void APlayerAtZeroCu_WithNoProduction_CanClimbBackOut()
        {
            Fixture fixture = NewFixture(NewSettings(explorerRobotCount: 1, missionsPerRobot: 40, maxConcurrent: 1,
                paidReconnaissances: 0, paidRecoveries: 0, regeneratingCooldown: 60f));
            SummonRobots(fixture);

            fixture.Compute.Spend(fixture.Compute.Reserve);
            Assert.AreEqual(0f, fixture.Compute.Reserve, 0.001f);

            float previous = 0f;
            for (int i = 0; i < 3; i++)
            {
                fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, i * 2), CoreRadius, out MissionRuntime mission);
                Assert.IsNotNull(mission, $"launch {i} was refused, so there is no way out at all");

                RunToReport(fixture, mission);

                Assert.Greater(fixture.Compute.Reserve, previous,
                    $"round {i} paid nothing - a player at zero with no production is stuck");
                previous = fixture.Compute.Reserve;
            }

            fixture.Destroy();
        }

        [Test]
        public void ARecoveredSite_CannotBeRecoveredTwice()
        {
            Fixture fixture = NewFixture(NewSettings(explorerRobotCount: 1, missionsPerRobot: 10, maxConcurrent: 1));
            SummonRobots(fixture);

            int sector = ReconnoitredSector(fixture);

            // Repeat until one actually succeeds - a missed harvest does not consume the site.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (fixture.Missions.CanLaunch(MissionKind.Recuperation, sector, CoreRadius) != MissionSystem.LaunchRefusal.None) break;

                fixture.Missions.TryLaunch(MissionKind.Recuperation, sector, CoreRadius, out MissionRuntime mission);
                RunToReport(fixture, mission);
            }

            Assert.AreEqual(MissionSystem.LaunchRefusal.AlreadyRecovered,
                fixture.Missions.CanLaunch(MissionKind.Recuperation, sector, CoreRadius),
                "the gisement is finite: a recovered site is not offered again");

            fixture.Destroy();
        }

        // ---- Crew ----

        /// <summary>Units do not exist, so crew is 1 everywhere - but the parameter is carried so the model does not have to be unpicked when squads arrive.</summary>
        [Test]
        public void CrewIsCarriedAtOne()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture), CoreRadius, out MissionRuntime mission);
            Assert.AreEqual(1, mission.Crew);

            fixture.Destroy();
        }

        // ---- Duration ----

        [Test]
        public void AFartherTargetTakesLonger()
        {
            var settings = NewSettings();
            var so = new SerializedObject(settings);
            so.FindProperty("secondsPerDistanceCell").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Fixture fixture = NewFixture(settings);

            float near = fixture.Missions.DurationOf(MissionKind.Prospection, fixture.Grid.IndexAt(315, 312));
            float far = fixture.Missions.DurationOf(MissionKind.Prospection, fixture.Grid.IndexAt(360, 312));

            Assert.Greater(far, near, "distance is what separates the two reconnaissances in the playing of them");

            fixture.Destroy();
        }

        // ---- Helpers ----

        /// <summary>Runs the clock until a mission has landed and its report is ready.</summary>
        static void RunToReport(Fixture fixture, MissionRuntime mission)
        {
            for (int guard = 0; guard < 1000 && mission.State != MissionState.Rapport; guard++)
            {
                fixture.Missions.Tick(mission.TotalSeconds * 0.25f, 0f, ComputeSystem.ReserveCap);
            }

            Assert.AreEqual(MissionState.Rapport, mission.State, "the mission never landed");
            fixture.Missions.CloseReport(mission);
        }
    }
}

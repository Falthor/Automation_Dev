using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Expeditions;
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

            /// <summary>Non-null only for the tests about the zone gate. Kept so they can be destroyed with the rest.</summary>
            public ExpeditionZoneSettings ZoneSettings;

            public SectorGrid Grid;
            public DiscoveryRuntime Discovery;
            public SectorCatalog Catalog;
            public ComputeSystem Compute;
            public ExpeditionZoneSystem Zones;
            public MissionSystem Missions;

            public void Destroy()
            {
                if (Settings != null) Object.DestroyImmediate(Settings);
                if (ZoneSettings != null) Object.DestroyImmediate(ZoneSettings);
            }
        }

        /// <summary>
        /// <paramref name="withZones"/> is what separates the tests about the process from the tests
        /// about the zone gate. Without it there is no zone rule at all, which is the shape every test
        /// written before the zones existed assumes - and the shape a headless test of the process
        /// itself still wants.
        /// </summary>
        static Fixture NewFixture(MissionSettings settings = null, bool withZones = false)
        {
            var fixture = new Fixture { Settings = settings ?? NewSettings() };

            fixture.Grid = new SectorGrid(MapSize, SectorSize);
            fixture.Discovery = new DiscoveryRuntime(MapSize, ChunkSize);
            fixture.Catalog = new SectorCatalog(fixture.Grid, Seed, CoreCenter, 40f, 250f, 330f, 384);
            fixture.Compute = new ComputeSystem();

            if (withZones)
            {
                fixture.ZoneSettings = ScriptableObject.CreateInstance<ExpeditionZoneSettings>();
                fixture.Zones = new ExpeditionZoneSystem(fixture.ZoneSettings, fixture.Grid, NewRange(),
                    CoreCenter, CoreRadius, Seed);
            }

            fixture.Missions = new MissionSystem(fixture.Settings, fixture.Grid, fixture.Discovery,
                fixture.Catalog, fixture.Compute, NewRange(), fixture.Zones, Seed);

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

        /// <summary>
        /// The first mining-band sector that is still wholly unknown, optionally inside one zone.
        ///
        /// <b>A moving set, and that is the point.</b> A mission's trail opens the ground it crossed, so
        /// sectors stop being reconnaissance targets as the map fills in - a fixed offset walks into
        /// ground a previous trip already opened and is refused. Anything launching repeatedly has to
        /// ask again rather than count.
        /// </summary>
        static int UnknownSector(Fixture fixture, int zone = -1)
        {
            var range = NewRange();

            for (int row = -12; row <= 12; row++)
            {
                for (int column = -12; column <= 12; column++)
                {
                    int index = fixture.Grid.IndexAt(312 + column, 312 + row);
                    if (index < 0) continue;

                    float distance = Vector2.Distance(fixture.Grid.CenterCells(index), CoreCenter);
                    if (distance <= CoreRadius || distance > range.ExplorationMinimumCells) continue;
                    if (!fixture.Grid.IsWhollyUnknown(index, fixture.Discovery)) continue;
                    if (zone >= 0 && fixture.Zones.ZoneOfSector(index) != zone) continue;

                    return index;
                }
            }

            throw new System.InvalidOperationException("no wholly unknown sector left in the mining band");
        }

        /// <summary>The nth sector of the mining band that falls in a given expedition zone, in a stable order.</summary>
        static int SectorInZone(Fixture fixture, int zone, int offset = 0)
        {
            var range = NewRange();
            int found = 0;

            for (int row = -12; row <= 12; row++)
            {
                for (int column = -12; column <= 12; column++)
                {
                    int index = fixture.Grid.IndexAt(312 + column, 312 + row);
                    if (index < 0) continue;

                    float distance = Vector2.Distance(fixture.Grid.CenterCells(index), CoreCenter);
                    if (distance <= CoreRadius || distance > range.ExplorationMinimumCells) continue;
                    if (fixture.Zones.ZoneOfSector(index) != zone) continue;

                    if (found == offset) return index;
                    found++;
                }
            }

            throw new System.InvalidOperationException($"no {offset}th mining-band sector in zone {zone}");
        }

        // ---- The zone gate, entered where the game enters it ----

        /// <summary>
        /// <b>Started from TryLaunch, and that is the whole design of this test.</b> Asking
        /// ExpeditionZoneSystem whether it would refuse proves only that the predicate is right; the
        /// defect this project has met four times is a predicate nobody calls. The launch path is the
        /// only place that can be wrong about that.
        ///
        /// <b>The first launch is the choice.</b> There is no separate gesture and no state where the
        /// map is readable but sterile: sending the first robot somewhere is what picks that direction
        /// and locks the other five.
        /// </summary>
        [Test]
        public void TheFirstLaunch_ChoosesTheZone_AndLocksTheOtherFive()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);

            int target = SectorInZone(fixture, 2);
            Assert.AreEqual(-1, fixture.Zones.ChosenZone, "precondition: the six are still on offer");
            Assert.IsTrue(fixture.Zones.WouldChoose(target), "and this launch is about to commit one");

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime mission));

            Assert.IsNotNull(mission);
            Assert.AreEqual(2, fixture.Zones.ChosenZone, "the launch chose the zone it went into");

            for (int zone = 0; zone < fixture.Zones.ZoneCount; zone++)
            {
                Assert.AreEqual(zone == 2, fixture.Zones.IsAvailable(zone, fixture.Discovery), $"zone {zone}");
            }

            fixture.Destroy();
        }

        /// <summary>
        /// A refused launch must not cost the player their five other directions. The lock is
        /// irreversible, so it is committed only once the launch is known to go - which is why
        /// ChooseByLaunch sits after the refusal check and not inside it.
        /// </summary>
        [Test]
        public void ARefusedLaunch_ChoosesNothing()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);

            // Inside the Core's own reach: ground no zone covers, so no zone can be chosen by aiming there.
            int tooClose = fixture.Grid.IndexAt(312, 312);

            Assert.AreNotEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, tooClose, CoreRadius, out MissionRuntime refused));
            Assert.IsNull(refused);
            Assert.AreEqual(-1, fixture.Zones.ChosenZone, "a refusal must leave the six on offer");

            fixture.Destroy();
        }

        /// <summary>
        /// Choosing locks the other five, and the lock is only worth anything where a mission is
        /// actually sent. The same sector that goes now would have gone before the choice too - what
        /// changed is the sector in the neighbouring slice, which is refused.
        /// </summary>
        [Test]
        public void ChoosingAZone_LocksTheOtherFive_AndALaunchOutsideItIsRefused()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);

            int inside = SectorInZone(fixture, 0);
            int outside = SectorInZone(fixture, 3);

            Assert.AreEqual(ZoneChoiceRefusal.None, fixture.Zones.Choose(0, fixture.Discovery));
            Assert.IsTrue(fixture.Zones.IsAvailable(0, fixture.Discovery));
            for (int zone = 1; zone < fixture.Zones.ZoneCount; zone++)
            {
                Assert.IsFalse(fixture.Zones.IsAvailable(zone, fixture.Discovery), $"zone {zone} must be locked");
            }

            Assert.AreEqual(MissionSystem.LaunchRefusal.OutsideChosenZone,
                fixture.Missions.TryLaunch(MissionKind.Prospection, outside, CoreRadius, out MissionRuntime refused));
            Assert.IsNull(refused);

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, inside, CoreRadius, out MissionRuntime sent));
            Assert.IsNotNull(sent);
            Assert.AreEqual(inside, sent.TargetSector);

            fixture.Destroy();
        }

        /// <summary>A recovery is bound by the zone too - a run works one zone at a time, whatever kind is aimed at it. Checked separately because a gate placed inside a kind's own branch would pass the test above and miss this one entirely.</summary>
        [Test]
        public void ARecoveryOutsideTheChosenZone_IsRefusedLikeAnyOtherMission()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            int outside = SectorInZone(fixture, 3);
            fixture.Grid.RevealInscribedDisc(outside, fixture.Discovery);

            Assert.AreEqual(MissionSystem.LaunchRefusal.OutsideChosenZone,
                fixture.Missions.TryLaunch(MissionKind.Recuperation, outside, CoreRadius, out _));

            fixture.Destroy();
        }

        // ---- The field study ----

        /// <summary>
        /// It is aimed exactly like a prospection - same band, same designation - so the only thing the
        /// launch path needed was the kind. Started from TryLaunch, because a band shared in
        /// <c>BandFor</c> and not reached from here would be the same unwired seam as before.
        /// </summary>
        [Test]
        public void AFieldStudy_IsLaunchedIntoTheMiningBand_LikeAProspection()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.EtudeDeTerrain, NearSector(fixture), CoreRadius, out MissionRuntime mission));
            Assert.IsNotNull(mission);

            // And refused past the threshold, where only a far reconnaissance may go.
            int beyond = fixture.Grid.IndexAt(312 + 14, 312);
            Assert.AreEqual(MissionSystem.LaunchRefusal.WrongBand,
                fixture.Missions.TryLaunch(MissionKind.EtudeDeTerrain, beyond, CoreRadius, out _));

            fixture.Destroy();
        }

        /// <summary>Two minutes and 250 CU, and it spends neither of the introduction's two budgets - so a run's eight paid reconnaissances are still eight after one.</summary>
        [Test]
        public void AFieldStudy_IsShortCheap_AndSpendsNeitherBudget()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            Assert.AreEqual(120f, fixture.Missions.DurationOf(MissionKind.EtudeDeTerrain, NearSector(fixture)), 0.001f);

            fixture.Missions.TryLaunch(MissionKind.EtudeDeTerrain, NearSector(fixture), CoreRadius, out MissionRuntime study);
            Assert.AreEqual(250f, study.RewardCu, 0.001f);

            // The budget is untouched: a prospection launched afterwards still gets the full 500.
            fixture.Missions.TryLaunch(MissionKind.Prospection, NearSector(fixture, 1), CoreRadius, out MissionRuntime prospection);
            Assert.AreEqual(500f, prospection.RewardCu, 0.001f, "the field study must not have eaten a paid reconnaissance");

            fixture.Destroy();
        }

        /// <summary>
        /// <b>The stock's first consumer.</b> RevealNextHiddenSite was built, tested and documented with
        /// nothing calling it - the fifth time this project has met that shape. A study that drew a find
        /// turns one up when it lands; one that did not, does not.
        /// </summary>
        [Test]
        public void AFieldStudyThatDrewAFind_TurnsUpAHiddenSite_WhenItLands()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            MissionRuntime study = LaunchStudyUntilItDrawsAFind(fixture, wantsFind: true);
            Assert.IsTrue(study.RevealsHiddenSite);

            // Read after the helper, never before: the attempts it discarded land too.
            int before = fixture.Zones.HiddenSitesLeft(0);
            Assert.Greater(before, 0, "precondition: the zone still holds a stock");

            Assert.AreEqual(before, fixture.Zones.HiddenSitesLeft(0), "nothing is found while the robot is still out");

            RunToReport(fixture, study);

            Assert.AreEqual(before - 1, fixture.Zones.HiddenSitesLeft(0), "one turned up at the docking");

            fixture.Destroy();
        }

        /// <summary>A study that drew no find leaves the stock alone - so the one in three is a draw and not a formality.</summary>
        [Test]
        public void AFieldStudyThatDrewNothing_LeavesTheStockAlone()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            MissionRuntime study = LaunchStudyUntilItDrawsAFind(fixture, wantsFind: false);
            Assert.IsFalse(study.RevealsHiddenSite);

            int before = fixture.Zones.HiddenSitesLeft(0);
            RunToReport(fixture, study);

            // The negative assertion below is satisfied by two different worlds: one where the study
            // landed and found nothing, and one where it never landed at all. So the positive comes
            // first - this is the shape that let a single oversized Tick pass for a working test.
            // Close rather than Rapport: RunToReport lands it and reads the report, and it is that
            // whole path - the one that would have revealed a site - which has to have run.
            Assert.AreEqual(MissionState.Close, study.State, "precondition: it actually came home and delivered");

            Assert.AreEqual(before, fixture.Zones.HiddenSitesLeft(0));

            fixture.Destroy();
        }

        /// <summary>Once the stock is spent a study finds only ground - which is what keeps a zone bounded however many are launched.</summary>
        [Test]
        public void OnceTheStockIsSpent_AFieldStudyFindsOnlyGround()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            while (fixture.Zones.RevealNextHiddenSite(0) != null) { }
            Assert.AreEqual(0, fixture.Zones.HiddenSitesLeft(0), "precondition: emptied");

            MissionRuntime study = LaunchStudyUntilItDrawsAFind(fixture, wantsFind: true);
            RunToReport(fixture, study);

            Assert.AreEqual(0, fixture.Zones.HiddenSitesLeft(0), "an exhausted stock stays exhausted, and nothing throws");

            fixture.Destroy();
        }

        /// <summary>The find is drawn at launch and carried, like the outcome and the reward - so a reload cannot re-roll it.</summary>
        [Test]
        public void TheFind_IsDrawnAtLaunch_AndSurvivesASave()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            MissionRuntime study = LaunchStudyUntilItDrawsAFind(fixture, wantsFind: true);
            JObject captured = fixture.Missions.CaptureState();

            Fixture reloaded = NewFixture(withZones: true);
            reloaded.Missions.RestoreState(captured);

            Assert.AreEqual(1, reloaded.Missions.InFlight.Count);
            Assert.IsTrue(reloaded.Missions.InFlight[0].RevealsHiddenSite, "a reload must not lose the find");
            Assert.AreEqual(study.Id, reloaded.Missions.InFlight[0].Id);

            fixture.Destroy();
            reloaded.Destroy();
        }

        /// <summary>Launches field studies until one draws the wanted answer, and returns it. The draw is a pure function of the mission id, so this walks ids rather than re-rolling anything.</summary>
        static MissionRuntime LaunchStudyUntilItDrawsAFind(Fixture fixture, bool wantsFind)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                // A fresh sector each time, asked for rather than counted: the study that just ran home
                // opened its target *and the ground it crossed*, so which sectors are still virgin is a
                // set that moves under the test.
                MissionSystem.LaunchRefusal refusal = fixture.Missions.TryLaunch(
                    MissionKind.EtudeDeTerrain, UnknownSector(fixture, 0), CoreRadius, out MissionRuntime mission);

                if (refusal != MissionSystem.LaunchRefusal.None) throw new System.InvalidOperationException($"refused: {refusal}");
                if (mission.RevealsHiddenSite == wantsFind) return mission;

                // Not the draw wanted: run it home so a slot frees up, and try the next id. Note that a
                // discarded attempt lands like any other - which is why a caller reads the stock after
                // this returns, never before.
                RunToReport(fixture, mission);
            }

            throw new System.InvalidOperationException($"no field study drew RevealsHiddenSite={wantsFind} in 20 tries");
        }

        // ---- The trail ----

        /// <summary>
        /// <b>The trail is discovery, so there is nothing to draw and nothing to store.</b> The ground
        /// between the Core and the target is opened when the robot docks, and the map's terrain shows
        /// it with everything else - which is what turns a set of floating discs into a star.
        /// </summary>
        [Test]
        public void AMissionThatDocks_OpensTheGroundBetweenTheCoreAndItsTarget()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int target = NearSector(fixture, 6);
            Vector2 targetCentre = fixture.Grid.CenterCells(target);
            var midway = new GridCoord(
                Mathf.RoundToInt((CoreCenter.x + targetCentre.x) * 0.5f),
                Mathf.RoundToInt((CoreCenter.y + targetCentre.y) * 0.5f));

            Assert.IsFalse(fixture.Discovery.IsDiscovered(midway), "precondition: the way there is unknown");

            fixture.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime mission);
            RunToReport(fixture, mission);

            // The band bends, so the exact midpoint may sit just off it - what matters is that the
            // ground around the halfway mark opened.
            Assert.IsTrue(DiscoveredNear(fixture, midway, 20),
                "nothing was opened halfway - a robot crossed the map without seeing any of it");

            fixture.Destroy();
        }

        /// <summary>
        /// Two missions to the same place follow the same path, which is what makes going back reveal
        /// almost nothing and a fresh direction worth more. Measured on what the second actually opened,
        /// because that is the property - not on the path, which nothing exposes.
        /// </summary>
        [Test]
        public void ASecondMissionToTheSameTarget_OpensAlmostNothingOnTheWay()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int target = NearSector(fixture, 6);

            fixture.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime first);
            RunToReport(fixture, first);
            int afterFirst = fixture.Discovery.DiscoveredCount();

            // A recovery, so the target being known already is not a refusal - and it travels the same
            // road.
            fixture.Missions.TryLaunch(MissionKind.Recuperation, target, CoreRadius, out MissionRuntime second);
            RunToReport(fixture, second);

            Assert.AreEqual(afterFirst, fixture.Discovery.DiscoveredCount(),
                "the second trip opened new ground - the two are not following one road");

            fixture.Destroy();
        }

        /// <summary>
        /// The path comes from the seed and the target and from nothing else, so a world rebuilt from
        /// the same seed opens exactly the same ground. Pinned on the count, since the path itself is
        /// deliberately not exposed.
        /// </summary>
        [Test]
        public void TheTrail_IsTheSameInTwoWorldsOfTheSameSeed()
        {
            Fixture first = NewFixture();
            Fixture second = NewFixture();
            SummonRobots(first);
            SummonRobots(second);

            int target = NearSector(first, 4);

            first.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime a);
            RunToReport(first, a);

            second.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime b);
            RunToReport(second, b);

            Assert.AreEqual(first.Discovery.DiscoveredCount(), second.Discovery.DiscoveredCount(),
                "the same seed and the same target must open the same ground");

            first.Destroy();
            second.Destroy();
        }

        /// <summary>
        /// The band is a third of the arrival disc's width, derived from it rather than written beside
        /// it. Measured across the trail near the Core, where the disc is not there to widen it.
        /// </summary>
        [Test]
        public void TheTrail_IsNarrowerThanTheDiscItLeadsTo()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int target = NearSector(fixture, 6);
            fixture.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime mission);
            RunToReport(fixture, mission);

            int trailWidth = WidthAcross(fixture, Mathf.RoundToInt(CoreCenter.y));
            int discWidth = WidthAcross(fixture, Mathf.RoundToInt(fixture.Grid.CenterCells(target).y));

            Assert.Greater(trailWidth, 0, "the trail has to exist to be measured");
            Assert.Less(trailWidth, discWidth, "the trail must be narrower than the disc it leads to");

            fixture.Destroy();
        }

        /// <summary>Nothing is opened while the robot is still out - the trail lands with everything else, at the docking.</summary>
        [Test]
        public void TheTrail_IsNotOpenedUntilTheRobotIsHome()
        {
            Fixture fixture = NewFixture();
            SummonRobots(fixture);

            int target = NearSector(fixture, 6);
            int before = fixture.Discovery.DiscoveredCount();

            fixture.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime mission);
            fixture.Missions.Tick(mission.TotalSeconds * 0.4f, 0f, ComputeSystem.ReserveCap);

            Assert.AreNotEqual(MissionState.Rapport, mission.State, "precondition: still out");
            Assert.AreEqual(before, fixture.Discovery.DiscoveredCount(), "the Core cannot hear a robot in the field");

            RunToReport(fixture, mission);
            Assert.Greater(fixture.Discovery.DiscoveredCount(), before, "and it all lands at the docking");

            fixture.Destroy();
        }

        /// <summary>Whether any cell within a radius of this one is discovered - the trail bends, so an exact point is the wrong question.</summary>
        static bool DiscoveredNear(Fixture fixture, GridCoord cell, int radius)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (fixture.Discovery.IsDiscovered(new GridCoord(cell.X + x, cell.Y + y))) return true;
                }
            }
            return false;
        }

        /// <summary>How many cells of one row are discovered. The trail runs east from the Core here, so a row across it measures its width.</summary>
        static int WidthAcross(Fixture fixture, int row)
        {
            int widest = 0;
            int run = 0;

            for (int x = (int)CoreCenter.x; x < (int)CoreCenter.x + 200; x++)
            {
                if (fixture.Discovery.IsDiscovered(new GridCoord(x, row))) run++;
                else { widest = Mathf.Max(widest, run); run = 0; }
            }

            return Mathf.Max(widest, run);
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
                // Asked for each time rather than stepped through: every trip's trail opens the ground
                // it crossed, so a fixed offset walks into a sector a previous one already revealed.
                int sector = UnknownSector(fixture);
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
        /// <summary>
        /// <b>Two minutes, whichever direction is picked, and distance adds nothing.</b> A discovery only
        /// ever goes to an entry sector, and the six sit at the same distance by construction - so the
        /// travel term every other kind carries would contribute nothing but the spread that rounding a
        /// cell into a sector produces, and two minutes would stop being two minutes for five of the six.
        /// </summary>
        [Test]
        public void ADiscovery_TakesItsFlatDuration_InEveryDirection()
        {
            Fixture fixture = NewFixture(withZones: true);

            // <b>The travel term is switched on for this one.</b> NewSettings zeroes it so that most
            // tests can ignore distance - which would make "the discovery is flat" true for the wrong
            // reason, since every kind would be flat.
            var so = new SerializedObject(fixture.Settings);
            so.FindProperty("secondsPerDistanceCell").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.AreEqual(6, fixture.Zones.ZoneCount, "precondition: there are directions to measure");

            for (int zone = 0; zone < fixture.Zones.ZoneCount; zone++)
            {
                int entry = fixture.Zones.EntrySectorOf(zone);
                Assert.IsTrue(fixture.Grid.ContainsIndex(entry), $"precondition: zone {zone} has an entry sector");

                Assert.AreEqual(fixture.Settings.DiscoverySeconds,
                    fixture.Missions.DurationOf(MissionKind.Decouverte, entry), 0.001f, $"zone {zone}");

                // The positive that makes the line above mean something: every other kind pays for the
                // trip to that very sector, so what is pinned is this kind's exemption rather than a
                // travel term that is zero for everybody.
                Assert.Greater(fixture.Missions.DurationOf(MissionKind.Prospection, entry),
                    fixture.Settings.ProspectionSeconds, $"a prospection to zone {zone} pays for the trip");
            }

            fixture.Destroy();
        }

        /// <summary>
        /// <b>One discovery per zone.</b> It is the mission that turns a direction into a place; once it
        /// has reported there is nothing left for a second one to bring back, and the zone refuses it.
        /// </summary>
        [Test]
        public void ADiscovery_IsRefusedOnceTheZoneHasBeenDiscovered()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            int entry = fixture.Zones.EntrySectorOf(0);
            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.CanLaunch(MissionKind.Decouverte, entry, CoreRadius),
                "precondition: an unvisited zone takes one");

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Decouverte, entry, CoreRadius, out MissionRuntime mission));

            RunToReport(fixture, mission);

            Assert.IsTrue(fixture.Zones.IsSurveyed(0), "precondition: it actually reported");
            Assert.AreEqual(MissionSystem.LaunchRefusal.ZoneAlreadySurveyed,
                fixture.Missions.CanLaunch(MissionKind.Decouverte, entry, CoreRadius));

            fixture.Destroy();
        }

        /// <summary>
        /// A discovery obeys no band: it is not a choice among several missions but the only one a zone
        /// takes, and the entry sector it is aimed at is a property of the zone rather than of a band.
        /// </summary>
        [Test]
        public void ADiscovery_IsRefusedByNoBand()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            // <b>One sector, two kinds.</b> The entry sector is far too close for a far exploration, so
            // the band is demonstrably being applied there - and the discovery goes to the same square
            // without it. Measuring on one sector is what keeps this about the band: anywhere else, the
            // zone gate would answer first and the band would never be reached.
            int entry = fixture.Zones.EntrySectorOf(0);

            Assert.AreEqual(MissionSystem.LaunchRefusal.WrongBand,
                fixture.Missions.CanLaunch(MissionKind.ExplorationLointaine, entry, CoreRadius));

            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.CanLaunch(MissionKind.Decouverte, entry, CoreRadius));

            fixture.Destroy();
        }

        /// <summary>
        /// <b>What the first mission is for.</b> A zone that has been chosen is still a direction: its
        /// content was derived at the choice, but nobody has been to see it, and the map has nothing to
        /// show until a robot reports. The positive comes with the negative on purpose - "not surveyed"
        /// on its own passes just as well in a world where the mission never launched.
        /// </summary>
        [Test]
        public void AZone_IsNotSurveyedUntilAMissionReportsFromIt()
        {
            Fixture fixture = NewFixture(withZones: true);
            SummonRobots(fixture);
            fixture.Zones.Choose(0, fixture.Discovery);

            Assert.IsFalse(fixture.Zones.IsSurveyed(0), "choosing a direction is not going there");

            int target = UnknownSector(fixture, 0);
            Assert.AreEqual(MissionSystem.LaunchRefusal.None,
                fixture.Missions.TryLaunch(MissionKind.Prospection, target, CoreRadius, out MissionRuntime mission));

            Assert.IsFalse(fixture.Zones.IsSurveyed(0), "nothing is known while the robot is still out");

            RunToReport(fixture, mission);

            Assert.AreEqual(MissionState.Close, mission.State, "precondition: it actually came home and delivered");
            Assert.IsTrue(fixture.Zones.IsSurveyed(0), "the report is what lists the zone");

            // The other five are untouched: a robot that went east says nothing about the west.
            for (int zone = 1; zone < fixture.Zones.ZoneCount; zone++)
            {
                Assert.IsFalse(fixture.Zones.IsSurveyed(zone), $"zone {zone} was never visited");
            }

            fixture.Destroy();
        }

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

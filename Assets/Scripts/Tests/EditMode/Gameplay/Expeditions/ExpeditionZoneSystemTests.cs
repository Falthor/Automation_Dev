using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Expeditions;
using Game.Gameplay.Sectors;
using Game.Grid;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Expeditions
{
    /// <summary>
    /// The expedition zones with no screen anywhere near them: a zone has bounds, a derived content and
    /// a choice.
    ///
    /// Three properties carry this brick and each fails quietly. The content must be a pure function of
    /// the seed, or a reloaded run finds its sites moved. The hidden stock must be finite, or a zone
    /// never ends. And cartography must be measured in ground rather than in sites, or the bar goes
    /// backwards the moment a field study finds something - which is the one thing a progress bar must
    /// never do, and the trap this whole brick is built around.
    /// </summary>
    public class ExpeditionZoneSystemTests
    {
        const int MapSize = 10000;
        const int SectorSize = 16;
        const int ChunkSize = 64;
        const int Seed = 20260908;

        static readonly Vector2 CoreCenter = new Vector2(5000f, 5000f);

        /// <summary>The Core's reach when the zones were laid out - the inner edge they were frozen against.</summary>
        const float InnerRadius = 22f;

        /// <summary>The shipped ceiling and gap: a threshold of 154 cells, and therefore an outer edge of 186.</summary>
        static SectorMissionRange NewRange() => new SectorMissionRange(32f, 90f);

        static ExpeditionZoneSettings NewSettings(
            int zoneCount = 6,
            int prospections = 3,
            int fieldStudies = 3,
            int explorations = 2,
            int recoveryMin = 2, int recoveryMax = 3,
            int studyMin = 1, int studyMax = 2,
            int hiddenMin = 3, int hiddenMax = 4,
            float radiusJitterCells = 20f,
            float angleJitterFraction = 0.1f,
            bool composeFirstZone = false,
            int firstProspections = 1, int firstFieldStudies = 3, int firstFarExplorations = 2, int firstRecoveries = 3,
            int firstStudies = 2, int firstHidden = 4)
        {
            var settings = ScriptableObject.CreateInstance<ExpeditionZoneSettings>();

            var so = new SerializedObject(settings);
            so.FindProperty("composeFirstChosenZone").boolValue = composeFirstZone;
            so.FindProperty("firstZoneProspections").intValue = firstProspections;
            so.FindProperty("firstZoneFieldStudies").intValue = firstFieldStudies;
            so.FindProperty("firstZoneFarExplorations").intValue = firstFarExplorations;
            so.FindProperty("firstZoneRecoveries").intValue = firstRecoveries;
            so.FindProperty("firstZoneCivilisationStudies").intValue = firstStudies;
            so.FindProperty("firstZoneHiddenSites").intValue = firstHidden;
            so.FindProperty("zoneCount").intValue = zoneCount;
            so.FindProperty("radiusJitterCells").floatValue = radiusJitterCells;
            so.FindProperty("angleJitterFraction").floatValue = angleJitterFraction;
            so.FindProperty("prospectionSites").intValue = prospections;
            so.FindProperty("fieldStudySites").intValue = fieldStudies;
            so.FindProperty("farExplorationSites").intValue = explorations;
            so.FindProperty("recoverySitesMin").intValue = recoveryMin;
            so.FindProperty("recoverySitesMax").intValue = recoveryMax;
            so.FindProperty("civilisationStudiesMin").intValue = studyMin;
            so.FindProperty("civilisationStudiesMax").intValue = studyMax;
            so.FindProperty("hiddenSitesMin").intValue = hiddenMin;
            so.FindProperty("hiddenSitesMax").intValue = hiddenMax;
            so.ApplyModifiedPropertiesWithoutUndo();

            return settings;
        }

        sealed class Fixture
        {
            public ExpeditionZoneSettings Settings;
            public SectorGrid Grid;
            public DiscoveryRuntime Discovery;
            public ExpeditionZoneSystem Zones;

            public void Destroy()
            {
                if (Settings != null) Object.DestroyImmediate(Settings);
            }
        }

        static Fixture NewFixture(ExpeditionZoneSettings settings = null, int seed = Seed)
        {
            var fixture = new Fixture { Settings = settings ?? NewSettings() };

            fixture.Grid = new SectorGrid(MapSize, SectorSize);
            fixture.Discovery = new DiscoveryRuntime(MapSize, ChunkSize);
            fixture.Zones = new ExpeditionZoneSystem(fixture.Settings, fixture.Grid, NewRange(),
                CoreCenter, InnerRadius, seed);

            return fixture;
        }

        // ---- Bounds ----

        /// <summary>
        /// The outer edge is the exploration threshold plus one maximum Core radius, and neither figure
        /// is written down here or there. Pinned with the resulting numbers rather than by recomputing
        /// the formula: a test that rebuilds its own expectation moves with the change and sees nothing.
        /// </summary>
        [Test]
        public void TheOuterEdge_IsTheThresholdPlusOneCoreRadius_AndFollowsBoth()
        {
            Fixture shipped = NewFixture();
            Assert.AreEqual(186f, shipped.Zones.OuterRadiusCells, 0.001f,
                "154 (2x32 + 90) plus one 32-cell Core radius.");

            var reaching = new ExpeditionZoneSystem(shipped.Settings, shipped.Grid,
                new SectorMissionRange(80f, 90f), CoreCenter, InnerRadius, Seed);
            Assert.AreEqual(330f, reaching.OuterRadiusCells, 0.001f,
                "A Core reaching 80 moves the threshold to 250 and the edge to 330, with nothing else touched.");

            shipped.Destroy();
        }

        /// <summary>
        /// Every bearing belongs to exactly one zone, and the six of them leave no gap. Walked degree by
        /// degree because the seams are where a per-zone containment test would go wrong, and there is no
        /// per-zone containment test - the partition is one division, which is why this can be asserted
        /// at all.
        /// </summary>
        [Test]
        public void TheZones_PartitionTheCircle_WithNoGapAndNoOverlap()
        {
            Fixture fixture = NewFixture();
            var seen = new HashSet<int>();

            for (int tenthDegree = 0; tenthDegree < 3600; tenthDegree++)
            {
                float radians = tenthDegree * 0.1f * Mathf.Deg2Rad;
                var point = CoreCenter + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 100f;

                int zone = fixture.Zones.ZoneAt(point);
                Assert.GreaterOrEqual(zone, 0, $"bearing {tenthDegree * 0.1f}° belongs to no zone");
                Assert.Less(zone, 6);
                seen.Add(zone);
            }

            Assert.AreEqual(6, seen.Count, "every zone must own some of the circle");

            // And the radial edges: the Core's own ground is nobody's zone, and neither is anything past
            // the outer edge - which is what makes a zone finite.
            Assert.AreEqual(-1, fixture.Zones.ZoneAt(CoreCenter + new Vector2(InnerRadius - 1f, 0f)));
            Assert.AreEqual(-1, fixture.Zones.ZoneAt(CoreCenter + new Vector2(187f, 0f)));

            fixture.Destroy();
        }

        // ---- Content ----

        /// <summary>
        /// Asked in a scrambled order in one system and in order in another, a zone answers the same
        /// thing. The derivation consults nothing mutable, so there is nothing an order could disturb -
        /// this states that the construction still holds rather than waiting to trip on it.
        /// </summary>
        [Test]
        public void AZonesContent_IsTheSameWhateverOrderTheZonesAreAskedAbout()
        {
            Fixture scrambled = NewFixture();
            var recorded = new Dictionary<int, List<(ExpeditionSiteKind kind, GridCoord cell, ExpeditionFinding finding)>>();

            foreach (int zone in new[] { 4, 1, 5, 0, 3, 2 })
            {
                var list = new List<(ExpeditionSiteKind, GridCoord, ExpeditionFinding)>();
                foreach (ExpeditionZoneSite site in scrambled.Zones.SitesOf(zone))
                {
                    list.Add((site.Kind, site.Cell, site.Finding));
                }
                recorded[zone] = list;
            }

            Fixture ordered = NewFixture();

            for (int zone = 0; zone < 6; zone++)
            {
                IReadOnlyList<ExpeditionZoneSite> sites = ordered.Zones.SitesOf(zone);
                Assert.AreEqual(recorded[zone].Count, sites.Count, $"zone {zone} changed size");

                for (int i = 0; i < sites.Count; i++)
                {
                    Assert.AreEqual(recorded[zone][i].kind, sites[i].Kind, $"zone {zone} site {i}");
                    Assert.AreEqual(recorded[zone][i].cell, sites[i].Cell, $"zone {zone} site {i}");
                    Assert.AreEqual(recorded[zone][i].finding, sites[i].Finding, $"zone {zone} site {i}");
                }
            }

            scrambled.Destroy();
            ordered.Destroy();
        }

        /// <summary>Two worlds are not one map with the same answers - and, specifically, the six zones of one world are not rotations of each other, which is what leaving the zone out of the jitter's mix produced while this was written.</summary>
        [Test]
        public void TheSixZones_AreNotRotationsOfOneAnother()
        {
            Fixture fixture = NewFixture();

            var bearings = new HashSet<float>();
            for (int zone = 0; zone < 6; zone++)
            {
                IReadOnlyList<ExpeditionZoneSite> sites = fixture.Zones.SitesOf(zone);
                ExpeditionZoneSite first = sites[0];

                Vector2 offset = new Vector2(first.Cell.X, first.Cell.Y) - CoreCenter;

                // The bearing relative to its own zone's centre. Identical for all six means one
                // stencil turned six times.
                float relative = Mathf.DeltaAngle(zone * fixture.Zones.SliceDegrees,
                    Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg);
                bearings.Add(Mathf.Round(relative * 100f) / 100f);
            }

            Assert.Greater(bearings.Count, 1, "all six zones placed their first site on the same relative bearing");

            fixture.Destroy();
        }

        /// <summary>
        /// A site is never placed outside the zone it belongs to. The jitter is laid on a ladder that
        /// already left room for it, so this holds by construction rather than by a clamp - and a clamp
        /// is exactly what would hide the settings having outgrown the geometry.
        /// </summary>
        [Test]
        public void EverySite_StandsInsideItsOwnZone()
        {
            Fixture fixture = NewFixture();

            for (int zone = 0; zone < 6; zone++)
            {
                foreach (ExpeditionZoneSite site in fixture.Zones.SitesOf(zone))
                {
                    var centre = new Vector2(site.Cell.X + 0.5f, site.Cell.Y + 0.5f);
                    Assert.AreEqual(zone, fixture.Zones.ZoneAt(centre),
                        $"zone {zone} site {site.IndexInZone} ({site.Kind}) landed outside its own zone at {site.Cell.X},{site.Cell.Y}");
                }
            }

            fixture.Destroy();
        }

        /// <summary>
        /// A far reconnaissance site sits past the exploration threshold, which is the only band a far
        /// reconnaissance may be sent into; everything else sits short of it, where a prospection may go.
        /// A site placed where its own kind of mission cannot reach would be a quest nobody can accept.
        /// </summary>
        [Test]
        public void EverySite_StandsWhereItsOwnKindOfMissionIsAllowedToGo()
        {
            Fixture fixture = NewFixture();
            float threshold = NewRange().ExplorationMinimumCells;

            for (int zone = 0; zone < 6; zone++)
            {
                foreach (ExpeditionZoneSite site in fixture.Zones.SitesOf(zone))
                {
                    float distance = Vector2.Distance(new Vector2(site.Cell.X + 0.5f, site.Cell.Y + 0.5f), CoreCenter);

                    if (site.Kind == ExpeditionSiteKind.ExplorationLointaine)
                    {
                        Assert.Greater(distance, threshold,
                            $"a far reconnaissance site at {distance:0.#} cells is inside the mining band");
                    }
                    else
                    {
                        Assert.LessOrEqual(distance, threshold,
                            $"a {site.Kind} site at {distance:0.#} cells is past the exploration threshold");
                    }
                }
            }

            fixture.Destroy();
        }

        /// <summary>
        /// Exactly one of the two far reconnaissances carries the secondary Core site - which is what
        /// makes finding it a draw rather than an announcement - and <b>the other is never empty</b>. Half
        /// the players would otherwise discover that one of their two quests held nothing.
        /// </summary>
        [Test]
        public void OneFarExplorationCarriesTheSecondaryCore_AndTheOtherStillCarriesSomething()
        {
            Fixture fixture = NewFixture();

            for (int zone = 0; zone < 6; zone++)
            {
                int carriers = 0;
                int explorations = 0;

                foreach (ExpeditionZoneSite site in fixture.Zones.SitesOf(zone))
                {
                    if (site.Kind != ExpeditionSiteKind.ExplorationLointaine)
                    {
                        Assert.AreEqual(ExpeditionFinding.None, site.Finding,
                            "only a far reconnaissance carries a finding");
                        continue;
                    }

                    explorations++;
                    if (site.Finding == ExpeditionFinding.SiteNoyauSecondaire) carriers++;
                    else Assert.AreEqual(ExpeditionFinding.TraceCivilisation, site.Finding,
                        "the far reconnaissance that does not carry the Core site must still carry something");
                }

                Assert.AreEqual(2, explorations, $"zone {zone}");
                Assert.AreEqual(1, carriers, $"zone {zone} must hold exactly one secondary Core site");
            }

            fixture.Destroy();
        }

        // ---- The hidden stock ----

        /// <summary>
        /// The stock runs out. Asked far more times than it holds, it hands over what it has and then
        /// nothing - and the zone's site list never grows, because the hidden sites were drawn with
        /// everything else at derivation rather than invented on demand.
        /// </summary>
        [Test]
        public void TheHiddenStock_IsFinite_AndRunsOut()
        {
            Fixture fixture = NewFixture(NewSettings(hiddenMin: 3, hiddenMax: 3));

            int before = fixture.Zones.SitesOf(2).Count;
            Assert.AreEqual(3, fixture.Zones.HiddenSitesLeft(2));

            var found = new HashSet<int>();
            for (int attempt = 0; attempt < 50; attempt++)
            {
                ExpeditionZoneSite site = fixture.Zones.RevealNextHiddenSite(2);
                if (site == null) break;

                Assert.IsTrue(site.IsHidden);
                Assert.IsTrue(site.IsRevealed);
                Assert.IsTrue(found.Add(site.IndexInZone), "the same hidden site was handed over twice");
            }

            Assert.AreEqual(3, found.Count, "exactly the stock, no more");
            Assert.AreEqual(0, fixture.Zones.HiddenSitesLeft(2));
            Assert.IsNull(fixture.Zones.RevealNextHiddenSite(2), "a spent stock finds only ground");
            Assert.AreEqual(before, fixture.Zones.SitesOf(2).Count, "revealing must not add a site to the list");

            fixture.Destroy();
        }

        // ---- Cartography ----

        /// <summary>
        /// <b>The trap this brick exists around.</b> Progress is ground seen against ground there is, so
        /// turning up a hidden site - which raises the number of sites in the zone - moves the bar by
        /// exactly nothing. A count of sites would have gone backwards here.
        /// </summary>
        [Test]
        public void FindingAHiddenSite_DoesNotMoveTheCartography()
        {
            Fixture fixture = NewFixture();
            fixture.Discovery.RevealDisc(CoreCenter + new Vector2(60f, 0f), 20f);

            float before = fixture.Zones.CartographyOf(0, fixture.Discovery).Ratio;
            Assert.Greater(before, 0f, "precondition: some of zone 0 is open");

            Assert.IsNotNull(fixture.Zones.RevealNextHiddenSite(0));

            Assert.AreEqual(before, fixture.Zones.CartographyOf(0, fixture.Discovery).Ratio, 0.0000001f,
                "cartography is measured in surface; a site appearing is a consequence, never the measure");

            fixture.Destroy();
        }

        /// <summary>
        /// It only ever goes up. Over a fixed set of cells with a discovery that only ever adds, that is
        /// a property of the construction rather than one to police - which is why the zone's inner edge
        /// is frozen at layout instead of following the Core's growing radius: dropping fully-discovered
        /// ground from both halves of the ratio lowers it.
        /// </summary>
        [Test]
        public void Cartography_NeverRecedes_AsGroundIsOpened()
        {
            Fixture fixture = NewFixture();
            fixture.Zones.Choose(0, fixture.Discovery);

            float previous = fixture.Zones.CartographyOf(0, fixture.Discovery).Ratio;
            Assert.AreEqual(0f, previous, 0.0000001f, "nothing is open yet");

            for (int step = 1; step <= 8; step++)
            {
                // Marching outward along zone 0's own bearing, so each step opens more of it.
                fixture.Discovery.RevealDisc(CoreCenter + new Vector2(step * 20f, 0f), 18f);

                ExpeditionZoneCartography now = fixture.Zones.CartographyOf(0, fixture.Discovery);
                Assert.GreaterOrEqual(now.Ratio, previous, $"step {step} moved the bar backwards");
                Assert.LessOrEqual(now.DiscoveredCells, now.TotalCells);
                previous = now.Ratio;
            }

            Assert.Greater(previous, 0f, "eight discs along its bearing must have opened some of the zone");

            fixture.Destroy();
        }

        /// <summary>The six totals are one walk of one partition, so they add up to the ring and never overlap - the property that would break silently if each zone measured itself.</summary>
        [Test]
        public void TheSixTotals_AddUpToTheWholeRing()
        {
            Fixture fixture = NewFixture();

            int summed = 0;
            for (int zone = 0; zone < 6; zone++) summed += fixture.Zones.CartographyOf(zone, fixture.Discovery).TotalCells;

            // The annulus between 22 and 186 cells, counted by cell centres. Compared loosely because
            // the discretisation of a disc is not π r² to the cell, but a percent is far tighter than
            // any double-count or gap could hide in.
            float expected = Mathf.PI * (186f * 186f - InnerRadius * InnerRadius);
            Assert.AreEqual(expected, summed, expected * 0.01f);

            fixture.Destroy();
        }

        // ---- Settings ----

        /// <summary>
        /// The mechanical test of validity the directive asks for: move one setting, re-run, and check
        /// that nothing else needed adjusting by hand. Eight zones cut 45° slices, every one of them
        /// still holds its full content, and every site is still inside its own - narrower - zone.
        /// </summary>
        [Test]
        public void ChangingTheZoneCount_MovesEverythingThatDependsOnIt()
        {
            Fixture fixture = NewFixture(NewSettings(zoneCount: 8));

            Assert.AreEqual(8, fixture.Zones.ZoneCount);
            Assert.AreEqual(45f, fixture.Zones.SliceDegrees, 0.001f, "the slice is derived from the count, never entered");

            var seen = new HashSet<int>();
            for (int tenthDegree = 0; tenthDegree < 3600; tenthDegree++)
            {
                float radians = tenthDegree * 0.1f * Mathf.Deg2Rad;
                seen.Add(fixture.Zones.ZoneAt(CoreCenter + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * 100f));
            }
            CollectionAssert.DoesNotContain(seen, -1, "the eight slices must still cover the circle");
            Assert.AreEqual(8, seen.Count);

            for (int zone = 0; zone < 8; zone++)
            {
                IReadOnlyList<ExpeditionZoneSite> sites = fixture.Zones.SitesOf(zone);
                Assert.Greater(sites.Count, 0, $"zone {zone} came out empty");

                foreach (ExpeditionZoneSite site in sites)
                {
                    Assert.AreEqual(zone, fixture.Zones.ZoneAt(new Vector2(site.Cell.X + 0.5f, site.Cell.Y + 0.5f)),
                        $"a site left its zone once the slices narrowed to 45°");
                }
            }

            fixture.Destroy();
        }

        /// <summary>The site counts come from the settings and from nowhere else. Pinned on the one that is a range, since a fixed count could be right by accident.</summary>
        [Test]
        public void ChangingASiteCount_MovesWhatTheZoneHolds()
        {
            Fixture five = NewFixture(NewSettings(hiddenMin: 5, hiddenMax: 5, prospections: 1, fieldStudies: 0, explorations: 1,
                recoveryMin: 0, recoveryMax: 0, studyMin: 0, studyMax: 0));

            Assert.AreEqual(5, five.Zones.HiddenSitesLeft(3));
            Assert.AreEqual(1 + 1 + 5, five.Zones.SitesOf(3).Count, "one prospection, one exploration, five hidden");

            five.Destroy();
        }

        // ---- The composed first zone ----

        /// <summary>How many sites of one kind a zone holds, ignoring the hidden stock.</summary>
        static int VisibleCount(ExpeditionZoneSystem zones, int zone, ExpeditionSiteKind kind)
        {
            int count = 0;
            foreach (ExpeditionZoneSite site in zones.SitesOf(zone))
            {
                if (!site.IsHidden && site.Kind == kind) count++;
            }
            return count;
        }

        static void AssertComposed(ExpeditionZoneSystem zones, int zone)
        {
            Assert.AreEqual(1, VisibleCount(zones, zone, ExpeditionSiteKind.Prospection), $"zone {zone} prospections");
            Assert.AreEqual(3, VisibleCount(zones, zone, ExpeditionSiteKind.EtudeDeTerrain), $"zone {zone} field studies");
            Assert.AreEqual(2, VisibleCount(zones, zone, ExpeditionSiteKind.ExplorationLointaine), $"zone {zone} far explorations");
            Assert.AreEqual(3, VisibleCount(zones, zone, ExpeditionSiteKind.Recuperation), $"zone {zone} recoveries");
            Assert.AreEqual(2, VisibleCount(zones, zone, ExpeditionSiteKind.EtudeCivilisation), $"zone {zone} studies");
            Assert.AreEqual(4, zones.HiddenSitesLeft(zone), $"zone {zone} hidden stock");
        }

        /// <summary>
        /// <b>It is not a slice, it is the choice.</b> Whichever of the six the player picks receives the
        /// composed content - so the six can stay equivalent while they are still on offer, which the
        /// design requires and which composing one particular slice would break.
        /// </summary>
        [Test]
        public void WhicheverZoneIsChosenFirst_CarriesTheComposedContent()
        {
            for (int chosen = 0; chosen < 6; chosen++)
            {
                Fixture fixture = NewFixture(NewSettings(composeFirstZone: true));
                Assert.AreEqual(ZoneChoiceRefusal.None, fixture.Zones.Choose(chosen, fixture.Discovery));

                Assert.IsTrue(fixture.Zones.IsComposed(chosen));
                AssertComposed(fixture.Zones, chosen);

                fixture.Destroy();
            }
        }

        /// <summary>The other five keep the derivation. Checked on the counts the composition changes, so a composition leaking sideways cannot pass.</summary>
        [Test]
        public void TheFiveUnchosenZones_KeepTheirDerivedContent()
        {
            Fixture fixture = NewFixture(NewSettings(composeFirstZone: true));
            fixture.Zones.Choose(2, fixture.Discovery);

            for (int zone = 0; zone < 6; zone++)
            {
                if (zone == 2) continue;

                Assert.IsFalse(fixture.Zones.IsComposed(zone));
                Assert.AreEqual(3, VisibleCount(fixture.Zones, zone, ExpeditionSiteKind.Prospection),
                    $"zone {zone} must still derive its three prospections, not the composed one");
                Assert.That(fixture.Zones.HiddenSitesLeft(zone), Is.InRange(3, 4), $"zone {zone} hidden stock");
            }

            fixture.Destroy();
        }

        /// <summary>
        /// <b>The ordering trap, and the reason the rule is about data rather than a step to perform
        /// first.</b> A zone's content is derived on first request and kept, so a screen that offered the
        /// six - or a hover, or a test - would have cached the derived content before the choice was
        /// made, and the composition would have arrived too late to be seen. Choosing drops what was
        /// cached, so the order the two happen in cannot matter.
        /// </summary>
        [Test]
        public void AskingAboutAZoneBeforeChoosingIt_DoesNotFreezeItsDerivedContent()
        {
            Fixture fixture = NewFixture(NewSettings(composeFirstZone: true));

            // Exactly what a zone-choice screen does: show all six before anything is picked.
            for (int zone = 0; zone < 6; zone++)
            {
                Assert.AreEqual(3, VisibleCount(fixture.Zones, zone, ExpeditionSiteKind.Prospection),
                    "precondition: before the choice every zone is derived");
            }

            fixture.Zones.Choose(5, fixture.Discovery);

            AssertComposed(fixture.Zones, 5);

            fixture.Destroy();
        }

        /// <summary>Nothing can have been done to a site before a zone is chosen - no mission launches until then - so re-deriving at the choice costs nothing. Stated rather than assumed, because it is what makes dropping the cache safe.</summary>
        [Test]
        public void BeforeAZoneIsChosen_NoSiteCanCarryState()
        {
            Fixture fixture = NewFixture(NewSettings(composeFirstZone: true));

            foreach (ExpeditionZoneSite site in fixture.Zones.SitesOf(1))
            {
                Assert.IsFalse(site.IsConsumed, "nothing can have been spent");
                Assert.AreEqual(!site.IsHidden, site.IsRevealed, "and nothing turned up");
            }

            fixture.Destroy();
        }

        /// <summary>The composition is a setting like any other: move it and the zone follows, with nothing else to adjust.</summary>
        [Test]
        public void ChangingTheComposition_MovesWhatTheFirstZoneHolds()
        {
            Fixture fixture = NewFixture(NewSettings(composeFirstZone: true,
                firstProspections: 0, firstFarExplorations: 1, firstRecoveries: 5,
                firstStudies: 0, firstHidden: 1));
            fixture.Zones.Choose(0, fixture.Discovery);

            Assert.AreEqual(0, VisibleCount(fixture.Zones, 0, ExpeditionSiteKind.Prospection));
            Assert.AreEqual(1, VisibleCount(fixture.Zones, 0, ExpeditionSiteKind.ExplorationLointaine));
            Assert.AreEqual(5, VisibleCount(fixture.Zones, 0, ExpeditionSiteKind.Recuperation));
            Assert.AreEqual(0, VisibleCount(fixture.Zones, 0, ExpeditionSiteKind.EtudeCivilisation));
            Assert.AreEqual(1, fixture.Zones.HiddenSitesLeft(0));

            fixture.Destroy();
        }

        /// <summary>Switched off, the chosen zone derives like the other five - so the composition is a decision the data carries, not something the code always does.</summary>
        [Test]
        public void WithTheCompositionOff_TheChosenZoneDerivesLikeTheRest()
        {
            Fixture fixture = NewFixture(NewSettings(composeFirstZone: false));
            fixture.Zones.Choose(0, fixture.Discovery);

            Assert.IsFalse(fixture.Zones.IsComposed(0));
            Assert.AreEqual(3, VisibleCount(fixture.Zones, 0, ExpeditionSiteKind.Prospection));

            fixture.Destroy();
        }

        // ---- Save / Restore ----

        /// <summary>
        /// The composition adds nothing to the save, and that is the point: it is a function of the
        /// chosen zone, which already travels, and of a settings asset. A reload re-derives it rather
        /// than restoring it - so what this pins is that the re-derivation lands on exactly the same
        /// sites, positions included.
        /// </summary>
        [Test]
        public void TheComposedZone_ComesBackIdenticalAfterAReload()
        {
            Fixture original = NewFixture(NewSettings(composeFirstZone: true));
            original.Zones.Choose(4, original.Discovery);
            AssertComposed(original.Zones, 4);

            JObject captured = original.Zones.CaptureState();

            Fixture reloaded = NewFixture(NewSettings(composeFirstZone: true));

            // The trap: the reloaded run asks about the zone before the save has said which one is
            // chosen. RestoreState clears what that derived and sets the choice before re-deriving.
            for (int zone = 0; zone < 6; zone++) reloaded.Zones.SitesOf(zone);

            reloaded.Zones.RestoreState(captured);

            Assert.AreEqual(4, reloaded.Zones.ChosenZone);
            Assert.IsTrue(reloaded.Zones.IsComposed(4));
            AssertComposed(reloaded.Zones, 4);

            IReadOnlyList<ExpeditionZoneSite> before = original.Zones.SitesOf(4);
            IReadOnlyList<ExpeditionZoneSite> after = reloaded.Zones.SitesOf(4);
            Assert.AreEqual(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].Kind, after[i].Kind, $"site {i} kind");
                Assert.AreEqual(before[i].Cell, after[i].Cell, $"site {i} cell");
                Assert.AreEqual(before[i].Finding, after[i].Finding, $"site {i} finding");
            }

            original.Destroy();
            reloaded.Destroy();
        }


        /// <summary>
        /// What survives a save is the choice, the frozen inner edge and what the player has done to the
        /// sites. Everything else is re-derived - so what this really pins is that the re-derivation and
        /// the restored history land on the same sites.
        /// </summary>
        [Test]
        public void TheChoice_TheInnerEdgeAndTheSitesTouched_SurviveARoundTrip()
        {
            Fixture original = NewFixture();
            original.Zones.Choose(4, original.Discovery);
            ExpeditionZoneSite found = original.Zones.RevealNextHiddenSite(4);
            Assert.IsTrue(original.Zones.Consume(4, 0));

            JObject captured = original.Zones.CaptureState();

            Fixture reloaded = NewFixture();
            Assert.AreEqual(-1, reloaded.Zones.ChosenZone, "precondition: a fresh run has chosen nothing");

            reloaded.Zones.RestoreState(captured);

            Assert.AreEqual(4, reloaded.Zones.ChosenZone);
            Assert.AreEqual(original.Zones.InnerRadiusCells, reloaded.Zones.InnerRadiusCells, 0.001f);
            Assert.IsTrue(reloaded.Zones.SitesOf(4)[found.IndexInZone].IsRevealed, "the hidden site stays found");
            Assert.IsTrue(reloaded.Zones.SitesOf(4)[0].IsConsumed);
            Assert.AreEqual(original.Zones.HiddenSitesLeft(4), reloaded.Zones.HiddenSitesLeft(4));

            // And the sites themselves came back where they were, since only history was stored.
            Assert.AreEqual(original.Zones.SitesOf(4)[found.IndexInZone].Cell,
                reloaded.Zones.SitesOf(4)[found.IndexInZone].Cell);

            original.Destroy();
            reloaded.Destroy();
        }

        /// <summary>
        /// The inner edge travels because the layout was frozen against it. A save restored into a run
        /// whose Core has since grown must keep the zones it was mapping, or the ground under a
        /// half-finished map moves.
        /// </summary>
        [Test]
        public void RestoringIntoAWiderCore_KeepsTheZonesTheRunWasMapping()
        {
            Fixture original = NewFixture();
            original.Zones.Choose(1, original.Discovery);
            JObject captured = original.Zones.CaptureState();

            var settings = NewSettings();
            var grid = new SectorGrid(MapSize, SectorSize);
            var widened = new ExpeditionZoneSystem(settings, grid, NewRange(), CoreCenter, 32f, Seed);
            Assert.AreEqual(32f, widened.InnerRadiusCells, 0.001f, "precondition: laid out against a wider Core");

            widened.RestoreState(captured);

            Assert.AreEqual(InnerRadius, widened.InnerRadiusCells, 0.001f,
                "the saved layout wins over the Core's current reach");

            Object.DestroyImmediate(settings);
            original.Destroy();
        }

        /// <summary>Tolerant like every other Restore: a save from before the zones is a run with the six still on offer, not a throw.</summary>
        [Test]
        public void RestoringNothing_LeavesTheSixZonesOnOffer()
        {
            Fixture fixture = NewFixture();
            fixture.Zones.Choose(2, fixture.Discovery);

            fixture.Zones.RestoreState(null);

            Assert.AreEqual(-1, fixture.Zones.ChosenZone);
            for (int zone = 0; zone < 6; zone++) Assert.IsTrue(fixture.Zones.IsAvailable(zone, fixture.Discovery));

            fixture.Destroy();
        }

        /// <summary>Choosing is a one-way door until the zone is mapped. Named rather than boolean, so the caller can tell "not a zone" from "too late".</summary>
        [Test]
        public void ASecondChoice_IsRefused_AndReChoosingTheSameZoneIsNot()
        {
            Fixture fixture = NewFixture();

            Assert.AreEqual(ZoneChoiceRefusal.None, fixture.Zones.Choose(3, fixture.Discovery));
            Assert.AreEqual(ZoneChoiceRefusal.AlreadyChosen, fixture.Zones.Choose(4, fixture.Discovery));
            Assert.AreEqual(ZoneChoiceRefusal.None, fixture.Zones.Choose(3, fixture.Discovery), "asking again for what is already chosen changes nothing");
            Assert.AreEqual(ZoneChoiceRefusal.NotAZone, fixture.Zones.Choose(9, fixture.Discovery));
            Assert.AreEqual(3, fixture.Zones.ChosenZone);

            fixture.Destroy();
        }
    }
}

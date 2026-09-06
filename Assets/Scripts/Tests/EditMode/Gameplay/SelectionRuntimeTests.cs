using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Selection;
using Game.Gameplay.Sites;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay
{
    /// <summary>
    /// CONTRACTS.md §7: what is currently inspected. Three slots - a building, a construction site,
    /// a named global panel - and at most one of them set at a time.
    ///
    /// The site has a slot of its own rather than riding on SelectedBuilding, and that is the thing
    /// worth pinning: its segments <b>are</b> BuildingRuntimes and are what the grid hands back, so
    /// the tempting shortcut is to select the segment. Every per-building panel keys off
    /// SelectionChanged with an `as` cast, so a Foundry segment would open a production panel over a
    /// machine that does not exist yet.
    /// </summary>
    public class SelectionRuntimeTests
    {
        static ConstructionSiteRuntime NewSite(string definitionId = "target")
        {
            StorageDefinition definition = TestDataFactory.NewStorage(definitionId);
            var segment = new StorageRuntime(definition, new GridCoord(4, 4), Direction.North);
            return new ConstructionSiteRuntime(1, segment);
        }

        static BuildingRuntime NewBuilding()
        {
            StorageDefinition definition = TestDataFactory.NewStorage("box");
            return new StorageRuntime(definition, new GridCoord(9, 9), Direction.North);
        }

        [Test]
        public void ASelectedSite_DoesNotAppearAsASelectedBuilding()
        {
            var selection = new SelectionRuntime();
            ConstructionSiteRuntime site = NewSite();

            BuildingRuntime raisedBuilding = null;
            bool buildingEventFired = false;
            selection.SelectionChanged += b => { raisedBuilding = b; buildingEventFired = true; };

            selection.SelectSite(site);

            Assert.AreSame(site, selection.SelectedSite);
            Assert.IsNull(selection.SelectedBuilding,
                "A site's segment is a BuildingRuntime, but the site is not a building - the per-building "
                + "panels must not see it at all.");
            Assert.IsFalse(buildingEventFired, "And they are not even notified.");
            Assert.IsNull(raisedBuilding);
        }

        [Test]
        public void SelectingABuilding_ClosesAnOpenSite_AndTheOtherWayRound()
        {
            var selection = new SelectionRuntime();
            ConstructionSiteRuntime site = NewSite();
            BuildingRuntime building = NewBuilding();

            selection.SelectSite(site);
            selection.Select(building);

            Assert.IsNull(selection.SelectedSite, "Inspecting a building stops inspecting the site.");
            Assert.AreSame(building, selection.SelectedBuilding);

            selection.SelectSite(site);

            Assert.IsNull(selection.SelectedBuilding, "And a site closes the building.");
            Assert.AreSame(site, selection.SelectedSite);
        }

        [Test]
        public void OpeningAGlobalPanel_ClosesASelectedSite()
        {
            var selection = new SelectionRuntime();

            selection.SelectSite(NewSite());
            selection.OpenGlobalPanel("Storage");

            Assert.IsNull(selection.SelectedSite);
            Assert.AreEqual("Storage", selection.ActiveGlobalPanel);

            selection.SelectSite(NewSite());

            Assert.IsNull(selection.ActiveGlobalPanel, "And selecting a site closes the global panel.");
        }

        [Test]
        public void Clear_ClosesWhicheverSlotWasSet()
        {
            var selection = new SelectionRuntime();

            selection.SelectSite(NewSite());
            selection.Clear();
            Assert.IsNull(selection.SelectedSite);

            selection.Select(NewBuilding());
            selection.Clear();
            Assert.IsNull(selection.SelectedBuilding);
        }

        /// <summary>
        /// Panels close themselves through Clear(), which now covers both slots. Firing the event
        /// for a slot that was already empty would have an unrelated panel re-run its close path on
        /// every clear.
        /// </summary>
        [Test]
        public void Clear_OnlyNotifiesTheSlotThatWasActuallySet()
        {
            var selection = new SelectionRuntime();

            int buildingEvents = 0;
            int siteEvents = 0;
            selection.SelectionChanged += _ => buildingEvents++;
            selection.SiteSelectionChanged += _ => siteEvents++;

            selection.SelectSite(NewSite());
            Assert.AreEqual(1, siteEvents);
            Assert.AreEqual(0, buildingEvents);

            selection.Clear();
            Assert.AreEqual(2, siteEvents, "The site slot was set, so it is told it is now empty.");
            Assert.AreEqual(0, buildingEvents, "The building slot never was, so nothing is told anything.");

            selection.Clear();
            Assert.AreEqual(2, siteEvents, "Clearing nothing notifies nobody.");
            Assert.AreEqual(0, buildingEvents);
        }
    }
}

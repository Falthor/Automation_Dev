using System;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;

namespace Game.Gameplay.Selection
{
    /// <summary>
    /// Owns what is currently inspected and the global UI-panel selection state (CONTRACTS.md §7).
    /// Three slots, and at most one of them is ever set: a building, a construction site, or a named
    /// global panel. Opening any of them closes the other two, matching the source project's
    /// behavior.
    ///
    /// A site has its own slot rather than riding on <see cref="SelectedBuilding"/>, even though its
    /// segments are BuildingRuntimes and are what the grid returns. Selecting a segment would open
    /// the panel of whatever it will eventually become - a Foundry's production tab over a site that
    /// has produced nothing and has no recipe - because every per-building panel keys off
    /// SelectionChanged with an `as` cast. The two are different questions about the same cell, so
    /// they are different slots.
    /// </summary>
    public sealed class SelectionRuntime
    {
        public BuildingRuntime SelectedBuilding { get; private set; }
        public ConstructionSiteRuntime SelectedSite { get; private set; }
        public string ActiveGlobalPanel { get; private set; }

        /// <summary>
        /// The one building an open global panel is about, when it is about one.
        ///
        /// Not the same slot as <see cref="SelectedBuilding"/>, and deliberately so: that one is
        /// mutually exclusive with a global panel - <see cref="Select"/> closes any open panel - while
        /// this one accompanies it. The Storage panel is the case that needs it. It serves two views
        /// through the same global slot, the aggregate over every container and one specific box, and
        /// only the second is about a place on the map. Without this, a box being inspected was a
        /// building nothing in the world could point at, and the hover outline had no cell to draw on.
        /// </summary>
        public BuildingRuntime GlobalPanelSubject { get; private set; }

        public event Action<BuildingRuntime> SelectionChanged;
        public event Action<ConstructionSiteRuntime> SiteSelectionChanged;
        public event Action<string> GlobalPanelChanged;

        public void Select(BuildingRuntime building)
        {
            if (ActiveGlobalPanel != null) CloseGlobalPanel();
            ClearSite();

            SelectedBuilding = building;
            SelectionChanged?.Invoke(building);
        }

        /// <summary>Inspects a construction site - its bill of materials rather than a building's production.</summary>
        public void SelectSite(ConstructionSiteRuntime site)
        {
            if (ActiveGlobalPanel != null) CloseGlobalPanel();
            ClearBuilding();

            SelectedSite = site;
            SiteSelectionChanged?.Invoke(site);
        }

        /// <summary>Stops inspecting anything. Fires only for the slot that was actually set, so a panel is never told to close twice.</summary>
        public void Clear()
        {
            ClearBuilding();
            ClearSite();
        }

        void ClearBuilding()
        {
            if (SelectedBuilding == null) return;

            SelectedBuilding = null;
            SelectionChanged?.Invoke(null);
        }

        void ClearSite()
        {
            if (SelectedSite == null) return;

            SelectedSite = null;
            SiteSelectionChanged?.Invoke(null);
        }

        public BuildingRuntime GetSelectedBuilding() => SelectedBuilding;

        /// <summary>Opens a named global panel, closing whichever one was open before (no-op if already active).</summary>
        public void OpenGlobalPanel(string name)
        {
            if (ActiveGlobalPanel == name) return;
            Clear();

            // A newly opened panel is about nothing until whoever opened it says otherwise, so the
            // subject never survives from the panel before.
            GlobalPanelSubject = null;
            ActiveGlobalPanel = name;
            GlobalPanelChanged?.Invoke(name);
        }

        /// <summary>Names the building the open panel is about, so the world can mark it. Cleared with the panel.</summary>
        public void SetGlobalPanelSubject(BuildingRuntime building)
        {
            if (ActiveGlobalPanel == null) return;

            GlobalPanelSubject = building;
        }

        public void CloseGlobalPanel()
        {
            if (ActiveGlobalPanel == null) return;

            GlobalPanelSubject = null;
            ActiveGlobalPanel = null;
            GlobalPanelChanged?.Invoke(null);
        }
    }
}

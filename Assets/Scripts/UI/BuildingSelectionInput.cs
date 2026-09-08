using Game.Core;
using Game.Gameplay.Buildings;
using Game.Gameplay.Sites;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Left-click routing to a building's own info panel, active only while no construction tool
    /// is selected (ConstructionInputAdapter owns left-click while placing/demolishing) and no
    /// other UI panel is open. A plain grid-cell lookup, no colliders or physics involved.
    ///
    /// Storage keeps its own dedicated global-panel mechanism (aggregate vs per-box, opened via
    /// the Bottom Nav or a click here) - clicking one calls StoragePanelController.Show directly.
    /// Every other per-building info panel (Extractor now, more later) instead goes through
    /// SelectionRuntime.Select(building) (CONTRACTS.md §7's "currently inspected building"),
    /// which the matching panel controller (e.g. ExtractorPanelController) reacts to - so adding
    /// a new building type's panel never means touching this router's Storage-specific branch.
    ///
    /// It is also the single place that implements click-outside-to-close for global panels:
    /// this is the one component that already sees every world-bound click and can tell UI from
    /// world, so the behavior lives here once instead of being duplicated in each panel.
    /// </summary>
    public sealed class BuildingSelectionInput : MonoBehaviour
    {
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Camera worldCamera;
        [SerializeField] StoragePanelController storagePanel;
        [SerializeField] UIDocument uiDocument;

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
        }

        void Update()
        {
            if (worldCamera == null || gameRuntime == null || storagePanel == null) return;
            if (gameRuntime.Construction.Selected != null) return;

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Vector2 screenPos = mouse.position.ReadValue();
            bool overUI = IsPointerOverUI(screenPos);

            // A global panel (Storage/Building/Research/Power/...) owns the click while it is
            // open: inside it, the panel's own widgets handle it; outside it, the click closes
            // it. Either way it never also reaches the world on that frame. An already-open
            // per-building panel (SelectedBuilding != null, also part of IsUIBlockingInput) must
            // NOT block routing below, otherwise clicking a different building while one is
            // selected - or clicking empty space to close it - would never register.
            if (gameRuntime.Selection.ActiveGlobalPanel != null)
            {
                if (!overUI) gameRuntime.Selection.CloseGlobalPanel();
                return;
            }

            if (gameRuntime.LastMenuCloseFrame == Time.frameCount) return;

            // A click that actually lands on a real UI element (a recipe card, a tab button, a
            // panel's own content) must never also be treated as a world click - otherwise
            // clicking something inside an open per-building panel (e.g. ProductionPanel's
            // recipe cards) would simultaneously select/clear a world building on the same
            // frame, closing the panel the player was just interacting with.
            if (overUI) return;

            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -worldCamera.transform.position.z));
            GridCoord cell = gameRuntime.Grid.WorldToCell(world);
            object occupant = gameRuntime.Grid.GetOccupant(cell);

            // A construction site's pending segment already occupies the grid - that is what stops
            // anything else being placed on it - but it is not the building it will become: nothing
            // has been delivered, it produces nothing, and a Foundry's production panel over it
            // would be a panel about a machine that does not exist yet.
            //
            // The click is routed to the site's own panel rather than dropped. What a player wants
            // from a blue silhouette is what it is waiting for, which is a different question about
            // the same cell - hence a different selection slot, not a different cast of the same one.
            // TryGetSiteContaining only matches segments that have not materialized yet, so the
            // first conveyor of a three-segment run opens its own panel as soon as it is built while
            // its two siblings still open the site's.
            if (occupant is BuildingRuntime pendingSegment
                && gameRuntime.ConstructionSites != null
                && gameRuntime.ConstructionSites.TryGetSiteContaining(pendingSegment, out ConstructionSiteRuntime site))
            {
                storagePanel.Hide();
                gameRuntime.Selection.SelectSite(site);
                return;
            }

            if (TryShowPanelFor(occupant)) return;

            // The parked fleet is not a grid occupant - it is a view standing on free ground - so it
            // is asked for separately, after the occupant lookup came back with nothing. Clicking a
            // robot opens the map, which is the only thing there is to do with one: a mission is
            // designated out there, not here.
            if (gameRuntime.ExplorerRobotStandsOn(cell))
            {
                storagePanel.Hide();
                gameRuntime.Selection.OpenGlobalPanel(SectorMapPanelController.PanelName);
                return;
            }

            storagePanel.Hide();
            gameRuntime.Selection.Clear();
        }

        /// <summary>
        /// Opens the panel a building deserves, and answers whether it had one at all. The single
        /// map from a building to its panel: a click lands here, and so does the construction site
        /// panel's handover when a site finishes, so a type gaining a panel is never something to
        /// remember in two routers.
        ///
        /// Explicit type checks, not "is BuildingRuntime": only types with an actual info panel may
        /// become the selection, otherwise a conveyor would block world input (IsUIBlockingInput)
        /// with no panel able to clear it. ProductionBuildingRuntime is checked at family level
        /// because every current and near-future one shares ProductionPanelController - as safe as
        /// the single-type checks, just for the whole family at once.
        /// </summary>
        public bool TryShowPanelFor(object occupant)
        {
            switch (occupant)
            {
                case StorageRuntime storage:
                    storagePanel.Show(storage);
                    return true;
                case ExtractorRuntime extractor:
                    gameRuntime.Selection.Select(extractor);
                    return true;
                case ProductionBuildingRuntime production:
                    gameRuntime.Selection.Select(production);
                    return true;
                case PowerplantGazRuntime powerplantGaz:
                    gameRuntime.Selection.Select(powerplantGaz);
                    return true;
                case DataCenterRuntime dataCenter:
                    gameRuntime.Selection.Select(dataCenter);
                    return true;
                case CoreRuntime core:
                    gameRuntime.Selection.Select(core);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// True when the click landed on a real (pickable) UI element rather than on the world -
        /// the case that first needed it being a ProductionPanel recipe card, which also read as a
        /// world click and cleared the selection, closing the panel it had just been clicked in.
        ///
        /// Delegated so the camera and this share one rule rather than two copies of it, Y flip
        /// included. See PointerOverUI.
        /// </summary>
        bool IsPointerOverUI(Vector2 screenPos) => PointerOverUI.At(uiDocument, screenPos);
    }
}

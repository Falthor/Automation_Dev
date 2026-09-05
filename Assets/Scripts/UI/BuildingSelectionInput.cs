using Game.Core;
using Game.Gameplay.Buildings;
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
            // anything else being placed on it - but it is not built yet: nothing has been
            // delivered, it produces nothing, and it has no state worth showing. Clicking the blue
            // silhouette of an empty Foundry must not open its production panel.
            //
            // Neutralized rather than ignored, so the click still closes whatever panel was open,
            // exactly like a click on bare ground. TryGetSiteContaining only matches segments that
            // have not materialized yet, so the first conveyor of a three-segment run stays
            // selectable as soon as it is built while its two siblings do not.
            if (occupant is BuildingRuntime pendingSegment
                && gameRuntime.ConstructionSites != null
                && gameRuntime.ConstructionSites.TryGetSiteContaining(pendingSegment, out _))
            {
                occupant = null;
            }

            if (occupant is StorageRuntime storage)
            {
                storagePanel.Show(storage);
            }
            else if (occupant is ExtractorRuntime extractor)
            {
                // Explicit type check, not "is BuildingRuntime": only building types with an
                // actual info panel may become the selection, otherwise clicking e.g. a conveyor
                // would block world input (IsUIBlockingInput) with no panel able to clear it.
                gameRuntime.Selection.Select(extractor);
            }
            else if (occupant is ProductionBuildingRuntime production)
            {
                // Family-level check (not a blanket "is BuildingRuntime"): every current and
                // near-future ProductionBuildingRuntime shares ProductionPanelController, so this
                // is as safe as the single-type checks above, just for the whole family at once.
                gameRuntime.Selection.Select(production);
            }
            else if (occupant is PowerplantGazRuntime powerplantGaz)
            {
                gameRuntime.Selection.Select(powerplantGaz);
            }
            else if (occupant is DataCenterRuntime dataCenter)
            {
                gameRuntime.Selection.Select(dataCenter);
            }
            else if (occupant is CoreRuntime core)
            {
                gameRuntime.Selection.Select(core);
            }
            else
            {
                storagePanel.Hide();
                gameRuntime.Selection.Clear();
            }
        }

        /// <summary>
        /// True when the click landed on a real (pickable) UI element rather than on the world.
        /// The mouse position comes in with the origin at the screen's bottom-left while a UI
        /// Toolkit panel's coordinates start at its top-left, and ScreenToPanel does not flip
        /// that axis itself - passing the raw position picks a vertically mirrored point, which
        /// reported "no UI here" for clicks that did hit a panel (a ProductionPanel recipe card
        /// then also read as a world click and cleared the selection, closing the panel).
        /// </summary>
        bool IsPointerOverUI(Vector2 screenPos)
        {
            if (uiDocument == null) return false;

            IPanel panel = uiDocument.rootVisualElement?.panel;
            if (panel == null) return false;

            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            return panel.Pick(panelPos) != null;
        }
    }
}

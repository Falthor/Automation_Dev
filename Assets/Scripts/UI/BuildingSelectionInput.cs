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
            // Only a global panel (Storage/Building/Research) blocks re-routing a click here - an
            // already-open per-building panel (SelectedBuilding != null, also part of
            // IsUIBlockingInput) must NOT block it, otherwise clicking a different building while
            // one is selected - or clicking empty space to close it - would never register.
            if (gameRuntime.Selection.ActiveGlobalPanel != null || gameRuntime.LastMenuCloseFrame == Time.frameCount) return;

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Vector2 screenPos = mouse.position.ReadValue();

            // A click that actually lands on a real UI element (a recipe card, a tab button, a
            // panel's own content) must never also be treated as a world click - otherwise
            // clicking something inside an open per-building panel (e.g. ProductionPanel's
            // recipe cards) would simultaneously select/clear a world building on the same
            // frame, closing the panel the player was just interacting with. Global panels are
            // already excluded above; this additionally covers a per-building panel's own content.
            if (uiDocument != null && uiDocument.rootVisualElement?.panel != null)
            {
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(uiDocument.rootVisualElement.panel, screenPos);
                if (uiDocument.rootVisualElement.panel.Pick(panelPos) != null) return;
            }

            Vector3 world = worldCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, -worldCamera.transform.position.z));
            GridCoord cell = gameRuntime.Grid.WorldToCell(world);
            object occupant = gameRuntime.Grid.GetOccupant(cell);

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
            else if (occupant is LaboratoryRuntime laboratory)
            {
                gameRuntime.Selection.Select(laboratory);
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
    }
}

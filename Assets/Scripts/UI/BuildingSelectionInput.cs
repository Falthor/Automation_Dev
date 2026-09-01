using Game.Core;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

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

        void Start()
        {
            if (worldCamera == null) worldCamera = Camera.main;
        }

        void Update()
        {
            if (worldCamera == null || gameRuntime == null || storagePanel == null) return;
            if (gameRuntime.Construction.Selected != null) return;
            if (gameRuntime.IsUIBlockingInput || gameRuntime.LastMenuCloseFrame == Time.frameCount) return;

            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return;

            Vector2 screenPos = mouse.position.ReadValue();
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
            else
            {
                storagePanel.Hide();
                gameRuntime.Selection.Clear();
            }
        }
    }
}

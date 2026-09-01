using Game.Core;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.UI
{
    /// <summary>
    /// Left-click selection for Storage boxes, active only while no construction tool is
    /// selected (ConstructionInputAdapter owns left-click while placing/demolishing, so there
    /// is no double-handling). Clicking a Storage opens its panel; clicking anything else
    /// closes it - a plain grid-cell lookup, no colliders or physics involved.
    /// </summary>
    public sealed class StorageSelectionInput : MonoBehaviour
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

            if (gameRuntime.Grid.GetOccupant(cell) is StorageRuntime storage)
            {
                storagePanel.Show(storage);
            }
            else
            {
                storagePanel.Hide();
            }
        }
    }
}

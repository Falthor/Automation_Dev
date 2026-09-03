using System;
using Game.Gameplay.Buildings;

namespace Game.Gameplay.Selection
{
    /// <summary>
    /// Owns the currently inspected building and the global UI-panel selection state
    /// (CONTRACTS.md §7). The two are mutually exclusive: opening a global panel clears the
    /// building selection and vice versa, matching the source project's behavior.
    /// </summary>
    public sealed class SelectionRuntime
    {
        public BuildingRuntime SelectedBuilding { get; private set; }
        public string ActiveGlobalPanel { get; private set; }

        public event Action<BuildingRuntime> SelectionChanged;
        public event Action<string> GlobalPanelChanged;

        public void Select(BuildingRuntime building)
        {
            if (ActiveGlobalPanel != null) CloseGlobalPanel();

            SelectedBuilding = building;
            SelectionChanged?.Invoke(building);
        }

        public void Clear()
        {
            if (SelectedBuilding == null) return;

            SelectedBuilding = null;
            SelectionChanged?.Invoke(null);
        }

        public BuildingRuntime GetSelectedBuilding() => SelectedBuilding;

        /// <summary>Opens a named global panel, closing whichever one was open before (no-op if already active).</summary>
        public void OpenGlobalPanel(string name)
        {
            if (ActiveGlobalPanel == name) return;
            if (SelectedBuilding != null) Clear();

            ActiveGlobalPanel = name;
            GlobalPanelChanged?.Invoke(name);
        }

        public void CloseGlobalPanel()
        {
            if (ActiveGlobalPanel == null) return;

            ActiveGlobalPanel = null;
            GlobalPanelChanged?.Invoke(null);
        }
    }
}

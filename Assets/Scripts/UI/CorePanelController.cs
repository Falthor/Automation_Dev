using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Contextual Core inspector - shows the Core's own inventory (never Storage contents, see
    /// StoragePanel for the merged global view) plus the global Power/Compute aggregate supply
    /// (CONTRACTS.md §9/§10/§12) - not Core-specific numbers, matching the source project.
    /// </summary>
    public sealed class CorePanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        VisualElement _root;
        Label _computeLabel;
        Label _powerLabel;
        VisualElement _itemsList;
        CoreRuntime _selected;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("CorePanelRoot");
            _computeLabel = panelRoot.Q<Label>("CoreComputeLabel");
            _powerLabel = panelRoot.Q<Label>("CorePowerLabel");
            _itemsList = panelRoot.Q<VisualElement>("CoreItemsList");
            if (computeIcon != null) panelRoot.Q<VisualElement>("CoreComputeIcon").style.backgroundImage = new StyleBackground(computeIcon);
            if (powerIcon != null) panelRoot.Q<VisualElement>("CorePowerIcon").style.backgroundImage = new StyleBackground(powerIcon);
            panelRoot.Q<Button>("CoreCloseButton").clicked += Close;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy() => gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as CoreRuntime;
            _root.EnableInClassList("hidden", _selected == null);
            if (_selected != null) Render();
        }

        void Close() => gameRuntime.Selection.Clear();

        void Update()
        {
            if (_selected == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            Render();
        }

        void Render()
        {
            _computeLabel.text = $"Compute: {Mathf.RoundToInt(gameRuntime.Compute.IncomePerSecond)} CU/s";
            _powerLabel.text = $"Power: {Mathf.RoundToInt(gameRuntime.Power.SettledSupply)} kW";

            _itemsList.Clear();
            foreach (var kvp in _selected.GetContents())
            {
                if (kvp.Value <= 0) continue;

                var row = new VisualElement();
                row.AddToClassList("production-ingredient-row");

                var icon = new VisualElement();
                icon.AddToClassList("production-ingredient-icon");
                var item = gameRuntime.Items.Get(kvp.Key);
                if (item != null && item.Icon != null) icon.style.backgroundImage = new StyleBackground(item.Icon);
                row.Add(icon);

                var name = new Label(item != null ? item.DisplayName : kvp.Key);
                name.AddToClassList("production-ingredient-name");
                row.Add(name);

                var amount = new Label(kvp.Value.ToString());
                amount.AddToClassList("core-item-amount");
                row.Add(amount);

                _itemsList.Add(row);
            }
        }
    }
}

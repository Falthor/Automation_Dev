using System.Collections.Generic;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Read-only Data Center inspector - DataCenter isn't a ProductionBuildingRuntime (no
    /// player-chosen recipe), so it never shows in ProductionPanel; this is its own minimal
    /// equivalent. Talks to the building only through its public CpuSlots/MemorySlots/
    /// GetTotalComputeOutput/GetTotalPowerDemand contract (CONTRACTS.md §12).
    /// </summary>
    public sealed class DataCenterPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        VisualElement _root;
        Label _computeLabel;
        Label _powerLabel;
        VisualElement _cpuList;
        VisualElement _memoryList;
        DataCenterRuntime _selected;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("DataCenterPanelRoot");
            _computeLabel = panelRoot.Q<Label>("DataCenterComputeLabel");
            _powerLabel = panelRoot.Q<Label>("DataCenterPowerLabel");
            _cpuList = panelRoot.Q<VisualElement>("DataCenterCpuList");
            _memoryList = panelRoot.Q<VisualElement>("DataCenterMemoryList");
            if (computeIcon != null) panelRoot.Q<VisualElement>("DataCenterComputeIcon").style.backgroundImage = new StyleBackground(computeIcon);
            if (powerIcon != null) panelRoot.Q<VisualElement>("DataCenterPowerIcon").style.backgroundImage = new StyleBackground(powerIcon);
            panelRoot.Q<Button>("DataCenterCloseButton").clicked += Close;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy() => gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as DataCenterRuntime;
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
            _computeLabel.text = $"Compute produit: {Mathf.RoundToInt(_selected.GetTotalComputeOutput())} CU/s";
            _powerLabel.text = $"Power consommee: {Mathf.RoundToInt(_selected.GetTotalPowerDemand())} kW";

            RebuildSlotList(_cpuList, _selected.CpuSlots);
            RebuildSlotList(_memoryList, _selected.MemorySlots);
        }

        void RebuildSlotList(VisualElement container, IReadOnlyList<ComponentInstance> slots)
        {
            container.Clear();

            for (int i = 0; i < slots.Count; i++)
            {
                ComponentInstance slot = slots[i];

                var row = new VisualElement();
                row.AddToClassList("datacenter-slot-row");
                row.EnableInClassList("datacenter-slot-empty", slot == null);
                row.EnableInClassList("datacenter-slot-replacing", slot != null && slot.IsReplacing);

                var label = new Label($"[{i}] {SlotText(slot)}");
                label.AddToClassList("datacenter-slot-label");
                row.Add(label);

                container.Add(row);
            }
        }

        string SlotText(ComponentInstance slot)
        {
            if (slot == null) return "vide";

            string name = gameRuntime.Items != null ? gameRuntime.Items.Get(slot.ItemId)?.DisplayName ?? slot.ItemId : slot.ItemId;
            string state = slot.IsReplacing ? "en remplacement" : "actif";
            return $"{name} - usure {Mathf.RoundToInt(slot.Wear)}% - stabilite {Mathf.RoundToInt(slot.Stability)}% - perf {Mathf.RoundToInt(slot.EffectivePerformance * 100f)}% - {state}";
        }
    }
}

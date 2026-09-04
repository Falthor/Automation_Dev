using System.Collections.Generic;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Read-only-except-for-its-own-sliders Data Center inspector (TASK_03_DATACENTER.md §10).
    /// DataCenter isn't a ProductionBuildingRuntime (no player-chosen recipe), so it never shows
    /// in ProductionPanel; this is its own equivalent. Talks to the building only through its
    /// public contract (CpuSlots/MemorySlots, the two axis-production accessors, the two
    /// replacement-threshold setters, the axis-share setter) - never a private field.
    /// </summary>
    public sealed class DataCenterPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        VisualElement _root;
        VisualElement _primingSection;
        VisualElement _primingFill;
        Label _primingLabel;
        Label _computeLabel;
        Label _powerLabel;
        Label _researchAxisLabel;
        Label _buildingsAxisLabel;
        Slider _axisSlider;
        Slider _cpuThresholdSlider;
        Label _cpuThresholdValue;
        Slider _memoryThresholdSlider;
        Label _memoryThresholdValue;
        VisualElement _cpuList;
        VisualElement _memoryList;
        DataCenterRuntime _selected;

        /// <summary>True while a slider callback is itself pushing a value into the runtime - avoids the render pass immediately reading it back and fighting the slider's own drag gesture.</summary>
        bool _applyingSliderChange;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("DataCenterPanelRoot");
            _primingSection = panelRoot.Q<VisualElement>("DataCenterPrimingSection");
            _primingFill = panelRoot.Q<VisualElement>("DataCenterPrimingFill");
            _primingLabel = panelRoot.Q<Label>("DataCenterPrimingLabel");
            _computeLabel = panelRoot.Q<Label>("DataCenterComputeLabel");
            _powerLabel = panelRoot.Q<Label>("DataCenterPowerLabel");
            _researchAxisLabel = panelRoot.Q<Label>("DataCenterResearchAxisLabel");
            _buildingsAxisLabel = panelRoot.Q<Label>("DataCenterBuildingsAxisLabel");
            _axisSlider = panelRoot.Q<Slider>("DataCenterAxisSlider");
            _cpuThresholdSlider = panelRoot.Q<Slider>("DataCenterCpuThresholdSlider");
            _cpuThresholdValue = panelRoot.Q<Label>("DataCenterCpuThresholdValue");
            _memoryThresholdSlider = panelRoot.Q<Slider>("DataCenterMemoryThresholdSlider");
            _memoryThresholdValue = panelRoot.Q<Label>("DataCenterMemoryThresholdValue");
            _cpuList = panelRoot.Q<VisualElement>("DataCenterCpuList");
            _memoryList = panelRoot.Q<VisualElement>("DataCenterMemoryList");
            if (computeIcon != null) panelRoot.Q<VisualElement>("DataCenterComputeIcon").style.backgroundImage = new StyleBackground(computeIcon);
            if (powerIcon != null) panelRoot.Q<VisualElement>("DataCenterPowerIcon").style.backgroundImage = new StyleBackground(powerIcon);
            panelRoot.Q<Button>("DataCenterCloseButton").clicked += Close;

            _axisSlider.RegisterValueChangedCallback(OnAxisSliderChanged);
            _cpuThresholdSlider.RegisterValueChangedCallback(OnCpuThresholdChanged);
            _memoryThresholdSlider.RegisterValueChangedCallback(OnMemoryThresholdChanged);

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

        void OnAxisSliderChanged(ChangeEvent<float> evt)
        {
            if (_selected == null) return;
            _applyingSliderChange = true;
            _selected.SetResearchAxisShare(evt.newValue / 100f);
            _applyingSliderChange = false;
        }

        void OnCpuThresholdChanged(ChangeEvent<float> evt)
        {
            if (_selected == null) return;
            _applyingSliderChange = true;
            _selected.SetCpuReplacementThreshold(evt.newValue);
            _applyingSliderChange = false;
        }

        void OnMemoryThresholdChanged(ChangeEvent<float> evt)
        {
            if (_selected == null) return;
            _applyingSliderChange = true;
            _selected.SetMemoryReplacementThreshold(evt.newValue);
            _applyingSliderChange = false;
        }

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
            bool isPriming = _selected.IsPriming;
            _primingSection.EnableInClassList("hidden", !isPriming);
            if (isPriming)
            {
                _primingFill.style.width = new StyleLength(Length.Percent(_selected.PrimingProgress * 100f));
                _primingLabel.text = $"~{Mathf.CeilToInt(_selected.GetPrimingSecondsRemaining())}s restantes";
            }

            _computeLabel.text = $"Compute produit: {Mathf.RoundToInt(_selected.GetResearchAxisProduction() + _selected.GetBuildingsAxisProduction())} CU/s";
            _powerLabel.text = $"Power consommee: {Mathf.RoundToInt(_selected.GetTotalPowerDemand())} kW";
            _researchAxisLabel.text = $"Recherche: {Mathf.RoundToInt(_selected.GetResearchAxisProduction())} CU/s";
            _buildingsAxisLabel.text = $"Batiments: {Mathf.RoundToInt(_selected.GetBuildingsAxisProduction())} CU/s";

            // A slider mid-drag must not be overwritten by the runtime's own value this same
            // frame (that would fight the pointer); every other frame keeps it in sync with
            // whatever last set the runtime value (e.g. a restored save).
            if (!_applyingSliderChange)
            {
                _axisSlider.SetValueWithoutNotify(_selected.ResearchAxisShare * 100f);
                _cpuThresholdSlider.SetValueWithoutNotify(_selected.CpuReplacementThresholdPercent);
                _memoryThresholdSlider.SetValueWithoutNotify(_selected.MemoryReplacementThresholdPercent);
            }
            _cpuThresholdValue.text = $"{Mathf.RoundToInt(_selected.CpuReplacementThresholdPercent)}%";
            _memoryThresholdValue.text = $"{Mathf.RoundToInt(_selected.MemoryReplacementThresholdPercent)}%";

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

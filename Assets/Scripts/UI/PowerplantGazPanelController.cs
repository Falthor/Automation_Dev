using Game.Data;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Contextual PowerplantGaz inspector - deliberately minimal (no recipe/production controls,
    /// this building has none): just its fuel stock and the current burn-cycle progress, read
    /// live from PowerplantGazRuntime's own public FuelAmount/FuelTimer and its Definition's
    /// FuelItem/FuelCycleTimeSeconds (static content data, CONTRACTS.md §12 allows UI to read it
    /// directly). Same shell/routing as ExtractorPanelController.
    /// </summary>
    public sealed class PowerplantGazPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _root;
        VisualElement _progressFill;
        VisualElement _fuelIcon;
        Label _fuelAmount;
        Label _progressLabel;
        PowerplantGazRuntime _selected;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("PowerplantGazPanelRoot");
            _progressFill = panelRoot.Q<VisualElement>("PowerplantGazProgressFill");
            _fuelIcon = panelRoot.Q<VisualElement>("PowerplantGazFuelIcon");
            _fuelAmount = panelRoot.Q<Label>("PowerplantGazFuelAmount");
            _progressLabel = panelRoot.Q<Label>("PowerplantGazProgressLabel");
            panelRoot.Q<Button>("PowerplantGazCloseButton").clicked += Close;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy() => gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as PowerplantGazRuntime;
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
            var definition = (PowerplantGazDefinition)_selected.Definition;

            if (definition.FuelItem != null && definition.FuelItem.Icon != null)
            {
                _fuelIcon.style.backgroundImage = new StyleBackground(definition.FuelItem.Icon);
            }
            _fuelAmount.text = _selected.FuelAmount.ToString();

            float cycleTime = definition.FuelCycleTimeSeconds;
            float elapsed = _selected.FuelTimer;
            float ratio = cycleTime > 0f ? Mathf.Clamp01(elapsed / cycleTime) : 0f;
            _progressFill.style.width = new StyleLength(Length.Percent(ratio * 100f));
            _progressLabel.text = $"{elapsed:0.0} / {cycleTime:0.0} s";
        }
    }
}

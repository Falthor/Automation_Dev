using Game.Data;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Contextual Laboratory inspector - deliberately minimal, same shape as
    /// PowerplantGazPanelController: the building's own Data Card stock and the current
    /// card-conversion cycle progress, read live from LaboratoryRuntime's public CardTimer and
    /// its Definition's CardItem/CardConvertIntervalSeconds.
    /// </summary>
    public sealed class LaboratoryPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _root;
        VisualElement _progressFill;
        VisualElement _cardIcon;
        Label _cardAmount;
        Label _progressLabel;
        LaboratoryRuntime _selected;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("LaboratoryPanelRoot");
            _progressFill = panelRoot.Q<VisualElement>("LaboratoryProgressFill");
            _cardIcon = panelRoot.Q<VisualElement>("LaboratoryCardIcon");
            _cardAmount = panelRoot.Q<Label>("LaboratoryCardAmount");
            _progressLabel = panelRoot.Q<Label>("LaboratoryProgressLabel");
            panelRoot.Q<Button>("LaboratoryCloseButton").clicked += Close;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy() => gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as LaboratoryRuntime;
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
            var definition = (LaboratoryDefinition)_selected.Definition;

            if (definition.CardItem != null && definition.CardItem.Icon != null)
            {
                _cardIcon.style.backgroundImage = new StyleBackground(definition.CardItem.Icon);
            }
            _cardAmount.text = definition.CardItem != null ? _selected.GetInputAmount(definition.CardItem.Id).ToString() : "0";

            float cycleTime = definition.CardConvertIntervalSeconds;
            float elapsed = _selected.CardTimer;
            float ratio = cycleTime > 0f ? Mathf.Clamp01(elapsed / cycleTime) : 0f;
            _progressFill.style.width = new StyleLength(Length.Percent(ratio * 100f));
            _progressLabel.text = $"{elapsed:0.0} / {cycleTime:0.0} s";
        }
    }
}

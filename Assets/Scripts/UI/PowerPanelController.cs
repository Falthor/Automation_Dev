using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Power panel (CONTRACTS.md §9/§12), opened from the Top Bar's Power card:
    /// Consumption/Production/Balance + saturation bar, plus a 5-minute history graph
    /// (supply vs demand, sampled every 5s) matching the source project's power_panel.gd.
    /// </summary>
    public sealed class PowerPanelController : MonoBehaviour
    {
        public const string PanelName = "power";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        const float SampleInterval = 5f;
        static readonly Color GraphColorA = new Color(0.333f, 0.867f, 0.961f, 1f);
        static readonly Color GraphColorB = new Color(0.91f, 0.66f, 0.24f, 1f);

        static readonly Color DeficitColor = new Color(0.949f, 0.325f, 0.325f, 1f);
        static readonly Color NormalColor = new Color(0.843f, 0.878f, 0.898f, 1f);

        VisualElement _root;
        Label _consumption;
        Label _production;
        Label _balance;
        VisualElement _barFill;
        HistoryGraphElement _graph;
        float _sampleTimer;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("PowerPanelRoot");
            _consumption = panelRoot.Q<Label>("PowerConsumptionLabel");
            _production = panelRoot.Q<Label>("PowerProductionLabel");
            _balance = panelRoot.Q<Label>("PowerBalanceLabel");
            _barFill = panelRoot.Q<VisualElement>("PowerBarFill");
            panelRoot.Q<Button>("PowerCloseButton").clicked += Hide;

            _graph = new HistoryGraphElement(GraphColorA, GraphColorB);
            panelRoot.Q<VisualElement>("PowerGraphContainer").Add(_graph);
            Sample();

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName) => _root.EnableInClassList("hidden", panelName != PanelName);

        void Hide()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            gameRuntime.Selection.CloseGlobalPanel();
        }

        void Update()
        {
            // Sampling runs regardless of whether the panel is open, matching the source
            // project's power_panel.gd - the history keeps accumulating in the background.
            _sampleTimer += Time.deltaTime;
            if (_sampleTimer >= SampleInterval)
            {
                _sampleTimer = 0f;
                Sample();
            }

            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            Refresh();
        }

        void Sample() => _graph.AddSample(gameRuntime.Power.SettledSupply, gameRuntime.Power.SettledDemand);

        void Refresh()
        {
            var power = gameRuntime.Power;
            float supply = power.SettledSupply;
            float demand = power.SettledDemand;
            bool deficit = demand > supply;
            float balance = supply - demand;
            string sign = balance >= 0f ? "+" : "";

            _consumption.text = $"Consumption: {Mathf.RoundToInt(demand)} kW";
            _production.text = $"Production: {Mathf.RoundToInt(supply)} kW";
            _balance.text = $"Balance: {sign}{Mathf.RoundToInt(balance)} kW";
            _balance.style.color = deficit ? DeficitColor : NormalColor;

            float ratio = supply > 0f ? Mathf.Clamp01(demand / supply) : 0f;
            _barFill.style.width = new StyleLength(Length.Percent(ratio * 100f));
        }
    }
}

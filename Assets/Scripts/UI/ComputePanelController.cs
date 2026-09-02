using System.Globalization;
using Game.Gameplay.Compute;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Compute panel (CONTRACTS.md §10/§12), opened from the Top Bar's Compute card.
    /// CU is a pooled currency: the panel shows how much is banked against the cap, and the rate
    /// it is being credited at, plus a 5-minute history graph of the reserve (sampled every 5s).
    /// There is no continuous-draw figure - CU is only ever spent in one shot when a production
    /// cycle starts, so there is no per-second consumption to report.
    /// </summary>
    public sealed class ComputePanelController : MonoBehaviour
    {
        public const string PanelName = "compute";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        const float SampleInterval = 5f;
        static readonly Color GraphColorA = new Color(0.333f, 0.867f, 0.961f, 1f);
        static readonly Color GraphColorB = new Color(0.91f, 0.66f, 0.24f, 1f);

        VisualElement _root;
        Label _available;
        Label _production;
        VisualElement _barFill;
        HistoryGraphElement _graph;
        float _sampleTimer;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ComputePanelRoot");
            _available = panelRoot.Q<Label>("ComputeAvailableLabel");
            _production = panelRoot.Q<Label>("ComputeProductionLabel");
            _barFill = panelRoot.Q<VisualElement>("ComputeBarFill");
            panelRoot.Q<Button>("ComputeCloseButton").clicked += Hide;

            _graph = new HistoryGraphElement(GraphColorA, GraphColorB);
            panelRoot.Q<VisualElement>("ComputeGraphContainer").Add(_graph);
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

        // Series B is the reserve cap, drawn as a flat ceiling line the reserve curve is read against.
        void Sample() => _graph.AddSample(gameRuntime.Compute.Reserve, ComputeSystem.ReserveCap);

        void Refresh()
        {
            var compute = gameRuntime.Compute;

            _available.text = $"Reserve: {FormatThousands(compute.Reserve)} / {FormatThousands(ComputeSystem.ReserveCap)} CU";
            _production.text = $"Production: {Mathf.RoundToInt(compute.IncomePerSecond)} CU/s";

            _barFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(compute.Reserve / ComputeSystem.ReserveCap) * 100f));
        }

        static string FormatThousands(float value)
        {
            return Mathf.RoundToInt(value).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}

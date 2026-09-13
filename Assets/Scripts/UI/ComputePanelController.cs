using System.Globalization;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Compute panel (CALCUL.md and UI.md), opened from the Top Bar's Building Compute or
    /// Research Compute element. CU is a pooled currency: the panel shows how much is banked
    /// against the cap, and the rate it is being credited at, plus a 5-minute history graph of the
    /// reserve (sampled every 5s). There is no continuous-draw figure for Building Compute - CU is
    /// only ever spent in one shot when a production cycle starts - though Research Compute's own
    /// continuous absorption still shows through the reserve curve itself.
    ///
    /// One class, two scene instances (CALCUL.md's two typed reserves): <see cref="pool"/> picks
    /// which of GameRuntime's reserves this instance shows, which is also what <see cref="PanelName"/>
    /// and the panel's own title are derived from - never a second copy of this class.
    /// </summary>
    public sealed class ComputePanelController : MonoBehaviour
    {
        public const string BuildingPanelName = "compute-building";
        public const string ResearchPanelName = "compute-research";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        [Tooltip("Which of GameRuntime's two reserves this panel instance shows.")]
        [SerializeField] ComputeSource pool = ComputeSource.Building;

        const float SampleInterval = 5f;
        static readonly Color BuildingGraphColorA = new Color(0.392f, 0.824f, 0.51f, 1f);
        static readonly Color ResearchGraphColorA = new Color(0.333f, 0.867f, 0.961f, 1f);
        static readonly Color GraphColorB = new Color(0.91f, 0.66f, 0.24f, 1f);

        VisualElement _root;
        Label _title;
        Label _available;
        Label _production;
        VisualElement _barFill;
        HistoryGraphElement _graph;
        float _sampleTimer;

        public string PanelName => pool == ComputeSource.Research ? ResearchPanelName : BuildingPanelName;

        ComputeSystem Reserve => pool == ComputeSource.Research ? gameRuntime.ResearchCompute : gameRuntime.BuildingCompute;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ComputePanelRoot");
            _title = panelRoot.Q<Label>("ComputeTitle");
            if (_title != null) _title.text = pool == ComputeSource.Research ? "RESEARCH COMPUTE" : "BUILDING COMPUTE";
            _available = panelRoot.Q<Label>("ComputeAvailableLabel");
            _production = panelRoot.Q<Label>("ComputeProductionLabel");
            _barFill = panelRoot.Q<VisualElement>("ComputeBarFill");
            panelRoot.Q<Button>("ComputeCloseButton").clicked += Hide;

            _graph = new HistoryGraphElement(pool == ComputeSource.Research ? ResearchGraphColorA : BuildingGraphColorA, GraphColorB);
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

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.GlobalPanel))
            {
                Hide();
                return;
            }

            Refresh();
        }

        // Series B is the reserve cap, drawn as a flat ceiling line the reserve curve is read against.
        void Sample() => _graph.AddSample(Reserve.Reserve, ComputeSystem.ReserveCap);

        void Refresh()
        {
            ComputeSystem reserve = Reserve;

            _available.text = $"Reserve: {FormatThousands(reserve.Reserve)} / {FormatThousands(ComputeSystem.ReserveCap)} CU";
            _production.text = $"Production: {Mathf.RoundToInt(reserve.IncomePerSecond)} CU/s";

            _barFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(reserve.Reserve / ComputeSystem.ReserveCap) * 100f));
        }

        static string FormatThousands(float value)
        {
            return Mathf.RoundToInt(value).ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}

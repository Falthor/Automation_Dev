using Game.Data;
using Game.Gameplay.Compute;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Global Top Bar (GLOBAL_UI.md §2-4): three compact status cards (Power/Compute/Research),
    /// each a pure view over an existing runtime system - no duplicated state, no new
    /// simulation. Hover expands a card in place to reveal its detail block; click opens the
    /// matching global panel through the same Selection routing every other panel uses. Menu is
    /// a reserved, non-functional icon (per spec); Pause freezes simulation via Time.timeScale,
    /// which every deltaTime-scaled system (Transport/Research/Power/Compute) already respects
    /// with no new per-system pause flag needed.
    /// </summary>
    public sealed class TopBarController : MonoBehaviour
    {
        const float ReferenceWidth = 1920f;
        const float CollapsedHeight = 28f;

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        [Header("Top Bar icons (placeholder - swap later)")]
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;
        [SerializeField] Sprite researchIcon;

        VisualElement _cardsRow;
        Label _pauseOverlay;
        bool _paused;

        Card _powerCard;
        Card _computeCard;
        Card _researchCard;

        /// <summary>One built card's live widgets, plus the responsive-width bounds it was configured with.</summary>
        sealed class Card
        {
            public VisualElement Root;
            public Label Value;
            public VisualElement Detail;
            public VisualElement BarFill;
            public Label[] Lines;
            public float RefWidth, MinWidth, MaxWidth;
            public float DetailHeight;
        }

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _cardsRow = panelRoot.Q<VisualElement>("TopBarCardsRow");
            _pauseOverlay = panelRoot.Q<Label>("TopBarPauseOverlay");

            // Menu is a reserved, non-functional placeholder (GLOBAL_UI.md §3) - no handler.
            panelRoot.Q<Button>("TopBarPauseButton").clicked += TogglePause;

            // Base widths reduced per user feedback ("moins large") - hover only expands height
            // (SetExpanded below), never width, so this has no effect on the hover-expand behavior.
            _powerCard = BuildCard(powerIcon, PowerPanelController.PanelName, 170f, 130f, 210f, 66f, 3, "top-bar-card-bar-fill-power");
            _computeCard = BuildCard(computeIcon, ComputePanelController.PanelName, 190f, 145f, 230f, 56f, 2, "top-bar-card-bar-fill-compute");
            _researchCard = BuildCard(researchIcon, ResearchPanelController.PanelName, 170f, 130f, 210f, 56f, 2, "top-bar-card-bar-fill-research");
        }

        Card BuildCard(Sprite icon, string panelName, float refWidth, float minWidth, float maxWidth, float detailHeight, int lineCount, string barFillClass)
        {
            var card = new Card { RefWidth = refWidth, MinWidth = minWidth, MaxWidth = maxWidth, DetailHeight = detailHeight };

            var root = new Button(() => gameRuntime.Selection.OpenGlobalPanel(panelName)) { text = string.Empty };
            root.AddToClassList("top-bar-card");
            card.Root = root;

            var header = new VisualElement();
            header.AddToClassList("top-bar-card-header");

            var iconElement = new VisualElement();
            iconElement.AddToClassList("top-bar-card-icon");
            if (icon != null) iconElement.style.backgroundImage = new StyleBackground(icon);
            header.Add(iconElement);

            var value = new Label();
            value.AddToClassList("top-bar-card-value");
            header.Add(value);
            card.Value = value;

            root.Add(header);

            var detail = new VisualElement();
            detail.AddToClassList("top-bar-card-detail");
            card.Detail = detail;

            var lines = new Label[lineCount];
            for (int i = 0; i < lineCount; i++)
            {
                var line = new Label();
                line.AddToClassList("top-bar-card-detail-line");
                detail.Add(line);
                lines[i] = line;
            }
            card.Lines = lines;

            var barTrack = new VisualElement();
            barTrack.AddToClassList("top-bar-card-bar-track");
            var barFill = new VisualElement();
            barFill.AddToClassList(barFillClass);
            barTrack.Add(barFill);
            detail.Add(barTrack);
            card.BarFill = barFill;

            root.Add(detail);

            root.RegisterCallback<MouseEnterEvent>(_ => SetExpanded(card, true));
            root.RegisterCallback<MouseLeaveEvent>(_ => SetExpanded(card, false));

            _cardsRow.Add(root);
            return card;
        }

        static void SetExpanded(Card card, bool expanded)
        {
            card.Root.EnableInClassList("top-bar-card-collapsing", !expanded);
            card.Detail.EnableInClassList("top-bar-card-collapsing", !expanded);
            card.Root.style.height = expanded ? CollapsedHeight + card.DetailHeight : CollapsedHeight;
            card.Detail.style.height = expanded ? card.DetailHeight : 0f;
            // Bottom padding is toggled here, not left as a constant in CSS: a fixed padding-
            // bottom persists even at height:0, leaving a sliver of the detail lines' text
            // visible under the collapsed card (the "peek" reported by feedback) - it must
            // collapse to exactly 0 together with height, not stay reserved.
            card.Detail.style.paddingBottom = expanded ? 6f : 0f;
        }

        void TogglePause()
        {
            _paused = !_paused;
            Time.timeScale = _paused ? 0f : 1f;
            _pauseOverlay.EnableInClassList("hidden", !_paused);
        }

        void Update()
        {
            RefreshWidths();
            RefreshPower();
            RefreshCompute();
            RefreshResearch();
        }

        void RefreshWidths()
        {
            float widthScale = Screen.width / ReferenceWidth;
            _powerCard.Root.style.width = ClampedWidth(_powerCard, widthScale);
            _computeCard.Root.style.width = ClampedWidth(_computeCard, widthScale);
            _researchCard.Root.style.width = ClampedWidth(_researchCard, widthScale);
        }

        static float ClampedWidth(Card card, float widthScale)
        {
            return Mathf.Clamp(card.RefWidth * widthScale, card.MinWidth, card.MaxWidth);
        }

        void RefreshPower()
        {
            var power = gameRuntime.Power;
            float supply = power.SettledSupply;
            float demand = power.SettledDemand;
            bool deficit = demand > supply;
            float balance = supply - demand;
            string sign = balance >= 0f ? "+" : "";

            _powerCard.Value.text = $"{Mathf.RoundToInt(demand)} / {Mathf.RoundToInt(supply)} kW";
            _powerCard.Value.EnableInClassList("top-bar-card-value-deficit", deficit);

            _powerCard.Lines[0].text = $"Consumption: {Mathf.RoundToInt(demand)} kW";
            _powerCard.Lines[1].text = $"Production: {Mathf.RoundToInt(supply)} kW";
            _powerCard.Lines[2].text = $"Balance: {sign}{Mathf.RoundToInt(balance)} kW";
            _powerCard.Lines[2].EnableInClassList("top-bar-card-detail-line-deficit", deficit);

            float ratio = supply > 0f ? Mathf.Clamp01(demand / supply) : 0f;
            _powerCard.BarFill.style.width = new StyleLength(Length.Percent(ratio * 100f));
        }

        void RefreshCompute()
        {
            var compute = gameRuntime.Compute;

            _computeCard.Value.text = $"{FormatThousands(compute.Reserve)} CU";

            // No continuous-draw line: CU is spent in one shot when a production cycle starts,
            // so there is no per-second consumption to show - only the banked reserve and the
            // rate it refills at.
            _computeCard.Lines[0].text = $"Reserve: {FormatThousands(compute.Reserve)} / {FormatThousands(ComputeSystem.ReserveCap)} CU";
            _computeCard.Lines[1].text = $"Production: {Mathf.RoundToInt(compute.IncomePerSecond)} CU/s";

            _computeCard.BarFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(compute.Reserve / ComputeSystem.ReserveCap) * 100f));
        }

        void RefreshResearch()
        {
            var research = gameRuntime.Research;

            if (research.HasActiveResearch())
            {
                ResearchDefinition active = research.GetActiveResearch();
                float progress = research.GetProgress();

                _researchCard.Value.text = active.DisplayName;
                _researchCard.Lines[0].text = $"{Mathf.RoundToInt(progress * 100f)}%";
                _researchCard.Lines[1].text = $"Temps restant  {FormatTime(research.GetEstimatedSecondsRemaining())}";
                _researchCard.BarFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(progress) * 100f));
            }
            else
            {
                int queued = research.GetQueue().Count;
                _researchCard.Value.text = queued > 0 ? $"{queued} en file" : "Aucune";
                _researchCard.Lines[0].text = "0%";
                _researchCard.Lines[1].text = "Temps restant  --:--";
                _researchCard.BarFill.style.width = new StyleLength(Length.Percent(0f));
            }
        }

        static string FormatTime(float seconds)
        {
            int s = Mathf.Max(Mathf.RoundToInt(seconds), 0);
            return $"{s / 60:00}:{s % 60:00}";
        }

        static string FormatThousands(float value)
        {
            return Mathf.RoundToInt(value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

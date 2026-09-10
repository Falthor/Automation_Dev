using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Directives;
using Game.Gameplay.Session;
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

        /// <summary>Occupied slots within this many of the cap turn the counter's alert color on (TASK_04_PLAFOND_RAYON.md §3.2/§5) - an arbitrary but reasonable "approaching the limit" band, not a value the ticket pins down.</summary>
        const int BuildingCapAlertMargin = 5;

        /// <summary>How long a refusal message (ShowRefusalMessage) stays visible before auto-hiding.</summary>
        const float RefusalMessageSeconds = 2.5f;

        /// <summary>Share of production being drawn past which the Power card turns amber - a warning that the network is nearly saturated, distinct from the red of an actual deficit.</summary>
        const float PowerStrainThreshold = 0.85f;

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        /// <summary>Optional - when assigned, its PlacementRefused event drives ShowRefusalMessage - the building cap (TASK_04_PLAFOND_RAYON.md §3.2) and insufficient resources. UI may depend on Presentation (PROJECT_ARCHITECTURE.md §4), so the reference lives here, not on the adapter.</summary>
        [SerializeField] ConstructionInputAdapter constructionInputAdapter;

        [Header("Top Bar icons (placeholder - swap later)")]
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;
        [SerializeField] Sprite researchIcon;
        [SerializeField] Sprite buildingIcon;

        VisualElement _cardsRow;
        Label _clock;
        Label _pauseOverlay;
        Label _refusalMessage;
        float _refusalMessageHideAt = -1f;
        bool _paused;

        /// <summary>False until the player has opened the Research panel once - what ends the "this is new" pulse on the card the Core just handed them.</summary>
        bool _researchMenuSeen;

        Card _powerCard;
        Card _computeCard;
        Card _directiveCard;
        Card _researchCard;
        Card _buildingCard;

        /// <summary>One built card's live widgets, plus the responsive-width bounds it was configured with.</summary>
        sealed class Card
        {
            public VisualElement Root;
            public Label Value;
            public VisualElement Detail;
            public VisualElement BarFill;
            public Label[] Lines;

            /// <summary>Only the directive card has this: what it asks for is a list that changes with the directive, so its header is rebuilt rather than filled in.</summary>
            public VisualElement Requirements;

            public float RefWidth, MinWidth, MaxWidth;
            public float DetailHeight;
        }

        InputAction _pause;

        void Start()
        {
            _pause = InputBindings.Find(InputActionCatalogue.Pause);

            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _cardsRow = panelRoot.Q<VisualElement>("TopBarCardsRow");
            _clock = panelRoot.Q<Label>("TopBarClock");
            _pauseOverlay = panelRoot.Q<Label>("TopBarPauseOverlay");
            _refusalMessage = panelRoot.Q<Label>("TopBarRefusalMessage");

            // Menu is a reserved, non-functional placeholder (GLOBAL_UI.md §3) - no handler.
            panelRoot.Q<Button>("TopBarPauseButton").clicked += TogglePause;

            ReleaseFocusAfterAClick();

            // Base widths reduced per user feedback ("moins large") - hover only expands height
            // (SetExpanded below), never width, so this has no effect on the hover-expand behavior.
            _powerCard = BuildCard(powerIcon, PowerPanelController.PanelName, 170f, 130f, 210f, 66f, 3, "top-bar-card-bar-fill-power");
            _computeCard = BuildCard(computeIcon, ComputePanelController.PanelName, 190f, 145f, 230f, 56f, 2, "top-bar-card-bar-fill-compute");
            // Built between Compute and Research so the row keeps one order for the whole run: the
            // directive card is what stands there before Research exists, and the two coexist from
            // the second directive on rather than one taking the other's place.
            _directiveCard = BuildDirectiveCard();
            _researchCard = BuildCard(researchIcon, ResearchPanelController.PanelName, 170f, 130f, 210f, 56f, 2, "top-bar-card-bar-fill-research");
            _buildingCard = BuildCard(buildingIcon, BuildingMenuController.PanelName, 150f, 115f, 190f, 40f, 1, "top-bar-card-bar-fill-buildings");

            if (constructionInputAdapter != null) constructionInputAdapter.PlacementRefused += ShowRefusalMessage;
        }

        /// <summary>Flashes an explicit refusal reason (e.g. the building cap) near the cards row for RefusalMessageSeconds, then auto-hides (TASK_04_PLAFOND_RAYON.md §3.2). Re-showing while already visible just resets the timer.</summary>
        public void ShowRefusalMessage(string text)
        {
            if (_refusalMessage == null) return;
            _refusalMessage.text = text;
            _refusalMessage.EnableInClassList("hidden", false);
            _refusalMessageHideAt = Time.unscaledTime + RefusalMessageSeconds;
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

        /// <summary>
        /// The card for what the Core is currently asking for: a row of item chips, and nothing
        /// else. No detail block and no hover-expand, unlike every other card - what it has to say
        /// is already fully said in the collapsed header, so there would be nothing behind the
        /// expansion but the same numbers written out again.
        ///
        /// Not a Button either: every other card opens a global panel, and a directive lives in the
        /// Core's own inspector, reached by clicking the Core in the world.
        /// </summary>
        Card BuildDirectiveCard()
        {
            var card = new Card { RefWidth = 200f, MinWidth = 150f, MaxWidth = 250f, DetailHeight = 0f };

            var root = new VisualElement();
            root.AddToClassList("top-bar-card");
            card.Root = root;

            var header = new VisualElement();
            header.AddToClassList("top-bar-card-header");

            // Which directive this is, ahead of what it wants: the bill on its own says nothing
            // about how far into the Core's sequence the player is.
            var number = new Label();
            number.AddToClassList("top-bar-directive-number");
            header.Add(number);
            card.Value = number;

            var requirements = new VisualElement();
            requirements.AddToClassList("top-bar-directive-requirements");
            header.Add(requirements);
            card.Requirements = requirements;

            root.Add(header);

            // The card states a bill; the Core panel is where it is read in full and accepted. The
            // player who reads "0/40" on the bar is already asking about the directive, and having
            // to go find the Core on the map to answer that is a detour the bar can spare them.
            root.AddToClassList("top-bar-card-clickable");
            root.RegisterCallback<ClickEvent>(_ => OpenCorePanel());

            _cardsRow.Add(root);
            return card;
        }

        /// <summary>Selects the Core, which is what CorePanelController listens for - the same route a click on the Core itself takes, so there is one way in and not two.</summary>
        void OpenCorePanel()
        {
            CoreRuntime core = gameRuntime.World?.Core;
            if (core == null) return;

            gameRuntime.Selection.Select(core);
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

        /// <summary>
        /// Drops the focus a click just handed to a button.
        ///
        /// <b>Space belongs to the pause, and a focused button was quietly taking it.</b> UI Toolkit
        /// activates a focused <see cref="Button"/> on Space (a NavigationSubmitEvent), so after
        /// clicking anything in the interface, pausing also re-fired that button - the Bottom Nav
        /// reopening a panel, or, at its sharpest, the Pause button itself toggling a second time so
        /// that Space appeared to do nothing at all.
        ///
        /// The focus is what is wrong here, not the pause: <see cref="Update"/> reads Space
        /// deliberately ungated, because pausing from behind an open panel is expected. And this
        /// project activates buttons by clicking them or by their digit shortcut, never by
        /// submitting a focused one, so a clicked button has no use for the focus it was given.
        ///
        /// <b>Registered on the document root rather than per button.</b> Every controller clones
        /// its tree into the same root, so one callback in the bubble phase covers the Top Bar, the
        /// Bottom Nav, the building menu and every panel - eight places to edit, and eight places to
        /// forget, become one. It lives here because this is the component that claims Space.
        ///
        /// Only a <see cref="Button"/> is blurred. A text field must keep the focus a click gives
        /// it, or typing into it would be impossible - which matters from the shortcuts menu on.
        ///
        /// <b>It asks what holds the focus, not what was clicked.</b> A Top Bar card is a Button
        /// containing an icon and labels, and those children take the click for themselves - so the
        /// event's own target is usually not the Button that ended up focused. Reading the focus
        /// controller is the same question the digit shortcuts already ask (UIFocus), and
        /// it is the only form of the question that survives a button with children.
        /// </summary>
        void ReleaseFocusAfterAClick()
        {
            uiDocument.rootVisualElement.RegisterCallback<ClickEvent>(_ =>
            {
                FocusController focus = uiDocument.rootVisualElement.panel?.focusController;
                if (focus?.focusedElement is Button button) button.Blur();
            });
        }

        void Update()
        {
            // Same gesture as the Pause button (GLOBAL_UI.md's Top Bar) - not gated on
            // IsUIBlockingInput, since pausing/resuming from behind an open panel is expected.
            // A clicked button no longer competes for this key - see ReleaseFocusAfterAClick.
            if (InputBindings.WasPressedThisFrame(_pause))
            {
                TogglePause();
            }

            RefreshClock();
            RefreshWidths();
            RefreshPower();
            RefreshCompute();
            RefreshDirective();
            RefreshResearch();
            RefreshBuildings();

            if (_refusalMessageHideAt >= 0f && Time.unscaledTime >= _refusalMessageHideAt)
            {
                _refusalMessage.EnableInClassList("hidden", true);
                _refusalMessageHideAt = -1f;
            }
        }

        /// <summary>
        /// The run's elapsed time. Read every frame from a clock that is itself only advanced by the
        /// simulation's own tick, so this keeps refreshing while paused and keeps showing the same
        /// value - which is exactly what a paused chronometer should do. Nothing here checks whether
        /// the game is paused, and nothing here should.
        /// </summary>
        void RefreshClock()
        {
            if (_clock == null || gameRuntime.Clock == null) return;
            _clock.text = PlayClock.Format(gameRuntime.Clock.ElapsedSeconds);
        }

        void RefreshWidths()
        {
            float widthScale = Screen.width / ReferenceWidth;
            _powerCard.Root.style.width = ClampedWidth(_powerCard, widthScale);
            _computeCard.Root.style.width = ClampedWidth(_computeCard, widthScale);
            _directiveCard.Root.style.width = ClampedWidth(_directiveCard, widthScale);
            _researchCard.Root.style.width = ClampedWidth(_researchCard, widthScale);
            _buildingCard.Root.style.width = ClampedWidth(_buildingCard, widthScale);
        }

        static float ClampedWidth(Card card, float widthScale)
        {
            return Mathf.Clamp(card.RefWidth * widthScale, card.MinWidth, card.MaxWidth);
        }

        /// <summary>
        /// Whether the network is drawing more than PowerStrainThreshold of what it produces: still
        /// working, but one more building away from not.
        ///
        /// False once demand actually exceeds supply, because that is a deficit - a worse thing that
        /// keeps its own red. The amber is the warning before it, not a milder shade of it.
        ///
        /// A plain function of the two figures so the threshold can be pinned without a running
        /// player: read off a live PowerSystem it would only ever report whatever this frame happens
        /// to be.
        /// </summary>
        public static bool IsPowerStrained(float demand, float supply)
        {
            if (demand > supply) return false;
            return supply > 0f && demand / supply > PowerStrainThreshold;
        }

        void RefreshPower()
        {
            var power = gameRuntime.Power;
            float supply = power.SettledSupply;
            float demand = power.SettledDemand;
            bool deficit = demand > supply;
            float balance = supply - demand;
            string sign = balance >= 0f ? "+" : "";

            float usage = supply > 0f ? demand / supply : 0f;
            bool strained = IsPowerStrained(demand, supply);

            _powerCard.Value.text = $"{Mathf.RoundToInt(demand)} / {Mathf.RoundToInt(supply)} kW";
            _powerCard.Value.EnableInClassList("top-bar-card-value-deficit", deficit);
            _powerCard.Value.EnableInClassList("top-bar-card-value-strained", strained);

            _powerCard.Lines[0].text = $"Consumption: {Mathf.RoundToInt(demand)} kW";
            _powerCard.Lines[1].text = $"Production: {Mathf.RoundToInt(supply)} kW";
            _powerCard.Lines[2].text = $"Balance: {sign}{Mathf.RoundToInt(balance)} kW";
            _powerCard.Lines[2].EnableInClassList("top-bar-card-detail-line-deficit", deficit);
            _powerCard.Lines[2].EnableInClassList("top-bar-card-detail-line-strained", strained);

            _powerCard.BarFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(usage) * 100f));
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

        /// <summary>
        /// What the Core is asking for, shown for as long as it is asking.
        ///
        /// Two states, not one. Before validation the card is a shopping list: each requirement
        /// against the aggregate stock a robot could actually go and claim - the same figure, read
        /// the same way, as the Core panel's own, so the two can never disagree. Once the player has
        /// validated, there is nothing left to shop for and the card simply says the convoy is out.
        /// A counter there would be answering a question nobody is asking any more, and answering it
        /// with a number that moves for reasons the player cannot see.
        /// </summary>
        void RefreshDirective()
        {
            CoreDirectiveSystem directives = gameRuntime.CoreDirectives;
            CoreDirectiveDefinition current = directives?.Current;

            _directiveCard.Root.EnableInClassList("hidden", current == null);
            if (current == null) return;

            _directiveCard.Value.text = $"Directive {directives.CurrentNumber}";
            _directiveCard.Requirements.Clear();

            if (directives.IsDelivering)
            {
                var status = new Label("Approvisionnement en cours");
                status.AddToClassList("top-bar-card-value");
                _directiveCard.Requirements.Add(status);
                return;
            }

            IReadOnlyDictionary<string, int> available = gameRuntime.DirectiveStock;
            foreach (RecipeIngredient requirement in current.Requirements)
            {
                if (requirement.Item == null || requirement.Amount <= 0) continue;

                int stock = available != null && available.TryGetValue(requirement.Item.Id, out int inStock) ? inStock : 0;
                _directiveCard.Requirements.Add(BuildDirectiveChip(requirement, Mathf.Min(stock, requirement.Amount)));
            }
        }

        static VisualElement BuildDirectiveChip(RecipeIngredient requirement, int held)
        {
            var chip = new VisualElement();
            chip.AddToClassList("top-bar-directive-chip");

            var icon = new VisualElement();
            icon.AddToClassList("top-bar-directive-icon");
            if (requirement.Item.Icon != null) icon.style.backgroundImage = new StyleBackground(requirement.Item.Icon);
            chip.Add(icon);

            var amount = new Label($"{held}/{requirement.Amount}");
            amount.AddToClassList("top-bar-directive-amount");
            amount.EnableInClassList("top-bar-directive-amount-met", held >= requirement.Amount);
            chip.Add(amount);

            return chip;
        }

        void RefreshResearch()
        {
            var research = gameRuntime.Research;

            // Research is the first directive's reward, so before that there is no card to fill in -
            // and nothing to fill it from. A run with no directives at all keeps it, since then
            // nothing was ever going to hand it over.
            bool menuUnlocked = gameRuntime.CoreDirectives == null || gameRuntime.CoreDirectives.IsResearchMenuUnlocked;
            _researchCard.Root.EnableInClassList("hidden", !menuUnlocked);

            // A card that appears mid-run appears among three the player has long stopped looking
            // at, so it announces itself until they act on it - and then stops, which is the point.
            if (gameRuntime.Selection.ActiveGlobalPanel == ResearchPanelController.PanelName) _researchMenuSeen = true;
            NewUnlockPulse.Apply(_researchCard.Root, menuUnlocked && !_researchMenuSeen);

            if (!menuUnlocked) return;

            if (research.HasActiveResearch())
            {
                ResearchDefinition active = research.GetActiveResearch();
                float progress = research.GetProgress();

                _researchCard.Value.text = active.DisplayName;
                _researchCard.Lines[0].text = $"{Mathf.RoundToInt(progress * 100f)}%";
                _researchCard.Lines[1].text = $"Temps restant  {FormatTime(research.GetEstimatedSecondsRemaining())}";
                _researchCard.BarFill.style.width = new StyleLength(Length.Percent(Mathf.Clamp01(progress) * 100f));

                SetResearchFinished(false);
            }
            else
            {
                int queued = research.GetQueue().Count;
                // "Recherche finie" means the tree is finished, not that something was unlocked
                // once. Read the other way round it fired on the very first unlock - including the
                // one the Core grants for a directive, which the player never researched at all -
                // and announced the end of a tree they had not started.
                bool finished = queued == 0 && IsWholeTreeUnlocked();
                string idleText = finished ? "Recherche finie" : "Aucune";
                _researchCard.Value.text = queued > 0 ? $"{queued} en file" : idleText;
                _researchCard.Lines[0].text = "0%";
                _researchCard.Lines[1].text = "Temps restant  --:--";
                _researchCard.BarFill.style.width = new StyleLength(Length.Percent(0f));

                SetResearchFinished(finished);
            }
        }

        /// <summary>Whether every research the tree lists has been unlocked - asked of the database, the one thing that knows how many there are.</summary>
        bool IsWholeTreeUnlocked()
        {
            ResearchDatabase database = gameRuntime.Researches;
            if (database == null) return false;

            IReadOnlyList<ResearchDefinition> all = database.GetAll();
            if (all.Count == 0) return false;

            foreach (ResearchDefinition research in all)
            {
                if (research != null && !gameRuntime.Research.IsUnlocked(research.Id)) return false;
            }
            return true;
        }

        /// <summary>
        /// The one Top Bar state that is good news rather than a warning: everything researched and
        /// nothing queued. It gets a green frame and a green value, so it reads as finished at a
        /// glance instead of looking like the "Aucune" it sits next to in the same slot. Cleared as
        /// soon as anything is being researched again, or the halo would outlive what it announced.
        /// </summary>
        void SetResearchFinished(bool finished)
        {
            _researchCard.Root.EnableInClassList("top-bar-card-done", finished);
            _researchCard.Value.EnableInClassList("top-bar-card-value-done", finished);
        }

        /// <summary>Occupied/cap counter (TASK_04_PLAFOND_RAYON.md §5) - the second Top Bar figure the survival-phase UI shows, alongside CU. Turns alert-colored within BuildingCapAlertMargin slots of the cap; the cap itself is read live from ConstructionService, so memory_allocation's 40->52 jump shows immediately.</summary>
        void RefreshBuildings()
        {
            var construction = gameRuntime.Construction;
            if (construction == null) return;

            int occupied = construction.OccupiedBuildingSlots;
            int cap = construction.BuildingCap;
            bool approaching = occupied >= cap - BuildingCapAlertMargin;

            _buildingCard.Value.text = $"{occupied} / {cap}";
            _buildingCard.Value.EnableInClassList("top-bar-card-value-deficit", approaching);

            _buildingCard.Lines[0].text = $"Batiments: {occupied} / {cap}";
            _buildingCard.Lines[0].EnableInClassList("top-bar-card-detail-line-deficit", approaching);

            _buildingCard.BarFill.style.width = new StyleLength(Length.Percent(cap > 0 ? Mathf.Clamp01((float)occupied / cap) * 100f : 0f));
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

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
    /// Global Top Bar (UI.md): plain, borderless status elements, each a pure view over an
    /// existing runtime system - no duplicated state, no new simulation. Every element is
    /// directly clickable, opening the matching global panel through the same Selection routing
    /// every other panel uses - there is no hover-to-expand step. Power, Building Compute,
    /// Research Compute and the current Directive sit left of centre, right after the clock;
    /// Research and Buildings sit on the right. Menu opens the in-game menu
    /// (<see cref="GameMenuPanel"/>: save, load, options, quit), whose Options entry hands over to
    /// the shortcuts screen (<see cref="ShortcutsPanel"/>) - a deliberate deviation from the
    /// imported spec, where Menu is a reserved placeholder; Pause freezes simulation via
    /// GameRuntime.SetPaused, which every deltaTime-scaled system (Transport/Research/Power/
    /// Compute) already respects with no new per-system pause flag needed.
    /// </summary>
    public sealed class TopBarController : MonoBehaviour
    {
        /// <summary>Occupied slots within this many of the cap turn the counter's alert color on - an arbitrary but reasonable "approaching the limit" band, not a pinned-down value.</summary>
        const int BuildingCapAlertMargin = 5;

        /// <summary>
        /// Below this many CU a reserve reads as an emergency rather than as a figure.
        ///
        /// 100 is about a single cycle of the cheapest machine: at that point the next thing to ask
        /// for CU will not get it, and the player has to be looking at the element before that
        /// happens rather than after.
        /// </summary>
        const float CriticalReserveCu = 100f;

        /// <summary>How long a refusal message (ShowRefusalMessage) stays visible before auto-hiding.</summary>
        const float RefusalMessageSeconds = 2.5f;

        /// <summary>Share of production being drawn past which the Power element turns amber - a warning that the network is nearly saturated, distinct from the red of an actual deficit.</summary>
        const float PowerStrainThreshold = 0.85f;

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        /// <summary>Optional - when assigned, its PlacementRefused event drives ShowRefusalMessage - the building cap (CONSTRUCTION.md) and insufficient resources. UI may depend on Presentation (PROJECT_ARCHITECTURE.md), so the reference lives here, not on the adapter.</summary>
        [SerializeField] ConstructionInputAdapter constructionInputAdapter;

        [Header("Top Bar icons (placeholder - swap later)")]
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite buildingComputeIcon;
        [SerializeField] Sprite researchComputeIcon;
        [SerializeField] Sprite researchIcon;
        [SerializeField] Sprite buildingIcon;

        VisualElement _leftRow;
        VisualElement _rightRow;
        Label _clock;
        Label _pauseOverlay;
        Label _refusalMessage;
        float _refusalMessageHideAt = -1f;

        /// <summary>False until the player has opened the Research panel once - what ends the "this is new" pulse on the element the Core just handed them.</summary>
        bool _researchMenuSeen;

        Element _powerElement;
        Element _buildingComputeElement;
        Element _researchComputeElement;
        Element _directiveElement;
        Element _researchElement;
        Element _buildingElement;

        /// <summary>
        /// One built element's live widgets. No width of its own: the button is left to Yoga's
        /// ordinary content-based sizing (icon + label, no explicit width set anywhere) so it
        /// tightens and grows with the text actually in it, rather than sitting in a fixed box
        /// sized for a worst case that is usually shorter.
        /// </summary>
        sealed class Element
        {
            public VisualElement Root;
            public Label Value;

            /// <summary>Only the directive element has this: what it asks for is a list that changes with the directive, so its header is rebuilt rather than filled in.</summary>
            public VisualElement Requirements;
        }

        InputAction _pause;

        /// <summary>
        /// The shortcuts screen, opened by the Menu button. The same template the main menu
        /// instantiates, so there is one list rather than two that can drift.
        ///
        /// Moved out of the Top Bar's own tree and onto the document root: it is a full-screen
        /// overlay, and left inside a bar that other controllers draw over it would have opened
        /// underneath them.
        /// </summary>
        ShortcutsPanel _shortcuts;

        /// <summary>The menu the button actually opens; Options inside it is what reaches the shortcuts screen.</summary>
        GameMenuPanel _gameMenu;

        void Start()
        {
            _pause = InputBindings.Find(InputActionCatalogue.Pause);

            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _leftRow = panelRoot.Q<VisualElement>("TopBarLeftRow");
            _rightRow = panelRoot.Q<VisualElement>("TopBarRightRow");
            _clock = panelRoot.Q<Label>("TopBarClock");
            _pauseOverlay = panelRoot.Q<Label>("TopBarPauseOverlay");
            _refusalMessage = panelRoot.Q<Label>("TopBarRefusalMessage");

            panelRoot.Q<Button>("TopBarPauseButton").clicked += TogglePause;

            ReleaseFocusAfterAClick();

            _powerElement = BuildElement(_leftRow, powerIcon, PowerPanelController.PanelName);
            _buildingComputeElement = BuildElement(_leftRow, buildingComputeIcon, ComputePanelController.BuildingPanelName);
            _researchComputeElement = BuildElement(_leftRow, researchComputeIcon, ComputePanelController.ResearchPanelName);
            // Built last on the left so the row keeps one order for the whole run: the directive
            // element is what stands there before Research exists, and the two coexist from the
            // second directive on rather than one taking the other's place.
            _directiveElement = BuildDirectiveElement();

            _researchElement = BuildElement(_rightRow, researchIcon, ResearchPanelController.PanelName);
            _buildingElement = BuildElement(_rightRow, buildingIcon, BuildingMenuController.PanelName);

            // Reparented into the row itself, as its last two children, rather than pinned at their
            // own independently-tuned `right` offset outside it - Add() moves a VisualElement rather
            // than duplicating it, so this is the only place either button lives from here on. The
            // row's own right anchor is what now keeps them flush with the bar's edge; nothing about
            // either button names a position of its own any more.
            _rightRow.Add(panelRoot.Q<Button>("TopBarMenuButton"));
            _rightRow.Add(panelRoot.Q<Button>("TopBarPauseButton"));

            if (constructionInputAdapter != null) constructionInputAdapter.PlacementRefused += ShowRefusalMessage;

            BuildShortcutsScreen(panelRoot);
        }

        /// <summary>
        /// Hands the Menu button the shortcuts screen.
        ///
        /// <b>Last in Start, and guarded.</b> The overlay is reparented onto the document root, and
        /// an <c>Add(null)</c> in the middle of this method would have thrown before the elements
        /// were built - costing the clock, the six status elements and Pause for a missing element
        /// that only the options screen needs. A screen that cannot be opened is the right price; a
        /// Top Bar that does not exist is not.
        /// </summary>
        void BuildShortcutsScreen(VisualElement panelRoot)
        {
            VisualElement overlay = panelRoot.Q<VisualElement>("ShortcutsOverlay");
            var menuButton = panelRoot.Q<Button>("TopBarMenuButton");

            if (overlay == null || menuButton == null)
            {
                Debug.LogError("TopBar.uxml no longer instantiates the Shortcuts template - the Menu button has nothing to open.", this);
                return;
            }

            // Moved out of the Top Bar's own tree: it is a full-screen overlay, and left inside a bar
            // that every other controller draws over, it would have opened underneath them.
            uiDocument.rootVisualElement.Add(overlay);

            _shortcuts = new ShortcutsPanel(overlay);

            VisualElement menuOverlay = panelRoot.Q<VisualElement>("GameMenuOverlay");
            if (menuOverlay == null)
            {
                // Same trade as above: without the menu the button falls back to what it used to
                // do, rather than the Top Bar losing its button.
                Debug.LogError("TopBar.uxml no longer instantiates the GameMenu template - the Menu button falls back to the shortcuts screen.", this);
                menuButton.clicked += _shortcuts.Show;
                return;
            }

            uiDocument.rootVisualElement.Add(menuOverlay);
            _gameMenu = new GameMenuPanel(menuOverlay, gameRuntime, _shortcuts);
            menuButton.clicked += _gameMenu.Show;
            gameRuntime.Escape.SetMenuProbe(MenuOverlayStateNow);
        }

        /// <summary>Flashes an explicit refusal reason (e.g. the building cap) near the elements row for RefusalMessageSeconds, then auto-hides. Re-showing while already visible just resets the timer.</summary>
        public void ShowRefusalMessage(string text)
        {
            if (_refusalMessage == null) return;
            _refusalMessage.text = text;
            _refusalMessage.EnableInClassList("hidden", false);
            _refusalMessageHideAt = Time.unscaledTime + RefusalMessageSeconds;
        }

        Element BuildElement(VisualElement row, Sprite icon, string panelName)
        {
            var element = new Element();

            var root = new Button(() => gameRuntime.Selection.OpenGlobalPanel(panelName)) { text = string.Empty };
            root.AddToClassList("top-bar-element");
            element.Root = root;

            var iconElement = new VisualElement();
            iconElement.AddToClassList("top-bar-element-icon");
            if (icon != null) iconElement.style.backgroundImage = new StyleBackground(icon);
            root.Add(iconElement);

            var value = new Label();
            value.AddToClassList("top-bar-element-value");
            root.Add(value);
            element.Value = value;

            row.Add(root);
            return element;
        }

        /// <summary>
        /// The element for what the Core is currently asking for: a row of item chips, and nothing
        /// else - no icon of its own, unlike the other elements: the directive number is the icon.
        /// </summary>
        Element BuildDirectiveElement()
        {
            var element = new Element();

            // The element states a bill; the Core panel is where it is read in full and accepted.
            // The player who reads "0/40" is already asking about the directive, and having to go
            // find the Core on the map to answer that is a detour the bar can spare them.
            var root = new Button(OpenCorePanel) { text = string.Empty };
            root.AddToClassList("top-bar-element");
            element.Root = root;

            // Which directive this is, ahead of what it wants: the bill on its own says nothing
            // about how far into the Core's sequence the player is.
            var number = new Label();
            number.AddToClassList("top-bar-directive-number");
            root.Add(number);
            element.Value = number;

            var requirements = new VisualElement();
            requirements.AddToClassList("top-bar-directive-requirements");
            root.Add(requirements);
            element.Requirements = requirements;

            _leftRow.Add(root);
            return element;
        }

        /// <summary>
        /// Selects the Core, which is what CorePanelController listens for - the same route a click on
        /// the Core itself takes, so there is one way in and not two.
        ///
        /// A missing Core is logged rather than shrugged off: this is reached by a deliberate click,
        /// and a click that silently does nothing is the hardest kind of defect to report.
        /// </summary>
        void OpenCorePanel()
        {
            CoreRuntime core = gameRuntime.World?.Core;
            if (core == null)
            {
                Debug.LogError("The Top Bar's directive element was clicked with no Core in the world - nothing to open.", this);
                return;
            }

            gameRuntime.Selection.Select(core);
        }

        void TogglePause() => gameRuntime.SetPaused(!gameRuntime.IsPaused);

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
        /// <b>It asks what holds the focus, not what was clicked.</b> A Top Bar element is a Button
        /// containing an icon and a label, and those children take the click for themselves - so the
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

        /// <summary>
        /// What Escape does when the arbiter awards it to the menu overlay: closes whichever of the
        /// two is in front of the player, and opens the menu when neither is.
        ///
        /// <b>The shortcuts screen goes first</b>, because it is the one the menu opens: closing it
        /// returns the player to the menu they came from rather than putting both away at once.
        ///
        /// <b>A row waiting for a key keeps Escape.</b> The rebinding operation cancels on it, and
        /// a player who opened a row by accident needs that more than they need the screen closed.
        /// Doing nothing here is the whole of what that takes.
        /// </summary>
        void ToggleMenuOverlay()
        {
            if (_shortcuts != null && _shortcuts.IsCapturingAKey) return;

            if (_shortcuts != null && _shortcuts.IsOpen)
            {
                _shortcuts.Hide();
                return;
            }

            if (_gameMenu == null) return;
            if (_gameMenu.IsOpen) _gameMenu.Hide();
            else _gameMenu.Show();
        }

        /// <summary>Reported to the arbiter, which cannot be built with the overlays because they do not exist yet - see EscapeArbiter.SetMenuProbe.</summary>
        MenuOverlayState MenuOverlayStateNow()
        {
            if (_gameMenu == null) return MenuOverlayState.Absent;
            bool open = _gameMenu.IsOpen || (_shortcuts != null && _shortcuts.IsOpen);
            return open ? MenuOverlayState.Open : MenuOverlayState.Closed;
        }

        void Update()
        {
            // Same gesture as the Pause button (UI.md's Top Bar) - not gated on
            // IsUIBlockingInput, since pausing/resuming from behind an open panel is expected.
            // A clicked button no longer competes for this key - see ReleaseFocusAfterAClick.
            //
            // Typing is the one thing that does gate it. Pause is bound to Space, and the save
            // name field is the project's first in-game text field: without this, naming a save
            // "avant le datacenter" would pause and unpause the game three times.
            if (InputBindings.WasPressedThisFrame(_pause) && !UIFocus.IsTypingInAField(uiDocument))
            {
                TogglePause();
            }

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.MenuOverlay))
            {
                ToggleMenuOverlay();
            }

            // Read fresh every frame rather than only where this button toggles it: the fleet-
            // arrival threshold message pauses and resumes the session too (ThresholdController),
            // through the same GameRuntime.IsPaused flag, and this badge has to agree with it
            // whichever of the two last touched it.
            _pauseOverlay.EnableInClassList("hidden", !gameRuntime.IsPaused);

            RefreshClock();
            RefreshPower();
            RefreshBuildingCompute();
            RefreshResearchCompute();
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

            bool strained = IsPowerStrained(demand, supply);

            _powerElement.Value.text = $"{Mathf.RoundToInt(demand)} / {Mathf.RoundToInt(supply)} kW";
            _powerElement.Value.EnableInClassList("top-bar-element-value-deficit", deficit);
            _powerElement.Value.EnableInClassList("top-bar-element-value-strained", strained);
        }

        /// <summary>Building Compute (CALCUL.md) - every building-side spender and the Core's own fixed grant, inheriting the role a single undifferentiated reserve used to have.</summary>
        void RefreshBuildingCompute() => RefreshComputeElement(_buildingComputeElement, gameRuntime.BuildingCompute);

        /// <summary>
        /// Research Compute (CALCUL.md) - what a research absorbs from by default. Hidden, and left
        /// at the 0 it starts every run at, until a Data Center exists to credit it
        /// (ConstructionService.HasDataCenter) - a reserve nothing has produced yet has nothing to
        /// report.
        /// </summary>
        void RefreshResearchCompute()
        {
            bool hasDataCenter = gameRuntime.Construction != null && gameRuntime.Construction.HasDataCenter;
            _researchComputeElement.Root.EnableInClassList("hidden", !hasDataCenter);
            if (!hasDataCenter) return;

            RefreshComputeElement(_researchComputeElement, gameRuntime.ResearchCompute);
        }

        void RefreshComputeElement(Element element, ComputeSystem reserve)
        {
            element.Value.text = $"{FormatThousands(reserve.Reserve)} CU";

            // Red, and blinking - the same rhythm the new-unlock pulse uses, read off the same clock
            // so two things blinking at once blink together. Unscaled, so it keeps going while the
            // player pauses to work out what went wrong, which is exactly when this appears.
            bool critical = reserve.Reserve < CriticalReserveCu;
            element.Value.EnableInClassList("top-bar-element-value-deficit", critical);
            element.Value.EnableInClassList("top-bar-element-value-blink", critical && !NewUnlockPulse.IsOn);
        }

        /// <summary>
        /// What the Core is asking for, shown for as long as it is asking.
        ///
        /// Two states, not one. Before validation the element is a shopping list: each requirement
        /// against the aggregate stock a robot could actually go and claim - the same figure, read
        /// the same way, as the Core panel's own, so the two can never disagree. Once the player has
        /// validated, there is nothing left to shop for and the element simply says the convoy is
        /// out. A counter there would be answering a question nobody is asking any more, and
        /// answering it with a number that moves for reasons the player cannot see.
        /// </summary>
        void RefreshDirective()
        {
            CoreDirectiveSystem directives = gameRuntime.CoreDirectives;
            CoreDirectiveDefinition current = directives?.Current;

            _directiveElement.Root.EnableInClassList("hidden", current == null);
            if (current == null) return;

            _directiveElement.Value.text = $"Directive {directives.CurrentNumber}";
            _directiveElement.Requirements.Clear();

            if (directives.IsDelivering)
            {
                var status = new Label("Approvisionnement en cours");
                status.AddToClassList("top-bar-element-value");
                _directiveElement.Requirements.Add(status);
                return;
            }

            IReadOnlyDictionary<string, int> available = gameRuntime.DirectiveStock;
            foreach (RecipeIngredient requirement in current.Requirements)
            {
                if (requirement.Item == null || requirement.Amount <= 0) continue;

                int stock = available != null && available.TryGetValue(requirement.Item.Id, out int inStock) ? inStock : 0;
                _directiveElement.Requirements.Add(BuildDirectiveChip(requirement, Mathf.Min(stock, requirement.Amount)));
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

            // No research menu before the Datacenter has finished priming and powered its cores
            // (GDD §5.4), so before that there is no element to fill in - and nothing to fill it from.
            bool menuUnlocked = ResearchPanelController.IsAvailable(gameRuntime.Researches, research);
            _researchElement.Root.EnableInClassList("hidden", !menuUnlocked);

            // An element that appears mid-run appears among others the player has long stopped
            // looking at, so it announces itself until they act on it - and then stops, which is
            // the point.
            if (gameRuntime.Selection.ActiveGlobalPanel == ResearchPanelController.PanelName) _researchMenuSeen = true;
            NewUnlockPulse.Apply(_researchElement.Root, menuUnlocked && !_researchMenuSeen);

            if (!menuUnlocked) return;

            if (research.HasActiveResearch())
            {
                ResearchDefinition active = research.GetActiveResearch();
                _researchElement.Value.text = active.DisplayName;

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
                _researchElement.Value.text = queued > 0 ? $"{queued} en file" : idleText;

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
        /// nothing queued. It gets a green value, so it reads as finished at a glance instead of
        /// looking like the "Aucune" it sits in the same place as. Cleared as soon as anything is
        /// being researched again, or the halo would outlive what it announced.
        /// </summary>
        void SetResearchFinished(bool finished)
        {
            _researchElement.Value.EnableInClassList("top-bar-element-value-done", finished);
        }

        /// <summary>Occupied/cap counter (CONSTRUCTION.md) - the second Top Bar figure the survival-phase UI shows, alongside CU. Turns alert-colored within BuildingCapAlertMargin slots of the cap; the cap itself is read live from ConstructionService, so each memory allocation level shows the moment it lands.</summary>
        void RefreshBuildings()
        {
            var construction = gameRuntime.Construction;
            if (construction == null) return;

            int occupied = construction.OccupiedBuildingSlots;
            int cap = construction.BuildingCap;
            bool approaching = occupied >= cap - BuildingCapAlertMargin;

            _buildingElement.Value.text = $"{occupied} / {cap}";
            _buildingElement.Value.EnableInClassList("top-bar-element-value-deficit", approaching);
        }

        static string FormatThousands(float value)
        {
            return Mathf.RoundToInt(value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}

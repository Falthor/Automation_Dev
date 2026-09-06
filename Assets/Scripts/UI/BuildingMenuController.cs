using System;
using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Research;
using Game.Gameplay.Transport;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>Definition + category pairing for one Building menu entry, configured in the Inspector.</summary>
    [Serializable]
    public struct BuildingMenuEntry
    {
        public BuildingDefinition definition;
        public BuildingCategory category;
    }

    /// <summary>Icon shown on a category rail button, configured in the Inspector.</summary>
    [Serializable]
    public struct CategoryIcon
    {
        public BuildingCategory category;
        public Sprite icon;
    }

    /// <summary>
    /// Building selection menu, toggled with B. Reproduces the source project's BuildingPanel
    /// layout intent (dark panel, cyan title, close button, a left category rail filtering a
    /// grid of icon+label cards) with UI Toolkit. Selecting a card hands off to the existing
    /// ConstructionService immediately and closes the menu, matching the source behavior
    /// (single click arms the tool, no separate confirm step).
    ///
    /// Also owns the 8 Bottom Nav toolbar slot assignments (CONTRACTS.md-equivalent: this panel
    /// is the source of truth, the Bottom Nav is just a reflecting view) and which card is
    /// currently hovered, so a 1-8 key press elsewhere can assign a slot without a second
    /// independent input system.
    /// </summary>
    public sealed class BuildingMenuController : MonoBehaviour
    {
        public const string PanelName = "building";
        public const int ToolbarSlotCount = 8;

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] BuildingMenuEntry[] entries;
        [SerializeField] CategoryIcon[] categoryIcons;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        VisualElement _root;
        VisualElement _categoryColumn;
        VisualElement _grid;
        VisualElement _details;
        readonly Dictionary<BuildingCategory, Button> _categoryButtons = new Dictionary<BuildingCategory, Button>();
        readonly List<(VisualElement card, BuildingDefinition definition)> _cardStates = new List<(VisualElement, BuildingDefinition)>();
        BuildingCategory _selectedCategory = BuildingCategory.Production;
        bool _isOpen;

        public bool IsOpen => _isOpen;
        public BuildingDefinition HoveredCardDefinition { get; private set; }

        /// <summary>The 8 Bottom Nav toolbar slots. Null entries are empty slots.</summary>
        public BuildingDefinition[] ToolbarSlots { get; } = new BuildingDefinition[ToolbarSlotCount];

        public event Action ToolbarChanged;

        void Start()
        {
            // Start(), not OnEnable(): GameRuntime.Awake() (which constructs Selection) is not
            // guaranteed to run before this object's OnEnable, but Start() always runs after
            // every object's Awake() - see ConstructionInputAdapter for the same reasoning.
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            // The clone wrapper itself is an invisible full-screen box with no content of its
            // own - without Ignore, it swallows clicks meant for whatever real content (this
            // panel's own included) sits underneath it in z-order, anywhere it has no visible
            // child at that exact point. Its actual content keeps its own default picking mode.
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("BuildingMenuRoot");
            _categoryColumn = panelRoot.Q<VisualElement>("CategoryColumn");
            _grid = panelRoot.Q<VisualElement>("BuildingGrid");
            _details = panelRoot.Q<VisualElement>("BuildingDetails");
            panelRoot.Q<Button>("BuildingCloseButton").clicked += Close;

            BuildCategoryButtons();
            SelectCategory(BuildingCategory.Production);

            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
            ApplyOpenState(gameRuntime.Selection.ActiveGlobalPanel == PanelName);
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName) => ApplyOpenState(panelName == PanelName);

        /// <summary>
        /// A category's tab appears only once it has something to show. Organisation holds the
        /// Storage Box alone, so before that research it was a tab onto an empty grid - the menu
        /// announcing a section of the game the player cannot reach yet.
        ///
        /// Derived from the per-card rule rather than named category by category: a tab is exactly
        /// as available as its contents, so a new building or a new category needs nothing here.
        /// </summary>
        public static bool HasVisibleBuilding(BuildingMenuEntry[] entries, BuildingCategory category, ResearchSystem research)
        {
            foreach (BuildingMenuEntry entry in entries)
            {
                if (entry.definition == null || entry.category != category) continue;
                if (IsUnlocked(entry.definition, research)) return true;
            }
            return false;
        }

        bool HasVisibleBuilding(BuildingCategory category) => HasVisibleBuilding(entries, category, gameRuntime.Research);

        /// <summary>
        /// Whether this building is listed at all. A locked one does not appear (matching the source
        /// project's building_panel.gd) - "unaffordable" is a different, visible-but-not-buildable
        /// state. Asked in one place so the cards and the category rail can never disagree about
        /// what exists.
        /// </summary>
        public static bool IsUnlocked(BuildingDefinition definition, ResearchSystem research)
            => definition.UnlockResearch == null || research.IsUnlocked(definition.UnlockResearch.Id);

        bool IsUnlocked(BuildingDefinition definition) => IsUnlocked(definition, gameRuntime.Research);

        void BuildCategoryButtons()
        {
            _categoryColumn.Clear();
            _categoryButtons.Clear();

            foreach (BuildingCategory category in Enum.GetValues(typeof(BuildingCategory)))
            {
                if (!HasVisibleBuilding(category)) continue;

                var button = new Button(() => SelectCategory(category)) { text = string.Empty };
                button.AddToClassList("category-button");

                var icon = new VisualElement();
                icon.AddToClassList("category-button-icon");
                Sprite iconSprite = ResolveCategoryIcon(category);
                if (iconSprite != null)
                {
                    icon.style.backgroundImage = new StyleBackground(iconSprite);
                }
                button.Add(icon);

                var label = new Label(category.ToString());
                label.AddToClassList("category-button-label");
                button.Add(label);

                _categoryColumn.Add(button);
                _categoryButtons[category] = button;
            }
        }

        Sprite ResolveCategoryIcon(BuildingCategory category)
        {
            foreach (CategoryIcon entry in categoryIcons)
            {
                if (entry.category == category) return entry.icon;
            }
            return null;
        }

        void SelectCategory(BuildingCategory category)
        {
            // The asked-for category may have no tab (nothing in it is unlocked yet), which is the
            // case every time the menu opens before the first research: fall back to whatever rail
            // there is rather than opening on an empty grid with no tab lit.
            if (!_categoryButtons.ContainsKey(category))
            {
                foreach (var kvp in _categoryButtons)
                {
                    category = kvp.Key;
                    break;
                }
            }

            _selectedCategory = category;

            foreach (var kvp in _categoryButtons)
            {
                kvp.Value.EnableInClassList("category-button-selected", kvp.Key == category);
            }

            BuildCards();
        }

        const int GridColumns = 3;

        void BuildCards()
        {
            HoveredCardDefinition = null;
            _cardStates.Clear();
            var cards = new List<VisualElement>();

            foreach (BuildingMenuEntry entry in entries)
            {
                if (entry.definition == null || entry.category != _selectedCategory) continue;

                BuildingDefinition definition = entry.definition;

                if (!IsUnlocked(definition)) continue;

                var card = new Button(() => SelectAndClose(definition)) { text = string.Empty };
                card.AddToClassList("building-card");
                card.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    HoveredCardDefinition = definition;
                    PopulateDetails(definition);
                });
                card.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (HoveredCardDefinition != definition) return;
                    HoveredCardDefinition = null;
                    _details.Clear();
                });

                var icon = new VisualElement();
                icon.AddToClassList("building-card-icon");
                Sprite iconSprite = ResolveIcon(definition);
                if (iconSprite != null)
                {
                    icon.style.backgroundImage = new StyleBackground(iconSprite);
                }
                card.Add(icon);

                var label = new Label(definition.DisplayName);
                label.AddToClassList("building-card-label");
                card.Add(label);

                cards.Add(card);
                _cardStates.Add((card, definition));
            }

            // Rows built manually (not CSS flex-wrap) at a fixed GridColumns count, so the panel
            // stays the same width across every category regardless of card count (the actual
            // fixed width comes from .building-grid-scroll in GameUI.uss).
            _grid.Clear();
            for (int i = 0; i < cards.Count; i += GridColumns)
            {
                var row = new VisualElement();
                row.AddToClassList("building-grid-row");
                for (int j = i; j < Mathf.Min(i + GridColumns, cards.Count); j++)
                {
                    row.Add(cards[j]);
                }
                _grid.Add(row);
            }
        }

        /// <summary>
        /// Tints every visible card by availability (matches the source project's
        /// building_panel.gd _refresh_states(): grey = locked, amber = unaffordable, normal =
        /// available), refreshed every frame the panel is open so paying/unlocking updates cards
        /// live without needing to hover them.
        /// </summary>
        void RefreshCardStates()
        {
            foreach (var (card, definition) in _cardStates)
            {
                bool locked = definition.UnlockResearch != null && !gameRuntime.Research.IsUnlocked(definition.UnlockResearch.Id);
                bool affordable = locked || gameRuntime.Construction.CanAfford(definition);
                card.EnableInClassList("building-card-locked", locked);
                card.EnableInClassList("building-card-unaffordable", !locked && !affordable);
            }
        }

        public Sprite ResolveIcon(BuildingDefinition definition)
        {
            if (definition == null) return null;

            if (definition is ConveyorDefinition conveyorDef)
            {
                return conveyorDef.OverrideSprite != null
                    ? conveyorDef.OverrideSprite
                    : _spriteFactory.CreateShapeSprite(conveyorDef.DefaultShape, conveyorDef.PlaceholderColor);
            }

            return definition.Sprite != null ? definition.Sprite : _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
        }

        /// <summary>Assigns a definition to a toolbar slot (null clears it), overwriting whatever was there.</summary>
        public void AssignToSlot(int slotIndex, BuildingDefinition definition)
        {
            if (slotIndex < 0 || slotIndex >= ToolbarSlots.Length) return;

            ToolbarSlots[slotIndex] = definition;
            ToolbarChanged?.Invoke();
        }

        void Update()
        {
            // Keeps "Available: X" and the affordability status live while the panel stays open
            // and a card stays hovered - a full rebuild each frame, same pattern as every other
            // panel controller's Update()-driven Refresh() (e.g. ResearchPanelController).
            if (_isOpen)
            {
                RefreshCardStates();
                if (HoveredCardDefinition != null) PopulateDetails(HoveredCardDefinition);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || IsTextFieldFocused()) return;

            if (keyboard.bKey.wasPressedThisFrame)
            {
                Toggle();
            }
            else if (_isOpen && keyboard.escapeKey.wasPressedThisFrame)
            {
                gameRuntime.Selection.CloseGlobalPanel();
            }
        }

        /// <summary>
        /// Hover-only details (matches the source project's building_panel.gd): icon, name, its
        /// construction cost (item icon + name + ×amount + how much is currently available) and
        /// an affordability status line - all read through ConstructionService's public
        /// CanAfford/GetAvailableAmount (CONTRACTS.md §12), never a second cost-aggregation path.
        /// </summary>
        void PopulateDetails(BuildingDefinition definition)
        {
            _details.Clear();

            var header = new VisualElement();
            header.AddToClassList("building-details-header");

            var icon = new VisualElement();
            icon.AddToClassList("building-details-icon");
            Sprite iconSprite = ResolveIcon(definition);
            if (iconSprite != null) icon.style.backgroundImage = new StyleBackground(iconSprite);
            header.Add(icon);

            var name = new Label(definition.DisplayName);
            name.AddToClassList("building-details-name");
            header.Add(name);

            _details.Add(header);

            var info = new VisualElement();
            info.AddToClassList("building-details-info");

            if (definition.Cost.Length > 0)
            {
                var costTitle = new Label("COUT DE CONSTRUCTION");
                costTitle.AddToClassList("building-details-section-title");
                info.Add(costTitle);

                foreach (RecipeIngredient ingredient in definition.Cost)
                {
                    if (ingredient.Item == null) continue;
                    info.Add(BuildCostRow(ingredient));
                }
            }

            float? throughputPerMinute = RatedThroughputPerMinute(definition);
            if (throughputPerMinute.HasValue)
            {
                var throughputTitle = new Label("DEBIT");
                throughputTitle.AddToClassList("building-details-section-title");
                info.Add(throughputTitle);

                var throughput = new Label($"{throughputPerMinute.Value:0.#} objets / min");
                throughput.AddToClassList("building-details-throughput");
                info.Add(throughput);
            }

            if (definition.PowerDemandKw > 0f || definition.CuCostPerCycle > 0f)
            {
                var consumptionTitle = new Label("CONSOMMATION");
                consumptionTitle.AddToClassList("building-details-section-title");
                info.Add(consumptionTitle);

                var consumptionRow = new VisualElement();
                consumptionRow.AddToClassList("building-details-consumption-row");
                if (definition.PowerDemandKw > 0f) consumptionRow.Add(BuildConsumptionPill(powerIcon, $"{definition.PowerDemandKw:0} kW"));
                if (definition.CuCostPerCycle > 0f) consumptionRow.Add(BuildConsumptionPill(computeIcon, $"{definition.CuCostPerCycle:0} CU"));
                info.Add(consumptionRow);
            }

            var status = new Label();
            status.AddToClassList("building-details-status");
            bool locked = definition.UnlockResearch != null && !gameRuntime.Research.IsUnlocked(definition.UnlockResearch.Id);
            bool affordable = gameRuntime.Construction.CanAfford(definition);
            if (locked)
            {
                status.text = "VERROUILLE";
                status.AddToClassList("building-details-status-locked");
            }
            else if (!affordable)
            {
                status.text = "RESSOURCES INSUFFISANTES";
                status.AddToClassList("building-details-status-unaffordable");
            }
            else
            {
                status.text = "DISPONIBLE";
                status.AddToClassList("building-details-status-available");
            }
            info.Add(status);

            _details.Add(info);
        }

        /// <summary>
        /// The rated throughput of a building whose output is a rate, in items per minute, or null
        /// where there is no single figure to give.
        ///
        /// Each answer is read from whoever owns the numbers it comes from - TransportSystem for the
        /// belt's metered intake, the definition itself for the extractor's interval and yield - so
        /// the menu never restates a rate of its own and cannot drift from the simulation. Both
        /// conveyor shapes quote the same figure because they are the same belt: that turning a line
        /// costs nothing is itself the answer.
        ///
        /// Null for a Foundry/Factory/Assembler on purpose. Their rate is a property of the recipe
        /// currently selected, not of the building, so there is no honest number to put on a
        /// catalogue card - it belongs in the production panel, beside the recipe.
        /// </summary>
        static float? RatedThroughputPerMinute(BuildingDefinition definition)
        {
            switch (definition)
            {
                case ConveyorDefinition _: return TransportSystem.ConveyorItemsPerMinute;
                case ExtractorDefinition extractor: return extractor.ItemsPerMinute;
                default: return null;
            }
        }

        VisualElement BuildConsumptionPill(Sprite icon, string text)
        {
            var pill = new VisualElement();
            pill.AddToClassList("building-details-consumption-pill");

            var iconElement = new VisualElement();
            iconElement.AddToClassList("building-details-consumption-icon");
            if (icon != null) iconElement.style.backgroundImage = new StyleBackground(icon);
            pill.Add(iconElement);

            var label = new Label(text);
            label.AddToClassList("building-details-consumption-text");
            pill.Add(label);

            return pill;
        }

        VisualElement BuildCostRow(RecipeIngredient ingredient)
        {
            var row = new VisualElement();
            row.AddToClassList("building-details-cost-row");

            var icon = new VisualElement();
            icon.AddToClassList("building-details-cost-icon");
            if (ingredient.Item.Icon != null) icon.style.backgroundImage = new StyleBackground(ingredient.Item.Icon);
            row.Add(icon);

            var name = new Label($"{ingredient.Item.DisplayName} x{ingredient.Amount}");
            name.AddToClassList("building-details-cost-name");
            row.Add(name);

            int available = gameRuntime.Construction.GetAvailableAmount(ingredient.Item.Id);
            var have = new Label($"{available}");
            have.AddToClassList("building-details-cost-have");
            have.EnableInClassList("building-details-cost-have-insufficient", available < ingredient.Amount);
            row.Add(have);

            return row;
        }

        bool IsTextFieldFocused()
        {
            VisualElement focused = uiDocument.rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            return focused is TextField;
        }

        void Toggle()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel == PanelName)
            {
                gameRuntime.Selection.CloseGlobalPanel();
            }
            else
            {
                gameRuntime.Selection.OpenGlobalPanel(PanelName);
            }
        }

        void SelectAndClose(BuildingDefinition definition)
        {
            gameRuntime.Construction.SelectBuilding(definition);
            gameRuntime.Selection.CloseGlobalPanel();
        }

        void Close() => gameRuntime.Selection.CloseGlobalPanel();

        void ApplyOpenState(bool open)
        {
            _isOpen = open;
            _root.EnableInClassList("hidden", !open);

            if (open)
            {
                // Rebuilt on every open, not once at Start: research completes while the menu is
                // closed, and a rail built before the Storage Box existed would never gain its tab.
                BuildCategoryButtons();
                SelectCategory(BuildingCategory.Production);
            }
            else
            {
                HoveredCardDefinition = null;
            }
        }
    }
}

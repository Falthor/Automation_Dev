using System;
using System.Collections.Generic;
using Game.Data;
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

        void BuildCategoryButtons()
        {
            _categoryColumn.Clear();
            _categoryButtons.Clear();

            foreach (BuildingCategory category in Enum.GetValues(typeof(BuildingCategory)))
            {
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

                // A building whose research is not yet unlocked doesn't appear at all (matches
                // the source project's building_panel.gd, which only lists unlocked buildings) -
                // "unaffordable" (amber tint) is a different, visible-but-not-yet-buildable state.
                if (definition.UnlockResearch != null && !gameRuntime.Research.IsUnlocked(definition.UnlockResearch.Id)) continue;
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
                SelectCategory(BuildingCategory.Production);
            }
            else
            {
                HoveredCardDefinition = null;
            }
        }
    }
}

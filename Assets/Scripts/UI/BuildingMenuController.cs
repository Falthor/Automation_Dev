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
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] BuildingMenuEntry[] entries;
        [SerializeField] CategoryIcon[] categoryIcons;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        VisualElement _root;
        VisualElement _categoryColumn;
        VisualElement _grid;
        readonly Dictionary<BuildingCategory, Button> _categoryButtons = new Dictionary<BuildingCategory, Button>();
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
            VisualElement documentRoot = uiDocument.rootVisualElement;
            _root = documentRoot.Q<VisualElement>("BuildingMenuRoot");
            _categoryColumn = documentRoot.Q<VisualElement>("CategoryColumn");
            _grid = documentRoot.Q<VisualElement>("BuildingGrid");
            documentRoot.Q<Button>("BuildingCloseButton").clicked += Close;

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

        void BuildCards()
        {
            _grid.Clear();
            HoveredCardDefinition = null;

            foreach (BuildingMenuEntry entry in entries)
            {
                if (entry.definition == null || entry.category != _selectedCategory) continue;

                BuildingDefinition definition = entry.definition;
                var card = new Button(() => SelectAndClose(definition)) { text = string.Empty };
                card.AddToClassList("building-card");
                card.RegisterCallback<PointerEnterEvent>(_ => HoveredCardDefinition = definition);
                card.RegisterCallback<PointerLeaveEvent>(_ =>
                {
                    if (HoveredCardDefinition == definition) HoveredCardDefinition = null;
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

                _grid.Add(card);
            }
        }

        public Sprite ResolveIcon(BuildingDefinition definition)
        {
            if (definition == null) return null;

            if (definition is ExtractorDefinition extractorDef)
            {
                return extractorDef.Sprite != null ? extractorDef.Sprite : _spriteFactory.CreateSolidSquareSprite(extractorDef.PlaceholderColor);
            }

            if (definition is StorageDefinition storageDef)
            {
                return storageDef.Sprite != null ? storageDef.Sprite : _spriteFactory.CreateSolidSquareSprite(storageDef.PlaceholderColor);
            }

            if (definition is ConveyorDefinition conveyorDef)
            {
                return conveyorDef.OverrideSprite != null
                    ? conveyorDef.OverrideSprite
                    : _spriteFactory.CreateShapeSprite(conveyorDef.DefaultShape, conveyorDef.PlaceholderColor);
            }

            return _spriteFactory.CreateSolidSquareSprite(definition.PlaceholderColor);
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

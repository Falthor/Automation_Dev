using System;
using Game.Data;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Bottom Nav: 3 category buttons (Storage/Building/Research) that open the matching global
    /// panel via SelectionRuntime, plus 8 permanent construction toolbar slots that reflect
    /// BuildingMenuController's own slot assignments (this view never owns that state, it only
    /// displays it - CONTRACTS.md §7's "single source of truth" applies here too).
    /// </summary>
    public sealed class BottomNavController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] BuildingMenuController buildingMenu;
        [SerializeField] StoragePanelController storagePanel;

        [Header("Category icons")]
        [SerializeField] Sprite storageIcon;
        [SerializeField] Sprite buildingIcon;
        [SerializeField] Sprite researchIcon;

        VisualElement _categoryRow;
        VisualElement _toolbarRow;
        readonly (string panelName, VisualElement button)[] _categoryButtons = new (string, VisualElement)[3];
        readonly VisualElement[] _slotRoots = new VisualElement[BuildingMenuController.ToolbarSlotCount];
        readonly VisualElement[] _slotIcons = new VisualElement[BuildingMenuController.ToolbarSlotCount];
        readonly Label[] _slotBadges = new Label[BuildingMenuController.ToolbarSlotCount];

        void Start()
        {
            // Start(), not OnEnable() - GameRuntime.Awake() (which constructs Selection) is not
            // guaranteed to run before this object's OnEnable, but Start() always runs after
            // every object's Awake() - see ConstructionInputAdapter/BuildingMenuController.
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            // See BuildingMenuController - the clone wrapper itself must not intercept clicks
            // meant for whatever real content sits underneath it in z-order.
            panelRoot.pickingMode = PickingMode.Ignore;

            _categoryRow = panelRoot.Q<VisualElement>("BottomNavCategoryRow");
            _toolbarRow = panelRoot.Q<VisualElement>("BottomNavToolbarRow");

            BuildCategoryButtons();
            BuildToolbarSlots();
            RefreshToolbar();
            RefreshCategoryHighlight(gameRuntime.Selection.ActiveGlobalPanel);

            gameRuntime.Selection.GlobalPanelChanged += RefreshCategoryHighlight;
            buildingMenu.ToolbarChanged += RefreshToolbar;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= RefreshCategoryHighlight;
            buildingMenu.ToolbarChanged -= RefreshToolbar;
        }

        void BuildCategoryButtons()
        {
            _categoryRow.Clear();
            AddCategoryButton(0, StoragePanelController.PanelName, storageIcon);
            AddCategoryButton(1, BuildingMenuController.PanelName, buildingIcon);
            AddCategoryButton(2, ResearchPanelController.PanelName, researchIcon);
        }

        void AddCategoryButton(int index, string panelName, Sprite icon)
        {
            var button = new Button(() => ToggleGlobalPanel(panelName)) { text = string.Empty };
            button.AddToClassList("bottom-nav-category-button");

            var iconElement = new VisualElement();
            iconElement.AddToClassList("bottom-nav-category-icon");
            if (icon != null) iconElement.style.backgroundImage = new StyleBackground(icon);
            button.Add(iconElement);

            _categoryRow.Add(button);
            _categoryButtons[index] = (panelName, button);
        }

        void ToggleGlobalPanel(string panelName)
        {
            if (gameRuntime.Selection.ActiveGlobalPanel == panelName)
            {
                gameRuntime.Selection.CloseGlobalPanel();
            }
            else
            {
                gameRuntime.Selection.OpenGlobalPanel(panelName);
            }
        }

        void RefreshCategoryHighlight(string activePanelName)
        {
            foreach (var (panelName, button) in _categoryButtons)
            {
                bool active = panelName == activePanelName;

                // Selecting a specific Storage box in the world reuses the "storage" global-panel
                // slot, but it is a different menu from the player's point of view - don't
                // highlight the category button for it, only for the aggregate view it opens.
                if (panelName == StoragePanelController.PanelName && storagePanel.IsSpecificBoxOpen)
                {
                    active = false;
                }

                button.EnableInClassList("bottom-nav-category-button-active", active);
            }
        }

        void BuildToolbarSlots()
        {
            _toolbarRow.Clear();

            for (int i = 0; i < BuildingMenuController.ToolbarSlotCount; i++)
            {
                var slot = new VisualElement();
                slot.AddToClassList("bottom-nav-slot");

                var icon = new VisualElement();
                icon.AddToClassList("bottom-nav-slot-icon");
                slot.Add(icon);

                var number = new Label((i + 1).ToString());
                number.AddToClassList("bottom-nav-slot-number");
                slot.Add(number);

                var badge = new Label(string.Empty);
                badge.AddToClassList("bottom-nav-slot-badge");
                slot.Add(badge);

                int slotIndex = i;
                slot.RegisterCallback<ClickEvent>(_ => OnSlotClicked(slotIndex));

                _toolbarRow.Add(slot);
                _slotRoots[i] = slot;
                _slotIcons[i] = icon;
                _slotBadges[i] = badge;
            }
        }

        void OnSlotClicked(int slotIndex)
        {
            if (buildingMenu.IsOpen && buildingMenu.HoveredCardDefinition != null)
            {
                buildingMenu.AssignToSlot(slotIndex, buildingMenu.HoveredCardDefinition);
                return;
            }

            if (buildingMenu.ToolbarSlots[slotIndex] == null) return;

            gameRuntime.Construction.SelectBuilding(buildingMenu.ToolbarSlots[slotIndex]);
            gameRuntime.Selection.CloseGlobalPanel();
        }

        void RefreshToolbar()
        {
            for (int i = 0; i < BuildingMenuController.ToolbarSlotCount; i++)
            {
                BuildingDefinition definition = buildingMenu.ToolbarSlots[i];
                bool occupied = definition != null;

                _slotRoots[i].EnableInClassList("bottom-nav-slot-empty", !occupied);

                if (occupied)
                {
                    Sprite icon = buildingMenu.ResolveIcon(definition);
                    _slotIcons[i].style.backgroundImage = icon != null ? new StyleBackground(icon) : default;
                    // No cap/remaining-count system exists in this project yet (buildings are
                    // placed freely once valid) - every occupied slot shows the "unlimited" badge
                    // until such a system exists to report a real remaining count.
                    _slotBadges[i].text = "∞";
                }
                else
                {
                    _slotIcons[i].style.backgroundImage = default;
                    _slotBadges[i].text = string.Empty;
                }
            }
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || IsTextFieldFocused()) return;

            for (int i = 0; i < BuildingMenuController.ToolbarSlotCount; i++)
            {
                if (!DigitKey(keyboard, i).wasPressedThisFrame) continue;
                OnSlotClicked(i);
            }
        }

        bool IsTextFieldFocused()
        {
            VisualElement focused = uiDocument.rootVisualElement.panel?.focusController?.focusedElement as VisualElement;
            return focused is TextField;
        }

        static ButtonControl DigitKey(Keyboard keyboard, int slotIndex) => slotIndex switch
        {
            0 => keyboard.digit1Key,
            1 => keyboard.digit2Key,
            2 => keyboard.digit3Key,
            3 => keyboard.digit4Key,
            4 => keyboard.digit5Key,
            5 => keyboard.digit6Key,
            6 => keyboard.digit7Key,
            _ => keyboard.digit8Key,
        };
    }
}

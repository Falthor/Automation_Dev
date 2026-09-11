using System;
using Game.Data;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
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

        /// <summary>Kept apart from the array because it is the one category that can be absent: the Research menu arrives with the Datacenter's cores, once its priming is done - not owned from the start.</summary>
        VisualElement _researchCategoryButton;

        /// <summary>Opens the zoomed-out map. Hidden until the explorer robots arrive.</summary>
        Button _mapButton;

        /// <summary>The slot the map button lives in. Hidden with the button, so before the robots arrive there is no empty frame sitting where a control will one day be.</summary>
        VisualElement _mapSlot;

        /// <summary>False until the player has opened the map once - what ends the green pulse announcing that somewhere else has become reachable.</summary>
        bool _mapSeen;

        /// <summary>False until the player has opened the Research panel once - what ends the "this is new" pulse on the icon that just appeared.</summary>
        bool _researchMenuSeen;
        readonly VisualElement[] _slotRoots = new VisualElement[BuildingMenuController.ToolbarSlotCount];
        readonly VisualElement[] _slotIcons = new VisualElement[BuildingMenuController.ToolbarSlotCount];
        readonly Label[] _slotBadges = new Label[BuildingMenuController.ToolbarSlotCount];

        /// <summary>One action per slot, in slot order. Resolved once - see InputBindings.</summary>
        readonly InputAction[] _slotShortcuts = new InputAction[BuildingMenuController.ToolbarSlotCount];

        void Start()
        {
            for (int slot = 0; slot < _slotShortcuts.Length; slot++)
            {
                _slotShortcuts[slot] = InputBindings.Find(InputActionCatalogue.Slot(slot + 1));
            }

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

            BuildMapButton(panelRoot.Q<VisualElement>("BottomNavMinimap"));

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
            _researchCategoryButton = _categoryButtons[2].button;
            RefreshResearchAvailability();
            RefreshMapAvailability();
        }

        /// <summary>
        /// The Research icon exists only once the Datacenter has finished priming and powered its
        /// cores (GDD §5.4) - there is no research menu before that. Re-asked every frame rather
        /// than wired to an event: this is the same per-frame refresh every other panel controller
        /// already runs.
        /// </summary>
        void RefreshResearchAvailability()
        {
            if (_researchCategoryButton == null) return;

            bool unlocked = ResearchPanelController.IsAvailable(gameRuntime.Researches, gameRuntime.Research);
            _researchCategoryButton.EnableInClassList("hidden", !unlocked);

            // Pulsed in step with the Top Bar card (both read NewUnlockPulse's shared phase), and
            // both stop on the same event: the player opening the panel they point at.
            if (gameRuntime.Selection.ActiveGlobalPanel == ResearchPanelController.PanelName) _researchMenuSeen = true;
            NewUnlockPulse.Apply(_researchCategoryButton, unlocked && !_researchMenuSeen);
        }

        /// <summary>
        /// The zoomed-out map opens from the slot the layout already reserved for a minimap - an
        /// element that existed in the UXML with no consumer. Using it rather than adding a fourth
        /// category keeps the category row meaning what it means: three menus about the base, and the
        /// map is about everywhere else.
        ///
        /// It is not a placeholder for a live minimap: at 10 000 cells a thumbnail of the whole world
        /// would show the player's entire territory as a couple of pixels.
        /// </summary>
        void BuildMapButton(VisualElement slot)
        {
            if (slot == null) return;

            _mapSlot = slot;
            _mapButton = new Button(() => ToggleGlobalPanel(SectorMapPanelController.PanelName)) { text = "CARTE" };
            _mapButton.AddToClassList("bottom-nav-map-button");

            slot.Clear();
            slot.Add(_mapButton);
        }

        /// <summary>
        /// The map exists once the robots do: before that there is nothing out there to look at, and
        /// their arrival - the CU reserve having fallen far enough - is what opens it.
        ///
        /// The whole slot goes, not just the button inside it. Hiding only the button left its empty
        /// frame drawn in the corner for the entire opening - a control-shaped hole announcing a
        /// control the player has not been given, which is the announcement the pulse below is for.
        ///
        /// Green rather than the Research menu's tint: this is not another menu being handed over,
        /// it is the world beyond the Core becoming reachable at all.
        /// </summary>
        void RefreshMapAvailability()
        {
            if (_mapButton == null) return;

            bool available = gameRuntime.ExplorerRobots != null && gameRuntime.ExplorerRobots.RobotsHaveAppeared;
            _mapSlot?.EnableInClassList("hidden", !available);
            _mapButton.EnableInClassList("hidden", !available);

            if (gameRuntime.Selection.ActiveGlobalPanel == SectorMapPanelController.PanelName) _mapSeen = true;
            _mapButton.EnableInClassList("newly-reachable", available && !_mapSeen && NewUnlockPulse.IsOn);
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
            RefreshResearchAvailability();
            RefreshMapAvailability();

            if (UIFocus.IsTypingInAField(uiDocument)) return;

            for (int i = 0; i < _slotShortcuts.Length; i++)
            {
                if (!InputBindings.WasPressedThisFrame(_slotShortcuts[i])) continue;
                OnSlotClicked(i);
            }
        }


    }
}

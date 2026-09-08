using System.Collections.Generic;
using Game.Construction;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Items;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Storage panel, opened either from the Bottom Nav "Storage" category button (the dedicated
    /// aggregate view TASK_05_ROBOT_CONSTRUCTEUR.md §1 asks for: GlobalStock, i.e. everything a
    /// builder robot could still be sent to fetch, grid sized to however many item types actually
    /// exist) or by clicking a specific Storage in the world (that box's own fixed slots, empty
    /// ones included). Same panel shell, same global-panel slot (CONTRACTS.md §7/§12: UI reads the
    /// public contract, never a private field).
    /// </summary>
    public sealed class StoragePanelController : MonoBehaviour
    {
        public const string PanelName = "storage";
        const int GridColumns = 4;

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        VisualElement _root;
        VisualElement _panelRoot;
        VisualElement _grid;
        Label _title;
        Button _moveButton;
        StorageRuntime _selected;

        /// <summary>The open right-click menu, or null. Lives beside the grid rather than inside a card, because the grid is rebuilt every frame.</summary>
        VisualElement _slotMenu;

        /// <summary>
        /// True only while showing one specific box's 8 slots - false for the aggregate view,
        /// even though both share the same "storage" global-panel slot. The Bottom Nav uses this
        /// to avoid highlighting its Storage category button for a per-box selection, which is a
        /// different menu from the player's point of view even though it reuses the same shell.
        /// </summary>
        public bool IsSpecificBoxOpen => _selected != null && gameRuntime.Selection.ActiveGlobalPanel == PanelName;

        void Start()
        {
            // Start(), not OnEnable() - see BuildingMenuController for why (GameRuntime.Awake
            // ordering across objects is not guaranteed, Start() always runs after all Awakes).
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            // See BuildingMenuController - the clone wrapper itself must not intercept clicks
            // meant for whatever real content sits underneath it in z-order.
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("StoragePanelRoot");
            _panelRoot = _root.Q<VisualElement>(className: "panel");
            _grid = panelRoot.Q<VisualElement>("StorageGrid");

            // Anywhere else in the panel dismisses an open slot menu, which is what makes it feel
            // like a menu rather than a control that latched on.
            _root.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (_slotMenu != null && !_slotMenu.worldBound.Contains(evt.position)) HideSlotMenu();
            }, TrickleDown.TrickleDown);
            _title = panelRoot.Q<Label>("StorageTitle");
            panelRoot.Q<Button>("StorageCloseButton").clicked += Hide;

            _moveButton = panelRoot.Q<Button>("StorageMoveButton");
            _moveButton.tooltip = "Déplacer la boîte et son contenu";
            _moveButton.clicked += MoveSelected;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName)
        {
            HideSlotMenu();

            if (panelName != PanelName)
            {
                _selected = null;
                _root.EnableInClassList("hidden", true);
                return;
            }

            _root.EnableInClassList("hidden", false);
            if (_selected == null) RenderAggregate();
        }

        /// <summary>Shows one specific Storage's 8 fixed slots. The caller (world click) owns which instance, this panel is just its view.</summary>
        public void Show(StorageRuntime storage)
        {
            _selected = storage;
            gameRuntime.Selection.OpenGlobalPanel(PanelName);
            // After opening, not before: opening clears the subject so no panel inherits the last
            // one's. This is what lets the world outline the box being inspected.
            gameRuntime.Selection.SetGlobalPanelSubject(storage);
            _root.EnableInClassList("hidden", false);
            RenderPerBox(storage);
        }

        public void Hide()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            gameRuntime.Selection.CloseGlobalPanel();
        }

        void Update()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            if (_selected != null) RenderPerBox(_selected);
            else RenderAggregate();
        }

        /// <summary>
        /// Hands the box to the placement gesture: a ghost follows the mouse under the ordinary
        /// rules, and the next click puts it down. The panel closes because the rest happens on the
        /// map, and because the box is about to be somewhere else.
        ///
        /// Nothing is transferred when it lands - the same StorageRuntime moves, so its contents
        /// were never anywhere else. See ConstructionService.TryRelocate.
        /// </summary>
        void MoveSelected()
        {
            if (_selected == null) return;

            gameRuntime.BeginBuildingRelocation(_selected);
            Hide();
        }

        void RenderPerBox(StorageRuntime storage)
        {
            // Read from the definition rather than written here: this panel serves every storage
            // there is, and hard-coding one of their names made the Core's reserve introduce itself
            // as a rudimentary box.
            _title.text = storage.Definition.DisplayName;
            _root.EnableInClassList("overlay-root-right", true);

            // Only a specific box can be moved - the aggregate view is every box at once - and only
            // one the player actually placed. ConstructionService.BeginRelocation already refuses a
            // world fixture like the Core's reserve, so showing the button there offered a gesture
            // that silently did nothing.
            _moveButton.EnableInClassList("hidden", ConstructionService.IsProtectedFromDemolition(storage));

            var cards = new List<VisualElement>(storage.Slots.Count);
            for (int i = 0; i < storage.Slots.Count; i++)
            {
                InventorySlot slot = storage.Slots[i];
                cards.Add(slot.IsEmpty ? BuildEmptyCard() : BuildCard(slot.ItemId, slot.Amount, i));
            }
            LayoutGrid(cards);
        }

        /// <summary>
        /// The aggregate view is now a straight read of GlobalStock (CONTRACTS.md §15) - the Core
        /// chest, every placed Storage and every production building's output, minus what
        /// construction sites have already reserved. It no longer sums those sources itself: what
        /// this panel shows must be exactly what a builder robot could still be sent to fetch, and
        /// there is one implementation of that rule, not two.
        /// </summary>
        void RenderAggregate()
        {
            _title.text = "Stock global";
            _root.EnableInClassList("overlay-root-right", false);
            _moveButton.EnableInClassList("hidden", true);

            IReadOnlyDictionary<string, int> totals = gameRuntime.GlobalStock;

            if (totals.Count == 0)
            {
                _grid.Clear();
                var empty = new Label("(aucun materiau disponible)");
                empty.AddToClassList("storage-empty-label");
                _grid.Add(empty);
                return;
            }

            var cards = new List<VisualElement>(totals.Count);
            foreach (var entry in totals)
            {
                // -1: the aggregate is a sum over every container, so no square of it is a slot
                // anyone could empty. Right-clicking one offers nothing.
                cards.Add(BuildCard(entry.Key, entry.Value, -1));
            }
            LayoutGrid(cards);
        }

        /// <summary>Chunks cards into fixed rows of GridColumns, so the grid is always N-wide regardless of panel width (4x2 for 8 slots, never 8x1).</summary>
        void LayoutGrid(List<VisualElement> cards)
        {
            _grid.Clear();
            for (int i = 0; i < cards.Count; i += GridColumns)
            {
                var row = new VisualElement();
                row.AddToClassList("storage-grid-row");
                for (int j = i; j < Mathf.Min(i + GridColumns, cards.Count); j++)
                {
                    row.Add(cards[j]);
                }
                _grid.Add(row);
            }
        }

        VisualElement BuildCard(string itemId, int amount, int slotIndex)
        {
            var card = new VisualElement();
            card.AddToClassList("storage-card");

            var icon = new VisualElement();
            icon.AddToClassList("storage-card-icon");
            icon.style.backgroundImage = new StyleBackground(ItemSprite(itemId));
            card.Add(icon);

            var count = new Label(amount.ToString());
            count.AddToClassList("storage-card-count");
            card.Add(count);

            // Right-click offers to throw the stack away. Only on a full slot: there is nothing to
            // discard from an empty one, and a menu that opens on nothing teaches the player the
            // gesture does nothing.
            if (slotIndex >= 0)
            {
                card.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 1) return;

                    ShowSlotMenu(slotIndex, card.worldBound);
                    evt.StopPropagation();
                });
            }

            return card;
        }

        /// <summary>
        /// The one-entry menu on a slot. Built fresh each time and parented to the panel rather than
        /// to the card: the grid is rebuilt every frame from Update, and a menu living inside a card
        /// would be destroyed the frame after it opened.
        /// </summary>
        void ShowSlotMenu(int slotIndex, Rect cardBounds)
        {
            HideSlotMenu();
            if (_selected == null) return;

            _slotMenu = new VisualElement();
            _slotMenu.AddToClassList("storage-slot-menu");
            _slotMenu.style.left = cardBounds.xMin - _panelRoot.worldBound.xMin;
            _slotMenu.style.top = cardBounds.yMax - _panelRoot.worldBound.yMin;

            var discard = new Button(() =>
            {
                _selected?.DiscardSlot(slotIndex);
                HideSlotMenu();
            })
            { text = "Supprimer" };
            discard.AddToClassList("storage-slot-menu-item");
            _slotMenu.Add(discard);

            _panelRoot.Add(_slotMenu);
        }

        void HideSlotMenu()
        {
            _slotMenu?.RemoveFromHierarchy();
            _slotMenu = null;
        }

        VisualElement BuildEmptyCard()
        {
            var card = new VisualElement();
            card.AddToClassList("storage-card");
            card.AddToClassList("storage-card-empty");
            return card;
        }

        /// <summary>The item's real icon, falling back to its flat color only for an item that has no art assigned yet.</summary>
        Sprite ItemSprite(string itemId)
        {
            ItemDefinition item = gameRuntime.Items != null ? gameRuntime.Items.Get(itemId) : null;
            if (item != null && item.Icon != null) return item.Icon;
            return _spriteFactory.CreateSolidSquareSprite(item != null ? item.FallbackColor : Color.gray);
        }
    }
}

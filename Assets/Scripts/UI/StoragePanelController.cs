using System.Collections.Generic;
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
    /// Storage panel, opened either from the Bottom Nav "Storage" category button (aggregated
    /// view: total of every item type across the player's global stock and every Storage in the
    /// world, grid sized to however many types actually exist) or by clicking a specific Storage
    /// in the world (that box's own
    /// 8 fixed slots, empty ones included). Same panel shell, same global-panel slot
    /// (CONTRACTS.md §7/§12: UI reads the public Inventory contract, never a private field).
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
        VisualElement _grid;
        Label _title;
        StorageRuntime _selected;

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
            _grid = panelRoot.Q<VisualElement>("StorageGrid");
            _title = panelRoot.Q<Label>("StorageTitle");
            panelRoot.Q<Button>("StorageCloseButton").clicked += Hide;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName)
        {
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

        void RenderPerBox(StorageRuntime storage)
        {
            _title.text = "STORAGE BOX";
            _root.EnableInClassList("overlay-root-right", true);

            var cards = new List<VisualElement>(storage.Slots.Count);
            foreach (InventorySlot slot in storage.Slots)
            {
                cards.Add(slot.IsEmpty ? BuildEmptyCard() : BuildCard(slot.ItemId, slot.Amount));
            }
            LayoutGrid(cards);
        }

        void RenderAggregate()
        {
            _title.text = "STORAGE";
            _root.EnableInClassList("overlay-root-right", false);

            var totals = new Dictionary<string, int>();

            // The player's global stock (the items the game starts with, held by no building) is
            // part of what this aggregate view reports - it is spendable exactly like a Storage
            // box's contents, so hiding it here would show the player less than they own.
            foreach (var entry in gameRuntime.GlobalStock.Contents)
            {
                totals[entry.Key] = (totals.TryGetValue(entry.Key, out int owned) ? owned : 0) + entry.Value;
            }

            foreach (StorageRuntime storage in gameRuntime.Transport.Storages)
            {
                foreach (InventorySlot slot in storage.Slots)
                {
                    if (slot.IsEmpty) continue;
                    totals[slot.ItemId] = (totals.TryGetValue(slot.ItemId, out int existing) ? existing : 0) + slot.Amount;
                }
            }

            // A production building's own internal stock (raw materials waiting on a cycle,
            // finished goods waiting to be pushed out) counts as the player's spendable inventory
            // too, exactly like a Storage box's contents - hiding it here would show less than
            // they actually own, and ConstructionService.GetAvailableAmount already draws from it.
            foreach (BuildingRuntime building in gameRuntime.Transport.GetAllBuildings())
            {
                if (!(building is ProductionBuildingRuntime production)) continue;

                foreach (var entry in production.GetInputContents())
                {
                    if (entry.Value <= 0) continue;
                    totals[entry.Key] = (totals.TryGetValue(entry.Key, out int existing) ? existing : 0) + entry.Value;
                }
                foreach (var entry in production.GetOutputContents())
                {
                    if (entry.Value <= 0) continue;
                    totals[entry.Key] = (totals.TryGetValue(entry.Key, out int existing) ? existing : 0) + entry.Value;
                }
            }

            if (totals.Count == 0)
            {
                _grid.Clear();
                var empty = new Label("(no items in any storage)");
                empty.AddToClassList("storage-empty-label");
                _grid.Add(empty);
                return;
            }

            var cards = new List<VisualElement>(totals.Count);
            foreach (var entry in totals)
            {
                cards.Add(BuildCard(entry.Key, entry.Value));
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

        VisualElement BuildCard(string itemId, int amount)
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

            return card;
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

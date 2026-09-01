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
    /// view: total of every item type across every Storage in the world, grid sized to however
    /// many types actually exist) or by clicking a specific Storage in the world (that box's own
    /// 8 fixed slots, empty ones included). Same panel shell, same global-panel slot
    /// (CONTRACTS.md §7/§12: UI reads the public Inventory contract, never a private field).
    /// </summary>
    public sealed class StoragePanelController : MonoBehaviour
    {
        public const string PanelName = "storage";
        const int GridColumns = 4;

        [SerializeField] UIDocument uiDocument;
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
            VisualElement documentRoot = uiDocument.rootVisualElement;
            _root = documentRoot.Q<VisualElement>("StoragePanelRoot");
            _grid = documentRoot.Q<VisualElement>("StorageGrid");
            _title = documentRoot.Q<Label>("StorageTitle");
            documentRoot.Q<Button>("StorageCloseButton").clicked += Hide;

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

            var cards = new List<VisualElement>(Inventory.SlotCount);
            foreach (InventorySlot slot in storage.Slots)
            {
                cards.Add(slot.IsEmpty ? BuildEmptyCard() : BuildCard(slot.ItemType, slot.Amount));
            }
            LayoutGrid(cards);
        }

        void RenderAggregate()
        {
            _title.text = "STORAGE";

            var totals = new Dictionary<OreType, int>();
            foreach (StorageRuntime storage in gameRuntime.Transport.Storages)
            {
                foreach (InventorySlot slot in storage.Slots)
                {
                    if (slot.IsEmpty) continue;
                    totals[slot.ItemType] = (totals.TryGetValue(slot.ItemType, out int existing) ? existing : 0) + slot.Amount;
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

        VisualElement BuildCard(OreType itemType, int amount)
        {
            var card = new VisualElement();
            card.AddToClassList("storage-card");

            var icon = new VisualElement();
            icon.AddToClassList("storage-card-icon");
            icon.style.backgroundImage = new StyleBackground(_spriteFactory.CreateSolidSquareSprite(ItemColor(itemType)));
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

        static Color ItemColor(OreType type) => type switch
        {
            OreType.Iron => new Color(0.80f, 0.78f, 0.75f),
            OreType.Copper => new Color(0.90f, 0.50f, 0.20f),
            OreType.Coal => new Color(0.10f, 0.10f, 0.10f),
            _ => Color.gray
        };
    }
}

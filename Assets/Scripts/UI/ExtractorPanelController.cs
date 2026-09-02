using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Extractor info panel: production progress bar + a single storage-style case showing the
    /// buffered (not-yet-output) amount, so it's visible when nothing can pull from the extractor
    /// (e.g. no conveyor attached) and production stalls once the internal buffer is full.
    /// Reacts to SelectionRuntime.SelectedBuilding (CONTRACTS.md §7) rather than owning its own
    /// open/closed state - the same "currently inspected building" concept future per-building
    /// panels (other than Storage, which has its own dedicated aggregate/per-box mechanism) will use.
    /// </summary>
    public sealed class ExtractorPanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        VisualElement _root;
        Label _title;
        VisualElement _progressFill;
        VisualElement _storageIcon;
        Label _storageCount;
        ExtractorRuntime _selected;

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

            _root = panelRoot.Q<VisualElement>("ExtractorPanelRoot");
            _title = panelRoot.Q<Label>("ExtractorTitle");
            _progressFill = panelRoot.Q<VisualElement>("ExtractorProgressFill");
            _storageIcon = panelRoot.Q<VisualElement>("ExtractorStorageIcon");
            _storageCount = panelRoot.Q<Label>("ExtractorStorageCount");
            panelRoot.Q<Button>("ExtractorCloseButton").clicked += Close;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as ExtractorRuntime;
            _root.EnableInClassList("hidden", _selected == null);
            if (_selected != null) Render();
        }

        void Close() => gameRuntime.Selection.Clear();

        void Update()
        {
            if (_selected == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            Render();
        }

        void Render()
        {
            var item = gameRuntime.Items != null ? gameRuntime.Items.Get(_selected.ItemId) : null;
            _title.text = $"EXTRACTOR - {(item != null ? item.DisplayName : _selected.ItemId)}";

            _progressFill.style.width = new StyleLength(Length.Percent(_selected.ProductionProgress * 100f));

            _storageIcon.style.backgroundImage = new StyleBackground(ItemSprite(_selected.ItemId));
            _storageCount.text = $"{_selected.BufferedAmount}/{ExtractorRuntime.InternalStorageCapacity}";
        }

        Sprite ItemSprite(string itemId)
        {
            var item = gameRuntime.Items != null ? gameRuntime.Items.Get(itemId) : null;
            if (item != null && item.Icon != null) return item.Icon;
            return _spriteFactory.CreateSolidSquareSprite(item != null ? item.FallbackColor : Color.gray);
        }
    }
}

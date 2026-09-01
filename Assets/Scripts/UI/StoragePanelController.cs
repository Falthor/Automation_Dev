using Game.Data;
using Game.Gameplay.Buildings;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>Shows the contents of the currently selected Storage Box (CONTRACTS.md §12: UI reads the public Inventory contract, never a private field).</summary>
    public sealed class StoragePanelController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;

        VisualElement _root;
        VisualElement _rows;
        StorageRuntime _selected;

        void OnEnable()
        {
            VisualElement documentRoot = uiDocument.rootVisualElement;
            _root = documentRoot.Q<VisualElement>("StoragePanelRoot");
            _rows = documentRoot.Q<VisualElement>("StorageRows");
            documentRoot.Q<Button>("StorageCloseButton").clicked += Hide;

            _root.EnableInClassList("hidden", true);
        }

        public void Show(StorageRuntime storage)
        {
            _selected = storage;
            _root.EnableInClassList("hidden", false);
        }

        public void Hide()
        {
            _selected = null;
            _root.EnableInClassList("hidden", true);
        }

        void Update()
        {
            if (_selected == null) return;

            _rows.Clear();

            bool any = false;
            foreach (var entry in _selected.Contents)
            {
                if (entry.Value <= 0) continue;
                any = true;

                var row = new VisualElement();
                row.AddToClassList("storage-row");

                var nameLabel = new Label(ItemLabel(entry.Key));
                nameLabel.AddToClassList("storage-row-name");
                row.Add(nameLabel);

                var amountLabel = new Label(entry.Value.ToString());
                amountLabel.AddToClassList("storage-row-amount");
                row.Add(amountLabel);

                _rows.Add(row);
            }

            if (!any)
            {
                var empty = new Label("(empty)");
                empty.AddToClassList("storage-empty-label");
                _rows.Add(empty);
            }
        }

        static string ItemLabel(OreType type) => type switch
        {
            OreType.Iron => "Iron Ore",
            OreType.Copper => "Copper Ore",
            OreType.Coal => "Coal",
            _ => type.ToString()
        };
    }
}

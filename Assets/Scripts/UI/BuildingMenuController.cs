using System;
using System.Collections.Generic;
using System.Linq;
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

    /// <summary>
    /// Building selection menu, toggled with B. Reproduces the source project's BuildingPanel
    /// layout intent (dark panel, cyan title, close button, a left category rail filtering a
    /// grid of icon+label cards) with UI Toolkit. Selecting a card hands off to the existing
    /// ConstructionService immediately and closes the menu, matching the source behavior
    /// (single click arms the tool, no separate confirm step).
    /// </summary>
    public sealed class BuildingMenuController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] BuildingMenuEntry[] entries;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        VisualElement _root;
        VisualElement _categoryColumn;
        VisualElement _grid;
        readonly Dictionary<BuildingCategory, Button> _categoryButtons = new Dictionary<BuildingCategory, Button>();
        BuildingCategory _selectedCategory = BuildingCategory.Production;
        bool _isOpen;

        void OnEnable()
        {
            VisualElement documentRoot = uiDocument.rootVisualElement;
            _root = documentRoot.Q<VisualElement>("BuildingMenuRoot");
            _categoryColumn = documentRoot.Q<VisualElement>("CategoryColumn");
            _grid = documentRoot.Q<VisualElement>("BuildingGrid");
            documentRoot.Q<Button>("BuildingCloseButton").clicked += Close;

            BuildCategoryButtons();
            SelectCategory(BuildingCategory.Production);
            SetOpen(false);
        }

        void BuildCategoryButtons()
        {
            _categoryColumn.Clear();
            _categoryButtons.Clear();

            foreach (BuildingCategory category in Enum.GetValues(typeof(BuildingCategory)))
            {
                var button = new Button(() => SelectCategory(category)) { text = category.ToString() };
                button.AddToClassList("category-button");
                _categoryColumn.Add(button);
                _categoryButtons[category] = button;
            }
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

            foreach (BuildingMenuEntry entry in entries)
            {
                if (entry.definition == null || entry.category != _selectedCategory) continue;

                BuildingDefinition definition = entry.definition;
                var card = new Button(() => SelectAndClose(definition)) { text = string.Empty };
                card.AddToClassList("building-card");

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

        Sprite ResolveIcon(BuildingDefinition definition)
        {
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

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.bKey.wasPressedThisFrame)
            {
                SetOpen(!_isOpen);
            }
        }

        void SelectAndClose(BuildingDefinition definition)
        {
            gameRuntime.Construction.SelectBuilding(definition);
            SetOpen(false);
        }

        void Close() => SetOpen(false);

        void SetOpen(bool open)
        {
            _isOpen = open;
            _root.EnableInClassList("hidden", !open);

            if (open)
            {
                SelectCategory(BuildingCategory.Production);
            }
        }
    }
}

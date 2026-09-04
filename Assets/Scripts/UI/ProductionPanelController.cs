using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Generic two-tab panel (RECETTES / PRODUCTION) for every ProductionBuildingRuntime
    /// (Foundry/Factory/AdvancedFoundry/Assembler today) - talks only through the
    /// ProductionBuildingRuntime contract, no per-concrete-type code. Mirrors the source
    /// project's production_panel.gd/recipe_card.gd: recipe cards only stage a pending choice
    /// (_pendingRecipeId); only the bottom action button ("COMMENCER"/"CHANGER DE RECETTE")
    /// actually calls SetSelectedRecipe. Reacts to SelectionRuntime.SelectionChanged
    /// (CONTRACTS.md §7), same pattern as ExtractorPanelController.
    /// </summary>
    public sealed class ProductionPanelController : MonoBehaviour
    {
        const int GridColumns = 3;

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();

        VisualElement _root;
        Label _title;
        Button _tabRecipesButton;
        Button _tabProductionButton;
        VisualElement _recipesTab;
        VisualElement _recipesGrid;
        Button _recipeActionButton;
        VisualElement _productionTab;
        VisualElement _emptyState;
        VisualElement _content;
        VisualElement _currentIcon;
        Label _currentAmount;
        Label _currentName;
        VisualElement _progressFill;
        Label _percentLabel;
        VisualElement _ingredientsList;
        VisualElement _stockList;
        Label _powerValue;
        Label _computeValue;
        Label _stateLabel;
        Label _timeLabel;

        ProductionBuildingRuntime _selected;
        string _pendingRecipeId = "";
        bool _tabIsProduction;

        void Start()
        {
            // Start(), not OnEnable() - see BuildingMenuController for why (GameRuntime.Awake
            // ordering across objects is not guaranteed, Start() always runs after all Awakes).
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("ProductionPanelRoot");
            _title = panelRoot.Q<Label>("ProductionTitle");
            _tabRecipesButton = panelRoot.Q<Button>("ProductionTabRecipes");
            _tabProductionButton = panelRoot.Q<Button>("ProductionTabProduction");
            _recipesTab = panelRoot.Q<VisualElement>("RecipesTab");
            _recipesGrid = panelRoot.Q<VisualElement>("RecipesGrid");
            _recipeActionButton = panelRoot.Q<Button>("RecipeActionButton");
            _productionTab = panelRoot.Q<VisualElement>("ProductionTab");
            _emptyState = panelRoot.Q<VisualElement>("ProductionEmptyState");
            _content = panelRoot.Q<VisualElement>("ProductionContent");
            _currentIcon = panelRoot.Q<VisualElement>("ProductionCurrentIcon");
            _currentAmount = panelRoot.Q<Label>("ProductionCurrentAmount");
            _currentName = panelRoot.Q<Label>("ProductionCurrentName");
            _progressFill = panelRoot.Q<VisualElement>("ProductionProgressFill");
            _percentLabel = panelRoot.Q<Label>("ProductionPercentLabel");
            _ingredientsList = panelRoot.Q<VisualElement>("ProductionIngredientsList");
            _stockList = panelRoot.Q<VisualElement>("ProductionStockList");
            _powerValue = panelRoot.Q<Label>("ProductionPowerValue");
            _computeValue = panelRoot.Q<Label>("ProductionComputeValue");
            if (powerIcon != null) panelRoot.Q<VisualElement>("ProductionPowerIcon").style.backgroundImage = new StyleBackground(powerIcon);
            if (computeIcon != null) panelRoot.Q<VisualElement>("ProductionComputeIcon").style.backgroundImage = new StyleBackground(computeIcon);
            _stateLabel = panelRoot.Q<Label>("ProductionStateLabel");
            _timeLabel = panelRoot.Q<Label>("ProductionTimeLabel");

            panelRoot.Q<Button>("ProductionCloseButton").clicked += Close;
            _tabRecipesButton.clicked += () => SetActiveTab(false);
            _tabProductionButton.clicked += () => SetActiveTab(true);
            _recipeActionButton.clicked += OnActionButtonClicked;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy()
        {
            gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;
        }

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as ProductionBuildingRuntime;
            _root.EnableInClassList("hidden", _selected == null);
            if (_selected == null) return;

            _title.text = _selected.Definition.DisplayName.ToUpperInvariant();
            _pendingRecipeId = _selected.GetSelectedRecipe();
            RebuildRecipeCards();
            // A building already producing opens on PRODUCTION; a fresh/idle one opens on
            // RECETTES - matches the source project exactly.
            SetActiveTab(_selected.GetSelectedRecipe() != string.Empty);
            RefreshActionButton();
            RefreshProductionTab();
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

            // Recipe identity rarely changes while open (only on selection change), but
            // afford/pending state does - so cards are only rebuilt above, re-styled every frame
            // here. Production tab refreshes unconditionally too (matches the source), so it's
            // already current the instant the player switches to it.
            RefreshRecipeCardStates();
            RefreshActionButton();
            RefreshProductionTab();
        }

        void SetActiveTab(bool isProduction)
        {
            _tabIsProduction = isProduction;
            _tabRecipesButton.EnableInClassList("production-tab-button-active", !isProduction);
            _tabProductionButton.EnableInClassList("production-tab-button-active", isProduction);
            _recipesTab.EnableInClassList("hidden", isProduction);
            _productionTab.EnableInClassList("hidden", !isProduction);
        }

        void RebuildRecipeCards()
        {
            _recipesGrid.Clear();
            IReadOnlyList<string> recipeIds = _selected.GetRecipeIds();

            var cards = new List<VisualElement>(recipeIds.Count);
            foreach (string recipeId in recipeIds)
            {
                cards.Add(BuildRecipeCard(recipeId));
            }

            for (int i = 0; i < cards.Count; i += GridColumns)
            {
                var row = new VisualElement();
                row.AddToClassList("recipes-grid-row");
                for (int j = i; j < Mathf.Min(i + GridColumns, cards.Count); j++)
                {
                    row.Add(cards[j]);
                }
                _recipesGrid.Add(row);
            }
        }

        VisualElement BuildRecipeCard(string recipeId)
        {
            RecipeDefinition recipe = gameRuntime.Recipes.Get(recipeId);

            var card = new Button(() =>
            {
                _pendingRecipeId = recipeId;
                RefreshActionButton();
                RefreshRecipeCardStates();
            })
            {
                text = string.Empty,
                name = "RecipeCard_" + recipeId
            };
            card.AddToClassList("recipe-card");

            var iconRow = new VisualElement();
            iconRow.AddToClassList("recipe-card-icon-row");
            var icon = new VisualElement();
            icon.AddToClassList("recipe-card-icon");
            icon.style.backgroundImage = new StyleBackground(ResolveItemIcon(recipeId));
            iconRow.Add(icon);
            var amount = new Label($"×{(recipe != null ? recipe.OutputAmount : 1)}");
            amount.AddToClassList("recipe-card-amount");
            iconRow.Add(amount);
            card.Add(iconRow);

            var divider = new VisualElement();
            divider.AddToClassList("recipe-card-divider");
            card.Add(divider);

            var name = new Label(ResolveItemName(recipeId).ToUpperInvariant());
            name.AddToClassList("recipe-card-name");
            card.Add(name);

            var ingredientsRow = new VisualElement();
            ingredientsRow.AddToClassList("recipe-card-ingredients-row");
            if (recipe != null)
            {
                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    var ingredientIcon = new VisualElement();
                    ingredientIcon.AddToClassList("recipe-card-ingredient-icon");
                    ingredientIcon.style.backgroundImage = new StyleBackground(ResolveItemIcon(ingredient.Item.Id));
                    ingredientsRow.Add(ingredientIcon);

                    var qty = new Label($"×{ingredient.Amount}");
                    qty.AddToClassList("recipe-card-ingredient-qty");
                    ingredientsRow.Add(qty);
                }
            }
            card.Add(ingredientsRow);

            var time = new Label(recipe != null ? $"{recipe.TimeSeconds:0.0} s" : string.Empty);
            time.AddToClassList("recipe-card-time");
            card.Add(time);

            return card;
        }

        void RefreshRecipeCardStates()
        {
            foreach (VisualElement row in _recipesGrid.Children())
            {
                foreach (VisualElement card in row.Children())
                {
                    string recipeId = card.name.Substring("RecipeCard_".Length);
                    bool isPending = recipeId == _pendingRecipeId;
                    bool isAvailable = _selected.HasResourcesFor(recipeId);
                    card.EnableInClassList("recipe-card-selected", isPending);
                    card.EnableInClassList("recipe-card-unavailable", !isAvailable);
                }
            }
        }

        void RefreshActionButton()
        {
            bool alreadyActive = _selected.GetSelectedRecipe() != string.Empty;
            _recipeActionButton.text = alreadyActive ? "CHANGER DE RECETTE" : "COMMENCER";
            _recipeActionButton.SetEnabled(_pendingRecipeId != string.Empty && _pendingRecipeId != _selected.GetSelectedRecipe());
        }

        /// <summary>The only place that calls ProductionBuildingRuntime.SetSelectedRecipe() - which already handles the interrupt-and-don't-refund behavior on its own.</summary>
        void OnActionButtonClicked()
        {
            if (string.IsNullOrEmpty(_pendingRecipeId) || _pendingRecipeId == _selected.GetSelectedRecipe()) return;

            _selected.SetSelectedRecipe(_pendingRecipeId);
            SetActiveTab(true);
        }

        void RefreshProductionTab()
        {
            RefreshStockList();

            string recipeId = _selected.GetSelectedRecipe();
            bool hasRecipe = !string.IsNullOrEmpty(recipeId);
            _emptyState.EnableInClassList("hidden", hasRecipe);
            _content.EnableInClassList("hidden", !hasRecipe);
            if (!hasRecipe) return;

            RecipeDefinition recipe = gameRuntime.Recipes.Get(recipeId);
            _currentIcon.style.backgroundImage = new StyleBackground(ResolveItemIcon(recipeId));
            _currentAmount.text = $"×{(recipe != null ? recipe.OutputAmount : 1)}";
            _currentName.text = ResolveItemName(recipeId).ToUpperInvariant();

            _ingredientsList.Clear();
            IReadOnlyDictionary<string, int> required = _selected.GetRequiredIngredients();
            foreach (var kvp in required)
            {
                _ingredientsList.Add(BuildIngredientRow(kvp.Key, kvp.Value));
            }

            _powerValue.text = $"{_selected.GetPowerDemandKw():0} kW";
            _computeValue.text = $"{(recipe != null ? recipe.ComputeCost : 0f):0} CU";

            float progress = _selected.GetProgress();
            _progressFill.style.width = new StyleLength(Length.Percent(progress * 100f));
            _percentLabel.text = $"{Mathf.RoundToInt(progress * 100f)} %";

            float remaining = _selected.GetProductionTime() * (1f - progress);
            _timeLabel.text = $"{remaining:0.0}s restantes";

            ProductionState state = _selected.GetState();
            _stateLabel.text = "● " + _selected.GetStateLabel();
            _stateLabel.RemoveFromClassList("state-producing");
            _stateLabel.RemoveFromClassList("state-waiting");
            _stateLabel.RemoveFromClassList("state-blocked");
            _stateLabel.RemoveFromClassList("state-idle");
            _stateLabel.AddToClassList(StateClass(state));
        }

        /// <summary>Everything the building currently holds internally - raw materials waiting on a cycle (input) and finished goods waiting to be pushed out (output) - regardless of whether a recipe is selected, so a jam (e.g. output backing up) is visible at a glance.</summary>
        void RefreshStockList()
        {
            _stockList.Clear();
            foreach (var kvp in _selected.GetInputContents())
            {
                if (kvp.Value <= 0) continue;
                _stockList.Add(BuildStockRow(kvp.Key, kvp.Value, "ENTREE"));
            }
            foreach (var kvp in _selected.GetOutputContents())
            {
                if (kvp.Value <= 0) continue;
                _stockList.Add(BuildStockRow(kvp.Key, kvp.Value, "SORTIE"));
            }
        }

        VisualElement BuildStockRow(string itemId, int amount, string sideLabel)
        {
            var row = new VisualElement();
            row.AddToClassList("production-ingredient-row");

            var icon = new VisualElement();
            icon.AddToClassList("production-ingredient-icon");
            icon.style.backgroundImage = new StyleBackground(ResolveItemIcon(itemId));
            row.Add(icon);

            var name = new Label(ResolveItemName(itemId));
            name.AddToClassList("production-ingredient-name");
            row.Add(name);

            var status = new Label($"{amount}  ({sideLabel})");
            status.AddToClassList("production-ingredient-status-ok");
            row.Add(status);

            return row;
        }

        VisualElement BuildIngredientRow(string itemId, int need)
        {
            int have = _selected.GetInputAmount(itemId);
            bool ok = have >= need;

            var row = new VisualElement();
            row.AddToClassList("production-ingredient-row");

            var icon = new VisualElement();
            icon.AddToClassList("production-ingredient-icon");
            icon.style.backgroundImage = new StyleBackground(ResolveItemIcon(itemId));
            row.Add(icon);

            var name = new Label(ResolveItemName(itemId));
            name.AddToClassList("production-ingredient-name");
            row.Add(name);

            var status = new Label($"{have} / {need}  {(ok ? "✓" : "✕")}");
            status.AddToClassList(ok ? "production-ingredient-status-ok" : "production-ingredient-status-missing");
            row.Add(status);

            return row;
        }

        Sprite ResolveItemIcon(string itemId)
        {
            ItemDefinition item = gameRuntime.Items != null ? gameRuntime.Items.Get(itemId) : null;
            if (item != null && item.Icon != null) return item.Icon;
            return _spriteFactory.CreateSolidSquareSprite(item != null ? item.FallbackColor : Color.magenta);
        }

        string ResolveItemName(string itemId)
        {
            ItemDefinition item = gameRuntime.Items != null ? gameRuntime.Items.Get(itemId) : null;
            return item != null && !string.IsNullOrEmpty(item.DisplayName) ? item.DisplayName : itemId;
        }

        static string StateClass(ProductionState state) => state switch
        {
            ProductionState.Producing => "state-producing",
            ProductionState.WaitingResources => "state-waiting",
            ProductionState.WaitingCompute => "state-waiting",
            ProductionState.OutputBlocked => "state-blocked",
            _ => "state-idle"
        };
    }
}

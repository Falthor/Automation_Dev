using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Directives;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Contextual Core inspector - shows the Core's own inventory (never Storage contents, see
    /// StoragePanel for the merged global view) plus the global Power/Compute aggregate supply
    /// (CONTRACTS.md §9/§10/§12) - not Core-specific numbers, matching the source project.
    /// </summary>
    public sealed class CorePanelController : MonoBehaviour
    {
        /// <summary>What a directive's REWARD line says when it grants an unlock but names no wording of its own - the honest thing to say when a directive opens several researches at once.</summary>
        const string GenericResearchReward = "Deverrouille des recherches";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;
        [SerializeField] Sprite powerIcon;
        [SerializeField] Sprite computeIcon;

        VisualElement _root;
        Label _computeLabel;
        Label _powerLabel;
        VisualElement _itemsList;
        CoreRuntime _selected;

        VisualElement _directive;
        VisualElement _requirements;
        VisualElement _rewardTitle;
        VisualElement _rewardItem;
        VisualElement _rewardResearchList;
        VisualElement _rewardIcon;
        Label _rewardName;
        Button _validateButton;

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("CorePanelRoot");
            _computeLabel = panelRoot.Q<Label>("CoreComputeLabel");
            _powerLabel = panelRoot.Q<Label>("CorePowerLabel");
            _itemsList = panelRoot.Q<VisualElement>("CoreItemsList");
            if (computeIcon != null) panelRoot.Q<VisualElement>("CoreComputeIcon").style.backgroundImage = new StyleBackground(computeIcon);
            if (powerIcon != null) panelRoot.Q<VisualElement>("CorePowerIcon").style.backgroundImage = new StyleBackground(powerIcon);
            panelRoot.Q<Button>("CoreCloseButton").clicked += Close;

            _directive = panelRoot.Q<VisualElement>("CoreDirective");
            _requirements = panelRoot.Q<VisualElement>("CoreDirectiveRequirements");
            _rewardTitle = panelRoot.Q<Label>("CoreDirectiveRewardTitle");
            _rewardItem = panelRoot.Q<VisualElement>("CoreDirectiveRewardItem");
            _rewardResearchList = panelRoot.Q<VisualElement>("CoreDirectiveRewardResearchList");
            _rewardIcon = panelRoot.Q<VisualElement>("CoreDirectiveRewardIcon");
            _rewardName = panelRoot.Q<Label>("CoreDirectiveRewardName");
            _validateButton = panelRoot.Q<Button>("CoreDirectiveValidate");
            _validateButton.clicked += OnValidateClicked;

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.SelectionChanged += OnSelectionChanged;
        }

        void OnDestroy() => gameRuntime.Selection.SelectionChanged -= OnSelectionChanged;

        void OnSelectionChanged(BuildingRuntime building)
        {
            _selected = building as CoreRuntime;
            _root.EnableInClassList("hidden", _selected == null);
            if (_selected != null) Render();
        }

        void Close() => gameRuntime.Selection.Clear();

        void Update()
        {
            if (_selected == null) return;

            if (gameRuntime.Escape.IsClaimedBy(EscapeClaimant.ContextualPanel))
            {
                Close();
                return;
            }

            Render();
        }

        void OnValidateClicked()
        {
            if (gameRuntime.CoreDirectives != null && gameRuntime.CoreDirectives.Validate(_selected)) gameRuntime.NotePlayerAction();
        }

        /// <summary>
        /// The Core's current directive, or nothing at all once they are done. Rebuilt every frame
        /// like the rest of this panel: the numbers move as the world produces, and the button has
        /// to grey and un-grey with them.
        ///
        /// Before validating, each requirement reads against the stock a robot could actually go and
        /// claim - the same aggregate the button is enabled from, so the figures and the button can
        /// never disagree. After validating it reads what has physically reached the Core, which is
        /// the only number that still means anything: the material has left the chests already.
        /// </summary>
        void RenderDirective()
        {
            CoreDirectiveSystem directives = gameRuntime.CoreDirectives;
            CoreDirectiveDefinition current = directives?.Current;

            _directive.EnableInClassList("hidden", current == null);
            if (current == null) return;

            bool delivering = directives.IsDelivering;
            IReadOnlyDictionary<string, int> available = gameRuntime.DirectiveStock;

            _requirements.Clear();
            foreach (RecipeIngredient requirement in current.Requirements)
            {
                if (requirement.Item == null || requirement.Amount <= 0) continue;

                int held = delivering
                    ? directives.DeliveredOf(requirement.Item.Id)
                    : available != null && available.TryGetValue(requirement.Item.Id, out int stock) ? stock : 0;

                _requirements.Add(BuildRequirement(requirement, Mathf.Min(held, requirement.Amount)));
            }

            // A directive can reward an item, an unlock, or both -
            // and the whole REWARD block steps aside when there is nothing to promise, rather than
            // showing a heading over an empty row.
            //
            // The unlock is announced only when no item stands for it. Every directive grants one, so
            // showing it unconditionally would put a second line under the Gear icon that already
            // says exactly the same thing: the reward is named once, by whichever names it best.
            ItemDefinition reward = current.RewardItem;
            bool hasItemReward = reward != null;
            bool hasResearchReward = !hasItemReward && current.Grants != null;

            if (hasItemReward && reward.Icon != null) _rewardIcon.style.backgroundImage = new StyleBackground(reward.Icon);
            _rewardName.text = hasItemReward ? reward.DisplayName : string.Empty;
            RebuildResearchRewardRows(hasResearchReward ? current.RewardLabels : null);

            _rewardItem.EnableInClassList("hidden", !hasItemReward);
            _rewardResearchList.EnableInClassList("hidden", !hasResearchReward);
            _rewardTitle.EnableInClassList("hidden", !hasItemReward && !hasResearchReward);

            _validateButton.text = delivering ? "LIVRAISON EN COURS" : "VALIDER";
            // No stock handed in: the system reads the one view the haul reserves from. `available`
            // above is the same figure, shown - but showing and deciding are now the same answer by
            // construction rather than by this panel passing the right dictionary.
            _validateButton.SetEnabled(directives.CanValidate());
        }

        /// <summary>
        /// One row per thing the directive opens, rebuilt from scratch each refresh - there are at
        /// most a handful and they only change when the directive does.
        ///
        /// An empty or absent list still gets one row, worded generically: a directive always grants
        /// an unlock, so saying nothing at all would be the one wrong answer.
        /// </summary>
        void RebuildResearchRewardRows(string[] labels)
        {
            _rewardResearchList.Clear();

            if (labels == null) return;
            if (labels.Length == 0) labels = new[] { GenericResearchReward };

            foreach (string label in labels)
            {
                if (string.IsNullOrEmpty(label)) continue;

                var row = new VisualElement();
                row.AddToClassList("core-directive-reward");

                var glyph = new VisualElement();
                glyph.AddToClassList("core-directive-reward-glyph");
                row.Add(glyph);

                var name = new Label(label);
                name.AddToClassList("core-directive-reward-name");
                row.Add(name);

                _rewardResearchList.Add(row);
            }
        }

        /// <summary>One requirement: a large icon with stock-over-target underneath, per the Core panel's own layout rather than the compact ingredient rows used elsewhere.</summary>
        VisualElement BuildRequirement(RecipeIngredient requirement, int held)
        {
            var box = new VisualElement();
            box.AddToClassList("core-directive-requirement");

            var icon = new VisualElement();
            icon.AddToClassList("core-directive-requirement-icon");
            if (requirement.Item.Icon != null) icon.style.backgroundImage = new StyleBackground(requirement.Item.Icon);
            box.Add(icon);

            var amount = new Label($"{held}/{requirement.Amount}");
            amount.AddToClassList("core-directive-requirement-amount");
            amount.EnableInClassList("core-directive-requirement-met", held >= requirement.Amount);
            box.Add(amount);

            return box;
        }

        void Render()
        {
            RenderDirective();

            _computeLabel.text = $"Compute: {Mathf.RoundToInt(gameRuntime.Compute.IncomePerSecond)} CU/s";
            _powerLabel.text = $"Power: {Mathf.RoundToInt(gameRuntime.Power.SettledSupply)} kW";

            _itemsList.Clear();
            foreach (var kvp in _selected.GetContents())
            {
                if (kvp.Value <= 0) continue;

                var row = new VisualElement();
                row.AddToClassList("production-ingredient-row");

                var icon = new VisualElement();
                icon.AddToClassList("production-ingredient-icon");
                var item = gameRuntime.Items.Get(kvp.Key);
                if (item != null && item.Icon != null) icon.style.backgroundImage = new StyleBackground(item.Icon);
                row.Add(icon);

                var name = new Label(item != null ? item.DisplayName : kvp.Key);
                name.AddToClassList("production-ingredient-name");
                row.Add(name);

                var amount = new Label(kvp.Value.ToString());
                amount.AddToClassList("core-item-amount");
                row.Add(amount);

                _itemsList.Add(row);
            }
        }
    }
}

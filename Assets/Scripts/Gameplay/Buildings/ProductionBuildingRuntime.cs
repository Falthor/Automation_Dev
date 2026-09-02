using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;
using Game.Gameplay.Research;

namespace Game.Gameplay.Buildings
{
    /// <summary>
    /// Generic single-active-recipe production contract (CONTRACTS.md §6), shared by every
    /// recipe-based production building (Foundry today; Factory/AdvancedFoundry/Assembler in
    /// later phases). A recipe cycle takes ALL its ingredients and its one-shot Compute cost at
    /// once, the moment it starts (transition into Producing) - switching recipes mid-cycle
    /// abandons it without refunding what was already taken (CONTRACTS.md §6). Power demand is
    /// reported only while actually Producing; Compute is never a continuous draw for these
    /// buildings, only the recipe's one-shot cost spent from the global reserve.
    /// </summary>
    public class ProductionBuildingRuntime : BuildingRuntime
    {
        readonly RecipeDatabase _recipeDatabase;
        readonly ComputeSystem _computeSystem;
        readonly PowerSystem _powerSystem;
        readonly ResearchSystem _researchSystem;
        readonly string[] _acceptedItemIds;
        readonly float _powerDemandKw;
        readonly PooledItemStock _input;
        readonly PooledItemStock _output;

        string _selectedRecipeId;
        bool _crafting;
        float _timer;
        ProductionState _state = ProductionState.Idle;

        protected ProductionBuildingRuntime(
            BuildingDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem,
            int maxStackPerItem, float powerDemandKw, string[] acceptedItemIds = null)
            : base(definition, cell, facingRotation)
        {
            _recipeDatabase = recipeDatabase;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            _acceptedItemIds = acceptedItemIds;
            _powerDemandKw = powerDemandKw;
            _input = new PooledItemStock(maxStackPerItem);
            _output = new PooledItemStock(maxStackPerItem);
        }

        RecipeDefinition SelectedRecipeDefinition => _recipeDatabase.Get(_selectedRecipeId);

        /// <summary>Recipe ids this concrete building type may ever offer. Empty by default - a real production building must override this.</summary>
        protected virtual string[] GetRecipeIdWhitelist() => System.Array.Empty<string>();

        /// <summary>
        /// Whether the output side also blocks incoming deliveries (true by default, matching
        /// the source project's Building default - Storage is the one exception, and it doesn't
        /// go through this contract at all).
        /// </summary>
        protected virtual bool BlocksInputOnOutputSide => true;

        /// <summary>
        /// Coarse item-type/accepted-list filter layered under the recipe-ingredient filter.
        /// Default: the constructor-supplied acceptedItemIds list (null/empty = no restriction).
        /// Foundry overrides this with an ItemType.Ore check instead of a fixed list.
        /// </summary>
        protected virtual bool AcceptsItemType(string itemId)
        {
            return _acceptedItemIds == null || _acceptedItemIds.Length == 0 || System.Array.IndexOf(_acceptedItemIds, itemId) >= 0;
        }

        // ---- CONTRACTS.md §6 ----

        /// <summary>Every recipe in this building's whitelist that also exists in the database and isn't behind an unfinished research.</summary>
        public IReadOnlyList<string> GetRecipeIds()
        {
            var result = new List<string>();
            foreach (string id in GetRecipeIdWhitelist())
            {
                RecipeDefinition recipe = _recipeDatabase.Get(id);
                if (recipe == null) continue;
                if (recipe.UnlockResearch != null && !_researchSystem.IsUnlocked(recipe.UnlockResearch.Id)) continue;
                result.Add(id);
            }
            return result;
        }

        public string GetSelectedRecipe() => _selectedRecipeId ?? string.Empty;

        public void SetSelectedRecipe(string recipeId)
        {
            if (recipeId == GetSelectedRecipe()) return;
            if (!string.IsNullOrEmpty(recipeId) && !GetRecipeIds().Contains(recipeId)) return;

            _selectedRecipeId = string.IsNullOrEmpty(recipeId) ? null : recipeId;
            _crafting = false;
            _timer = 0f;
        }

        public float GetProductionTime() => SelectedRecipeDefinition?.TimeSeconds ?? 0f;

        public IReadOnlyDictionary<string, int> GetRequiredIngredients()
        {
            var result = new Dictionary<string, int>();
            RecipeDefinition recipe = SelectedRecipeDefinition;
            if (recipe == null) return result;

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                result[ingredient.Item.Id] = ingredient.Amount;
            }
            return result;
        }

        public float GetProgress()
        {
            if (!_crafting) return 0f;
            float time = GetProductionTime();
            if (time <= 0f) return 0f;
            float t = _timer / time;
            if (t < 0f) return 0f;
            return t > 1f ? 1f : t;
        }

        public bool HasRequiredResources() => HasIngredients(GetRequiredIngredients());

        public bool HasResourcesFor(string recipeId)
        {
            RecipeDefinition recipe = _recipeDatabase.Get(recipeId);
            if (recipe == null) return false;

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                if (_input.GetAmount(ingredient.Item.Id) < ingredient.Amount) return false;
            }
            return true;
        }

        bool HasIngredients(IReadOnlyDictionary<string, int> ingredients)
        {
            foreach (var kvp in ingredients)
            {
                if (_input.GetAmount(kvp.Key) < kvp.Value) return false;
            }
            return true;
        }

        /// <summary>Configured Power demand (kW) while Producing - static/definition-derived, for the UI's consumption display.</summary>
        public float GetPowerDemandKw() => _powerDemandKw;

        public ProductionState GetState() => _state;

        public string GetStateLabel() => _state switch
        {
            ProductionState.Producing => "PRODUCTION",
            ProductionState.WaitingResources => "EN ATTENTE DE RESSOURCES",
            ProductionState.OutputBlocked => "SORTIE PLEINE",
            ProductionState.WaitingCompute => "COMPUTE INSUFFISANT",
            _ => "ARRET"
        };

        /// <summary>
        /// Advances the production state machine; call once per simulation tick. Power demand
        /// is reported based on whether this building was Producing at the END of the PREVIOUS
        /// tick (the same one-frame settle lag PowerSystem/ComputeSystem already have) - if
        /// unpowered, the effective delta passed to the state machine (and to
        /// OnBeforeProductionTick) is scaled to 0, freezing an in-progress cycle's timer in
        /// place without losing already-consumed ingredients/compute (matches the source
        /// project's Building._process -> _process_production(delta * performance) chain).
        /// </summary>
        public override void Tick(float deltaTime)
        {
            float performance = ComputeEffectivePerformance(
                cuDemand: 0f, computeActive: false,
                powerDemand: _powerDemandKw, powerActive: _state == ProductionState.Producing,
                _computeSystem, _powerSystem);

            float effectiveDeltaTime = deltaTime * performance;
            OnBeforeProductionTick(effectiveDeltaTime);
            RunProductionStateMachine(effectiveDeltaTime);
        }

        /// <summary>Hook for a subclass's own per-tick bookkeeping (e.g. Foundry's intake cooldown) that must freeze in lockstep with production while unpowered.</summary>
        protected virtual void OnBeforeProductionTick(float effectiveDeltaTime)
        {
        }

        void RunProductionStateMachine(float deltaTime)
        {
            if (string.IsNullOrEmpty(_selectedRecipeId))
            {
                _crafting = false;
                _timer = 0f;
                _state = ProductionState.Idle;
                return;
            }

            RecipeDefinition recipe = SelectedRecipeDefinition;
            if (recipe == null)
            {
                _state = ProductionState.Idle;
                return;
            }

            if (!_crafting)
            {
                if (!HasRequiredResources())
                {
                    _state = ProductionState.WaitingResources;
                    return;
                }

                if (_output.GetAmount(recipe.Id) + recipe.OutputAmount > _output.MaxStackPerItem)
                {
                    _state = ProductionState.OutputBlocked;
                    return;
                }

                if (!_computeSystem.CanSpend(recipe.ComputeCost))
                {
                    _state = ProductionState.WaitingCompute;
                    return;
                }

                // All checks passed: the cycle can genuinely run to completion, so its Compute
                // cost and ingredients are taken right now, not reserved-and-taken-later.
                // Changing recipe from this point on does NOT refund these - they're already gone.
                _computeSystem.Spend(recipe.ComputeCost);
                foreach (RecipeIngredient ingredient in recipe.Ingredients)
                {
                    _input.Take(ingredient.Item.Id, ingredient.Amount);
                }
                _crafting = true;
                _timer = 0f;
            }

            _state = ProductionState.Producing;
            _timer += deltaTime;
            if (_timer < recipe.TimeSeconds) return;

            _output.Add(recipe.Id, recipe.OutputAmount);
            _crafting = false;
            _timer = 0f;
        }

        // ---- Building/Inventory contract (CONTRACTS.md §3) ----

        public override bool CanAcceptInput(string itemId, int amount, Direction fromDirection)
        {
            if (BlocksInputOnOutputSide && fromDirection == ExitDirection) return false;
            if (!AcceptsItemType(itemId)) return false;
            if (!GetRequiredIngredients().ContainsKey(itemId)) return false;
            return _input.CanAccept(itemId, amount);
        }

        public override void AddInput(string itemId, int amount, Direction fromDirection) => _input.Add(itemId, amount);
        public override int TakeInput(string itemId, int amount) => _input.Take(itemId, amount);
        public override int GetInputAmount(string itemId) => _input.GetAmount(itemId);
        public override void AddOutput(string itemId, int amount) => _output.Add(itemId, amount);
        public override int TakeOutput(string itemId, int amount) => _output.Take(itemId, amount);
        public override IReadOnlyDictionary<string, int> GetOutputContents() => _output.Contents;

        // ---- Building/Flow contract (CONTRACTS.md §2) - lets an existing conveyor placed
        // behind this building pull its output exactly like it already pulls from an Extractor. ----

        public override object PeekPullableItem()
        {
            foreach (var kvp in _output.Contents)
            {
                if (kvp.Value > 0) return kvp.Key;
            }
            return null;
        }

        public override void ConsumePulledItem(object item)
        {
            if (item is string itemId) _output.Take(itemId, 1);
        }
    }
}

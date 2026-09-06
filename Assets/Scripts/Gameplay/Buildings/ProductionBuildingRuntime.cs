using System.Collections.Generic;
using System.Linq;
using Game.Core;
using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Items;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// How many crafts' worth of each raw material a production building holds at most: the
        /// recipe's per-craft amount times this. A recipe needing 2 iron and 4 screws therefore
        /// buffers 6 and 12 - a building's intake is sized by what it actually consumes, not by one
        /// number shared across every ingredient and every building.
        /// </summary>
        public const int InputCraftsHeld = 3;

        /// <summary>Finished goods a production building stacks before it stops and reports OutputBlocked. Flat, unlike the input: the output is a buffer against a stalled belt, and how big that buffer should be has nothing to do with the recipe.</summary>
        public const int OutputStackCapacity = 10;

        protected ProductionBuildingRuntime(
            BuildingDefinition definition, GridCoord cell, Direction facingRotation,
            RecipeDatabase recipeDatabase, ComputeSystem computeSystem, PowerSystem powerSystem, ResearchSystem researchSystem,
            float powerDemandKw, string[] acceptedItemIds = null)
            : base(definition, cell, facingRotation)
        {
            _recipeDatabase = recipeDatabase;
            _computeSystem = computeSystem;
            _powerSystem = powerSystem;
            _researchSystem = researchSystem;
            _acceptedItemIds = acceptedItemIds;
            _powerDemandKw = powerDemandKw;

            // Read live, not captured: the ceilings follow the selected recipe as it changes.
            _input = new PooledItemStock(InputCapacityFor);
            _output = new PooledItemStock(_ => OutputStackCapacity);
        }

        /// <summary>
        /// How much of one raw material this building may hold: what the selected recipe consumes of
        /// it per craft, times InputCraftsHeld. Zero for anything the recipe does not use - including
        /// everything, while no recipe is selected - which is the same answer CanAcceptInput already
        /// gives from the other direction.
        /// </summary>
        int InputCapacityFor(string itemId)
        {
            RecipeDefinition recipe = SelectedRecipeDefinition;
            if (recipe == null) return 0;

            foreach (RecipeIngredient ingredient in recipe.Ingredients)
            {
                if (ingredient.Item != null && ingredient.Item.Id == itemId) return ingredient.Amount * InputCraftsHeld;
            }
            return 0;
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
            ProductionState.Paused => "EN PAUSE",
            _ => "ARRET"
        };

        /// <summary>
        /// Switched off by the player: no power reported, no Compute spent, no cycle advanced.
        ///
        /// An in-progress craft freezes rather than being abandoned - its ingredients and its CU are
        /// already spent and are not refunded, exactly as they are not when the power drops. Pausing
        /// is a way to stop a building drawing on a strained network, not a way to take a cycle back.
        /// </summary>
        public bool IsPaused { get; private set; }

        public void SetPaused(bool paused) => IsPaused = paused;

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
            // Before anything reports demand: a paused building must be invisible to the power
            // network, not merely idle on it. Returning here is what makes "consumes nothing" true -
            // ComputeEffectivePerformance below is the only thing that ever reports this building's
            // draw, and it is never reached.
            if (IsPaused)
            {
                _state = ProductionState.Paused;
                return;
            }

            float performance = ComputeEffectivePerformance(_powerDemandKw, powerActive: _state == ProductionState.Producing, _powerSystem);

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

                if (_output.GetAmount(recipe.Id) + recipe.OutputAmount > _output.CapacityFor(recipe.Id))
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

        /// <summary>Read-only snapshot of everything currently held in input (raw materials waiting on a cycle to start) - mirrors GetOutputContents() (CONTRACTS.md §3), for a caller that needs to display or enumerate a building's whole internal stock rather than a single item's amount (GetInputAmount).</summary>
        public IReadOnlyDictionary<string, int> GetInputContents() => _input.Contents;

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

        // ---- Save/Restore (CONTRACTS.md §14) ----

        public override JObject CaptureState()
        {
            return new JObject
            {
                ["recipeId"] = _selectedRecipeId ?? string.Empty,
                ["crafting"] = _crafting,
                ["timer"] = _timer,
                ["state"] = (int)_state,
                // A switched-off building must come back switched off: reloading is not a reason to
                // put a factory back onto a network the player deliberately took it off.
                ["paused"] = IsPaused,
                ["input"] = JObject.FromObject(_input.Contents),
                ["output"] = JObject.FromObject(_output.Contents)
            };
        }

        public override void RestoreState(JObject state)
        {
            string recipeId = state.Value<string>("recipeId");
            _selectedRecipeId = string.IsNullOrEmpty(recipeId) ? null : recipeId;
            _crafting = state.Value<bool?>("crafting") ?? false;
            _timer = state.Value<float?>("timer") ?? 0f;
            _state = (ProductionState)(state.Value<int?>("state") ?? 0);
            IsPaused = state.Value<bool?>("paused") ?? false;
            _input.RestoreContents(state["input"]?.ToObject<Dictionary<string, int>>());
            _output.RestoreContents(state["output"]?.ToObject<Dictionary<string, int>>());
        }
    }
}

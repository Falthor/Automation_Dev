using System;
using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Research;
using Game.Gameplay.Sites;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Directives
{
    /// <summary>
    /// What the Core is currently asking the player for, and what happens when they say yes.
    ///
    /// One directive at a time, in the order CoreDirectiveDatabase lists them. The player sees the
    /// bill and validates it; validating hands the bill to ConstructionSiteSystem, which reserves
    /// the material and sends the robots exactly as it would for a building. When the last unit
    /// lands, the directive's unlock is granted through ResearchSystem.Grant - an ordinary unlock
    /// id, so recipe and building gates need no second notion of "available" - and the next
    /// directive becomes current. Past the last one there is nothing, and the Core panel is empty.
    ///
    /// Ticked by nobody: it is driven entirely by the player validating and by the haul completing.
    /// </summary>
    public sealed class CoreDirectiveSystem
    {
        readonly IReadOnlyList<CoreDirectiveDefinition> _directives;
        readonly ConstructionSiteSystem _sites;
        readonly ResearchSystem _research;

        int _index;

        /// <summary>Fired when a directive completes, with the unlock it granted - the Core panel listens so it can stop showing it.</summary>
        public event Action<CoreDirectiveDefinition> DirectiveCompleted;

        public CoreDirectiveSystem(CoreDirectiveDatabase database, ConstructionSiteSystem sites, ResearchSystem research)
        {
            _directives = database != null ? database.GetAll() : Array.Empty<CoreDirectiveDefinition>();
            _sites = sites;
            _research = research;

            if (_sites != null) _sites.CoreHaulCompleted += OnHaulCompleted;
        }

        /// <summary>The directive being asked for, or null once they have all been fulfilled - which is what empties the Core panel.</summary>
        public CoreDirectiveDefinition Current => _index >= 0 && _index < _directives.Count ? _directives[_index] : null;

        /// <summary>Which directive this is, counting from 1 as the player counts them - 0 when there is none left. What the Top Bar puts in front of the bill.</summary>
        public int CurrentNumber => Current != null ? _index + 1 : 0;

        /// <summary>Whether the player has validated the current directive and the robots are on it. While true the panel shows what has landed rather than what is available.</summary>
        public bool IsDelivering => _sites != null && _sites.CoreHaul != null;

        /// <summary>
        /// Whether the Research menu has been handed over yet - the Top Bar card and the Bottom Nav
        /// icon are both absent until it has.
        ///
        /// Derived from the directives already completed rather than stored, so it needs nothing of
        /// its own in the save: the index already says which ones are done, and a flag on top of it
        /// could only ever disagree with them.
        /// </summary>
        public bool IsResearchMenuUnlocked
        {
            get
            {
                for (int i = 0; i < _index && i < _directives.Count; i++)
                {
                    if (_directives[i] != null && _directives[i].UnlocksResearchMenu) return true;
                }
                return false;
            }
        }

        /// <summary>How much of one requirement has physically reached the Core, once validated. Zero before that: nothing has been carried yet.</summary>
        public int DeliveredOf(string itemId)
            => _sites != null && _sites.CoreHaul != null ? _sites.CoreHaul.DeliveredOf(itemId) : 0;

        /// <summary>
        /// Whether the current directive can be validated right now: every requirement covered by
        /// stock a robot could actually go and claim. The same aggregate the player is shown, so the
        /// button is grey exactly when the numbers underneath say it should be.
        /// </summary>
        public bool CanValidate(IReadOnlyDictionary<string, int> availableStock)
        {
            CoreDirectiveDefinition directive = Current;
            if (directive == null || IsDelivering || availableStock == null) return false;

            foreach (RecipeIngredient requirement in directive.Requirements)
            {
                if (requirement.Item == null || requirement.Amount <= 0) continue;
                if (!availableStock.TryGetValue(requirement.Item.Id, out int held) || held < requirement.Amount) return false;
            }
            return true;
        }

        /// <summary>
        /// The player pressed Validate: the bill goes to the robots. Refused - rather than queued -
        /// when the stock is not there or a delivery is already running, so the button being enabled
        /// and this succeeding are the same condition.
        /// </summary>
        public bool Validate(IReadOnlyDictionary<string, int> availableStock, BuildingRuntime core)
        {
            if (core == null || !CanValidate(availableStock)) return false;

            var bill = new Dictionary<string, int>();
            foreach (RecipeIngredient requirement in Current.Requirements)
            {
                if (requirement.Item == null || requirement.Amount <= 0) continue;
                bill[requirement.Item.Id] = requirement.Amount;
            }

            return _sites.BeginCoreHaul(core, bill);
        }

        void OnHaulCompleted()
        {
            CoreDirectiveDefinition completed = Current;
            if (completed == null) return;

            if (completed.Grants != null) _research?.Grant(completed.Grants.Id);

            _index++;
            DirectiveCompleted?.Invoke(completed);
        }

        // ---- Save / Restore (CONTRACTS.md §14 convention) ----

        /// <summary>
        /// Only the index is saved. A delivery in flight is not: its material has already left the
        /// containers and sits in robots that the save does restore, so persisting the haul as well
        /// would have the same units counted twice. A reload therefore re-offers the directive with
        /// whatever stock is actually there, which is the honest state.
        /// </summary>
        public JObject CaptureState() => new JObject { ["index"] = _index };

        public void RestoreState(JObject state)
        {
            _index = state?.Value<int?>("index") ?? 0;
        }
    }
}

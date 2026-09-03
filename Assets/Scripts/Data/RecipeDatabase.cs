using System.Collections.Generic;
using UnityEngine;

namespace Game.Data
{
    /// <summary>Single source of truth for recipes, keyed by the produced item's id.</summary>
    [CreateAssetMenu(fileName = "RecipeDatabase", menuName = "Game/Items/Recipe Database")]
    public sealed class RecipeDatabase : ScriptableObject
    {
        [SerializeField] RecipeDefinition[] recipes;

        Dictionary<string, RecipeDefinition> _byId;

        /// <summary>Returns the recipe for recipeId, or null if it isn't registered.</summary>
        public RecipeDefinition Get(string recipeId)
        {
            if (_byId == null) BuildLookup();
            return recipeId != null && _byId.TryGetValue(recipeId, out var recipe) ? recipe : null;
        }

        void BuildLookup()
        {
            _byId = new Dictionary<string, RecipeDefinition>();
            if (recipes == null) return;

            foreach (RecipeDefinition recipe in recipes)
            {
                if (recipe != null && !string.IsNullOrEmpty(recipe.Id)) _byId[recipe.Id] = recipe;
            }
        }
    }
}

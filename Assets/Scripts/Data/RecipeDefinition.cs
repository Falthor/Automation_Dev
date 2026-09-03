using System;
using UnityEngine;

namespace Game.Data
{
    /// <summary>One ingredient requirement: itemId + amount consumed per craft.</summary>
    [Serializable]
    public struct RecipeIngredient
    {
        [SerializeField] ItemDefinition item;
        [SerializeField] int amount;

        public ItemDefinition Item => item;
        public int Amount => amount;
    }

    /// <summary>
    /// Static crafting recipe. Id is always the produced item's id (same convention as the
    /// source project's Items.RECIPES) - callers never need a separate "what item does this
    /// recipe make" lookup. ComputeCost is a one-time deduction at cycle start (CONTRACTS.md
    /// §10: "continuous demand and one-time cycle costs remain distinct concepts"), never a
    /// per-second draw.
    /// </summary>
    [CreateAssetMenu(fileName = "RecipeDefinition", menuName = "Game/Items/Recipe Definition")]
    public sealed class RecipeDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] RecipeIngredient[] ingredients;
        [SerializeField, Min(1)] int outputAmount = 1;
        [SerializeField, Min(0.01f)] float timeSeconds = 1f;
        [SerializeField, Min(0f)] float computeCost;
        [SerializeField] ResearchDefinition unlockResearch;

        public string Id => id;
        public RecipeIngredient[] Ingredients => ingredients;
        public int OutputAmount => outputAmount;
        public float TimeSeconds => timeSeconds;
        public float ComputeCost => computeCost;

        /// <summary>Research required before this recipe is offered by GetRecipeIds(). Null means available from the start.</summary>
        public ResearchDefinition UnlockResearch => unlockResearch;
    }
}

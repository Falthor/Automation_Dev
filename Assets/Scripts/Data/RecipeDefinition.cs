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

        public string Id => id;
        public RecipeIngredient[] Ingredients => ingredients;
        public int OutputAmount => outputAmount;
        public float TimeSeconds => timeSeconds;
        public float ComputeCost => computeCost;

        /// <summary>
        /// How many times this recipe completes in a minute at full speed. The bridge between a
        /// duration - which is what the recipe is authored in - and a rate, which is what the player
        /// plans a factory with: nobody sizes a belt from "3 seconds".
        /// </summary>
        public float CraftsPerMinute => timeSeconds > 0f ? 60f / timeSeconds : 0f;

        /// <summary>
        /// Units of the produced item per minute: the yield of one craft times the crafts a minute
        /// holds. Four plates every three seconds is 80/min, one ingot every three seconds is 20/min.
        ///
        /// Derived here rather than configured or computed by each panel that shows it, for the same
        /// reason ExtractorDefinition.ItemsPerMinute is: retuning the time or the yield moves every
        /// figure on screen with it, and no display can drift from the recipe it describes.
        ///
        /// A rating, not a promise. It assumes ingredients always present, full power and the CU to
        /// pay each cycle; a building short of any of those produces less. What one ingredient
        /// <b>demands</b> per minute is the same arithmetic from the other side - its per-craft
        /// amount times CraftsPerMinute.
        /// </summary>
        public float OutputPerMinute => outputAmount * CraftsPerMinute;
    }
}

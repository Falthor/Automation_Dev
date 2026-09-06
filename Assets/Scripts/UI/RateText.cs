namespace Game.UI
{
    /// <summary>
    /// How a per-minute rate is written on screen, in one place.
    ///
    /// The figure now appears on a recipe card, under a production progress bar, beside each raw
    /// material and under the extractor's bar - four labels the player reads as one kind of number,
    /// which they only do while they are punctuated and rounded identically. The rates themselves
    /// are owned by whoever knows them (RecipeDefinition.OutputPerMinute,
    /// ExtractorDefinition.ItemsPerMinute); this only spells them.
    /// </summary>
    public static class RateText
    {
        /// <summary>
        /// "80/min", "8.6/min". One decimal only when there is one to show: a rate is usually whole
        /// (a 3 s recipe divides 60 exactly) and "80.0/min" reads like a measurement rather than a
        /// property of the recipe.
        /// </summary>
        public static string PerMinute(float rate) => $"{rate:0.#}/min";
    }
}

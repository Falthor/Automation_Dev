using Game.Data;
using Game.Tests.EditMode.TestSupport;
using Game.UI;
using NUnit.Framework;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The per-minute figures the recipe cards, the production panel and the extractor panel now
    /// show, held apart from the panels themselves so they can be checked without a UIDocument -
    /// same approach as ConstructionSitePanelFormatTests.
    ///
    /// A recipe is authored as a duration and a yield; a player plans a factory in rates. The
    /// conversion between them is the whole of what these tests pin, because it is what the screen
    /// asserts to the player and there is nothing else in the game to contradict it.
    /// </summary>
    public class RecipeRateTests
    {
        static ItemDefinition Ore() => TestDataFactory.NewItem("iron_ore", ItemType.Ore);

        /// <summary>The two worked examples the figure was asked for, verbatim.</summary>
        [Test]
        public void OutputPerMinute_IsTheYieldTimesTheCraftsAMinuteHolds()
        {
            RecipeDefinition plates = TestDataFactory.NewRecipe("iron_plate", timeSeconds: 3f, computeCost: 0f, outputAmount: 4, (Ore(), 1));
            RecipeDefinition ingot = TestDataFactory.NewRecipe("iron_ingot", timeSeconds: 3f, computeCost: 0f, outputAmount: 1, (Ore(), 1));

            Assert.AreEqual(20f, plates.CraftsPerMinute, 0.0001f, "Three seconds is twenty crafts a minute...");
            Assert.AreEqual(80f, plates.OutputPerMinute, 0.0001f, "...and four plates each is 80/min.");
            Assert.AreEqual(20f, ingot.OutputPerMinute, 0.0001f, "One ingot every three seconds is 20/min.");
        }

        /// <summary>
        /// Derived from the recipe's own fields rather than authored beside them, so retuning a
        /// duration or a yield moves every figure on screen with it. The same rule
        /// ExtractorDefinition.ItemsPerMinute follows, and for the same reason: a maintained-by-hand
        /// rate is a number that eventually lies.
        /// </summary>
        [Test]
        public void OutputPerMinute_FollowsARetunedTimeOrYield()
        {
            RecipeDefinition faster = TestDataFactory.NewRecipe("iron_plate", timeSeconds: 1.5f, computeCost: 0f, outputAmount: 4, (Ore(), 1));
            RecipeDefinition richer = TestDataFactory.NewRecipe("iron_plate", timeSeconds: 3f, computeCost: 0f, outputAmount: 8, (Ore(), 1));

            Assert.AreEqual(160f, faster.OutputPerMinute, 0.0001f, "Half the time, twice the rate.");
            Assert.AreEqual(160f, richer.OutputPerMinute, 0.0001f, "Twice the yield, twice the rate.");
        }

        /// <summary>
        /// What an ingredient demands per minute is the same arithmetic read from the other side, and
        /// it is the figure that sizes the line feeding the building - the per-craft amount alone
        /// never could, since it says nothing about how often the craft happens.
        /// </summary>
        [Test]
        public void IngredientDemandPerMinute_IsThePerCraftAmountAtTheSameCadence()
        {
            RecipeDefinition recipe = TestDataFactory.NewRecipe("iron_plate", timeSeconds: 3f, computeCost: 0f, outputAmount: 4, (Ore(), 2));

            Assert.AreEqual(40f, recipe.Ingredients[0].Amount * recipe.CraftsPerMinute, 0.0001f,
                "Two ore per craft, twenty crafts a minute.");
        }

        /// <summary>A degenerate recipe reports no rate rather than dividing by zero - the Min(0.01f) on the field makes this unreachable through the inspector, and unreachable is not the same as guarded.</summary>
        [Test]
        public void AZeroLengthRecipe_ReportsNoRate_RatherThanInfinity()
        {
            RecipeDefinition instant = TestDataFactory.NewRecipe("iron_plate", timeSeconds: 0f, computeCost: 0f, outputAmount: 4);

            Assert.AreEqual(0f, instant.CraftsPerMinute, 0.0001f);
            Assert.AreEqual(0f, instant.OutputPerMinute, 0.0001f);
        }

        /// <summary>
        /// One spelling for every rate on screen. They are read as one kind of number across four
        /// different labels, which only works while they are punctuated and rounded the same way.
        ///
        /// The whole-number cases are asserted literally; the fractional one only for its shape. The
        /// separator is the running culture's, like every other number this UI prints, so pinning
        /// "8.6" here would pass on an English machine and fail on the French one the game is
        /// written for - and re-deriving it with the same format string would only assert that the
        /// formatter agrees with itself.
        /// </summary>
        [Test]
        public void PerMinute_ShowsADecimalOnlyWhenThereIsOne()
        {
            Assert.AreEqual("80/min", RateText.PerMinute(80f));
            Assert.AreEqual("20/min", RateText.PerMinute(20f));
            Assert.AreEqual("0/min", RateText.PerMinute(0f));

            string awkward = RateText.PerMinute(60f / 7f);
            Assert.AreNotEqual("9/min", awkward, "A rate that does not divide 60 must not be rounded to a whole number...");
            Assert.IsTrue(awkward.StartsWith("8") && awkward.EndsWith("6/min"),
                $"...but keeps exactly one decimal, not the full 8.571428: got \"{awkward}\".");
        }
    }
}

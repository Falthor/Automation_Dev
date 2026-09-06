using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    /// <summary>
    /// The extraction rate the Building menu quotes on hover, measured on an extractor actually
    /// running rather than re-derived from the same two fields the constant comes from.
    ///
    /// The belt taught this: a figure checked only against its own formula agrees with itself while
    /// both drift away from the simulation, and quoting it told the player a belt did four times what
    /// it did. Here the rate has to survive the CU charged per cycle and the buffer filling up, so
    /// the arithmetic being right is genuinely not enough.
    /// </summary>
    public class ExtractorThroughputTests
    {
        const float TickSeconds = 1f / 60f;

        static ExtractorDefinition NewExtractor(float intervalSeconds, int itemsPerCycle, float cuCostPerCycle)
        {
            var definition = ScriptableObject.CreateInstance<ExtractorDefinition>();
            var so = new SerializedObject(definition);
            so.FindProperty("id").stringValue = "extractor";
            so.FindProperty("extractionIntervalSeconds").floatValue = intervalSeconds;
            so.FindProperty("itemsPerCycle").intValue = itemsPerCycle;
            so.FindProperty("cuCostPerCycle").floatValue = cuCostPerCycle;
            so.FindProperty("powerDemandKw").floatValue = 0f;
            so.ApplyModifiedPropertiesWithoutUndo();
            return definition;
        }

        /// <summary>A deposit far deeper than a minute of extraction can exhaust, so what is measured is the extractor's rate and never the ore running out.</summary>
        static DepositRuntime NewDeposit(ItemDefinition item)
        {
            OreDepositDefinition definition = TestDataFactory.NewOreDeposit(item, Vector2Int.one);
            var deposit = new DepositRuntime(definition, new GridCoord(0, 0));
            deposit.RestoreState(100000);
            return deposit;
        }

        /// <summary>
        /// Runs an extractor for a minute with its output emptied every tick, so the buffer never
        /// fills and what is measured is the extraction rate itself rather than a downstream stall.
        /// </summary>
        static int ItemsExtractedInOneMinute(ExtractorDefinition definition)
        {
            var compute = new ComputeSystem();
            var power = new PowerSystem();
            ItemDefinition ore = TestDataFactory.NewItem("iron_ore");

            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East, NewDeposit(ore), compute, power);

            int extracted = 0;
            int ticks = Mathf.RoundToInt(60f / TickSeconds);

            for (int tick = 0; tick < ticks; tick++)
            {
                power.Settle();
                compute.Tick(TickSeconds);
                extractor.Tick(TickSeconds);

                object ready = extractor.PeekPullableItem();
                if (ready != null)
                {
                    extractor.ConsumePulledItem(ready);
                    extracted++;
                }
            }

            return extracted;
        }

        [Test]
        public void AnExtractor_PullsWhatTheMenuQuotes()
        {
            // The shipped asset's own numbers: one unit every four seconds.
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f);

            int measured = ItemsExtractedInOneMinute(definition);

            Assert.AreEqual(definition.ItemsPerMinute, measured, 1f,
                $"An extractor really pulls {measured}/min, and the Building menu says {definition.ItemsPerMinute:0.#}/min.");
        }

        [Test]
        public void AFasterExtractor_QuotesAndDeliversMore()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 1.5f, itemsPerCycle: 2, cuCostPerCycle: 2f);

            Assert.AreEqual(80f, definition.ItemsPerMinute, 0.0001f, "Two units every 1.5s is 80 a minute.");
            Assert.AreEqual(definition.ItemsPerMinute, ItemsExtractedInOneMinute(definition), 2f);
        }

        /// <summary>
        /// The quoted figure comes from the interval and the yield, so retuning either moves the menu
        /// with it instead of leaving a number nobody maintains.
        /// </summary>
        [Test]
        public void TheQuotedFigure_IsYieldOverInterval_NotAValueOfItsOwn()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f);

            Assert.AreEqual(definition.ItemsPerCycle / definition.ExtractionIntervalSeconds * 60f,
                definition.ItemsPerMinute, 0.0001f);
        }

        /// <summary>
        /// A rating, not a promise. Starve the CU and the extractor produces nothing - which is why
        /// the menu quotes what it can do rather than what it will do, and why this is measured with
        /// the compute reserve deliberately kept full.
        /// </summary>
        [Test]
        public void WithoutTheComputeToPayForACycle_ItProducesNothing()
        {
            var compute = new ComputeSystem();
            var power = new PowerSystem();
            ItemDefinition ore = TestDataFactory.NewItem("iron_ore");

            ExtractorDefinition definition = NewExtractor(intervalSeconds: 1f, itemsPerCycle: 1, cuCostPerCycle: 1000000f);
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East, NewDeposit(ore), compute, power);

            for (int tick = 0; tick < 60 * 10; tick++)
            {
                power.Settle();
                extractor.Tick(TickSeconds);
            }

            Assert.AreEqual(0, extractor.BufferedAmount, "A cycle nobody can pay for never completes.");
        }
    }
}

using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Compute;
using Game.Gameplay.Power;
using Game.Gameplay.Research;
using Game.Grid;
using Game.Tests.EditMode.TestSupport;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
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
            => TestDataFactory.NewExtractor(extractionIntervalSeconds: intervalSeconds, itemsPerCycle: itemsPerCycle, cuCostPerCycle: cuCostPerCycle);

        /// <summary>
        /// A plain deposit. Nothing is done to top it up: a deposit is meant never to run out
        /// (the depletion still in the code is a tracked removal, not a rule), and even the stock
        /// it currently carries outlasts the measured minute many times
        /// over. What is measured here is the extractor's rate, never the ore.
        /// </summary>
        static DepositRuntime NewDeposit(ItemDefinition item)
            => new DepositRuntime(TestDataFactory.NewOreDeposit(item, Vector2Int.one), new GridCoord(0, 0));

        /// <summary>
        /// Runs an extractor for a minute with its output emptied every tick, so the buffer never
        /// fills and what is measured is the extraction rate itself rather than a downstream stall.
        /// </summary>
        static int ItemsExtractedInOneMinute(ExtractorDefinition definition, ResearchSystem research = null)
        {
            var compute = new ComputeSystem();
            var power = new PowerSystem();
            research ??= new ResearchSystem(compute, compute);
            ItemDefinition ore = TestDataFactory.NewItem("iron_ore");

            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East, NewDeposit(ore), compute, power, research);

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

        // ---- ExtractorItemsPerMinute research effect ----

        /// <summary>A test research carrying one rate target. Named for what it does, never after a shipped research (extractor_power): the id is not what the effect hangs on.</summary>
        static ResearchDefinition RateTarget(float itemsPerMinute)
            => TestDataFactory.WithEffects(TestDataFactory.NewResearch("rate_" + itemsPerMinute, 10f),
                new ResearchEffect(ResearchEffectKind.ExtractorItemsPerMinute, value: (int)itemsPerMinute));

        [Test]
        public void Constructor_StartsAtTheDefinitionsRate()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f); // 15/min
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East,
                NewDeposit(TestDataFactory.NewItem("iron_ore")), new ComputeSystem(), new PowerSystem(),
                new ResearchSystem(new ComputeSystem(), new ComputeSystem()));

            Assert.AreEqual(15f, extractor.ItemsPerMinute, 0.0001f);
        }

        /// <summary>The shipped extractor_power research: 15/min doubled to 30/min once completed, actually delivered rather than just quoted.</summary>
        [Test]
        public void ARateEffect_GrowsTheRateToItsTarget_AndTheExtraCargoIsActuallyDelivered()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f); // 15/min
            ResearchDefinition rate30 = RateTarget(30f);
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { rate30 }));
            var deposit = NewDeposit(TestDataFactory.NewItem("iron_ore"));
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East, deposit,
                new ComputeSystem(), new PowerSystem(), research);

            research.Grant(rate30.Id);

            Assert.AreEqual(30f, extractor.ItemsPerMinute, 0.0001f);
            Assert.AreEqual(30f, ItemsExtractedInOneMinute(definition, research), 2f);
        }

        /// <summary>Each research sets its own target and the highest completed wins, matching CoreRuntime.ActionRadiusCells.</summary>
        [Test]
        public void RateTargets_TheHighestReachedWins_WhateverTheOrder()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f);
            ResearchDefinition rate30 = RateTarget(30f), rate45 = RateTarget(45f);
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { rate30, rate45 }));
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East,
                NewDeposit(TestDataFactory.NewItem("iron_ore")), new ComputeSystem(), new PowerSystem(), research);

            research.Grant(rate45.Id);
            Assert.AreEqual(45f, extractor.ItemsPerMinute, 0.0001f);

            research.Grant(rate30.Id);
            Assert.AreEqual(45f, extractor.ItemsPerMinute, 0.0001f, "A lower target after a higher one changes nothing.");
        }

        [Test]
        public void CaptureAndRestore_RoundTripsTheResearchedRate()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f);
            ResearchDefinition rate30 = RateTarget(30f);
            var research = new ResearchSystem(new ComputeSystem(), new ComputeSystem(), new ResearchCatalog(new[] { rate30 }));
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East,
                NewDeposit(TestDataFactory.NewItem("iron_ore")), new ComputeSystem(), new PowerSystem(), research);
            research.Grant(rate30.Id);

            var restored = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East,
                NewDeposit(TestDataFactory.NewItem("iron_ore")), new ComputeSystem(), new PowerSystem(),
                new ResearchSystem(new ComputeSystem(), new ComputeSystem()));
            restored.RestoreState(extractor.CaptureState());

            Assert.AreEqual(30f, restored.ItemsPerMinute, 0.0001f);
        }

        [Test]
        public void RestoreState_ToleratesABlobMissingTheResearchedRate_FallsBackToTheDefinitionsRate()
        {
            ExtractorDefinition definition = NewExtractor(intervalSeconds: 4f, itemsPerCycle: 1, cuCostPerCycle: 2f);
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East,
                NewDeposit(TestDataFactory.NewItem("iron_ore")), new ComputeSystem(), new PowerSystem(),
                new ResearchSystem(new ComputeSystem(), new ComputeSystem()));

            extractor.RestoreState(new JObject());

            Assert.AreEqual(15f, extractor.ItemsPerMinute, 0.0001f);
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
            var research = new ResearchSystem(compute, compute);
            ItemDefinition ore = TestDataFactory.NewItem("iron_ore");

            ExtractorDefinition definition = NewExtractor(intervalSeconds: 1f, itemsPerCycle: 1, cuCostPerCycle: 1000000f);
            var extractor = new ExtractorRuntime(definition, new GridCoord(0, 0), Direction.East, NewDeposit(ore), compute, power, research);

            for (int tick = 0; tick < 60 * 10; tick++)
            {
                power.Settle();
                extractor.Tick(TickSeconds);
            }

            Assert.AreEqual(0, extractor.BufferedAmount, "A cycle nobody can pay for never completes.");
        }
    }
}

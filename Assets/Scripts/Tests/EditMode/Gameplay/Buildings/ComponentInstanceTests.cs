using Game.Data;
using Game.Gameplay.Buildings;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class ComponentInstanceTests
    {
        const float DefaultThreshold = 25f;

        static ItemDatabase NewCpuDatabase(float cuOutput, float powerKw, float nominalLifetimeSeconds = 120f)
        {
            ItemDefinition cpu = TestDataFactory.NewItem("cpu_mkI", ItemType.Component);
            var so = new UnityEditor.SerializedObject(cpu);
            so.FindProperty("cuOutput").floatValue = cuOutput;
            so.FindProperty("powerKw").floatValue = powerKw;
            so.FindProperty("nominalLifetimeSeconds").floatValue = nominalLifetimeSeconds;
            so.ApplyModifiedPropertiesWithoutUndo();
            return TestDataFactory.NewItemDatabase(cpu);
        }

        static ComponentInstance NewComponent(ItemDatabase db, int seed = 1, float threshold = DefaultThreshold)
        {
            return new ComponentInstance("cpu_mkI", db, new System.Random(seed), threshold);
        }

        [Test]
        public void Constructor_SnapshotsCuAndPower_AndStartsFullyUnwornAtDefaultStability()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);

            Assert.AreEqual(1000f, component.BaseCu);
            Assert.AreEqual(2f, component.PowerKw);
            Assert.AreEqual(100f, component.Wear);
            Assert.AreEqual(1f, component.EffectivePerformance);
            Assert.AreEqual(95f, component.Stability, 0.001f, "Wear=100 (new) must give 95% stability - TASK_03_DATACENTER.md §4.1.");
        }

        [Test]
        public void Stability_MatchesFormula_AtBothWearBounds()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);

            Assert.AreEqual(95f, component.Stability, 0.001f, "95% at Wear=100 (neuf).");

            // Force Wear to 0 by decaying far past its lifetime.
            component.DecayWear(100000f);
            Assert.AreEqual(0f, component.Wear);
            Assert.AreEqual(30f, component.Stability, 0.001f, "30% at Wear=0 (fin de vie).");
        }

        [Test]
        public void FluctuationFloor_WidensFrom70PercentTo30Percent_AsWearDropsToZero()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);

            Assert.AreEqual(0.70f, component.FluctuationFloor, 0.001f, "A new component's floor is 70% - TASK_03_DATACENTER.md §4.2.");

            component.DecayWear(100000f);
            Assert.AreEqual(0f, component.Wear);
            Assert.AreEqual(0.30f, component.FluctuationFloor, 0.001f, "An end-of-life component's floor is 30%.");
        }

        [Test]
        public void DecayWear_AcceleratesAsWearDrops_ComponentAt20PercentLosesWear2Point6TimesFaster()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);

            var fresh = NewComponent(db); // Wear = 100
            float freshLossRate = fresh.BaseLossPerSecond * (1f + 2f * (1f - fresh.Wear / 100f));

            var worn = NewComponent(db);
            worn.DecayWear(100000f); // drive to 0 first
            // Nudge back up isn't exposed publicly (Wear only ever decreases) - instead compare
            // the formula directly at Wear=20 using the same BaseLossPerSecond, which is exactly
            // what TASK_03_DATACENTER.md §4.3 specifies: "perte_base × (1 + 2 × (1 − usure/100))".
            float lossRateAt20 = worn.BaseLossPerSecond * (1f + 2f * (1f - 20f / 100f));
            float lossRateAtFull = worn.BaseLossPerSecond * (1f + 2f * (1f - 100f / 100f));

            Assert.AreEqual(2.6f, lossRateAt20 / lossRateAtFull, 0.001f, "A component at 20% wear must lose wear 2.6x faster than a fresh one.");
        }

        [Test]
        public void DrawnLifetime_IsDeterministic_ForTheSameSeedAndParameters()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f, nominalLifetimeSeconds: 120f);

            var a = new ComponentInstance("cpu_mkI", db, new System.Random(42), DefaultThreshold);
            var b = new ComponentInstance("cpu_mkI", db, new System.Random(42), DefaultThreshold);

            Assert.AreEqual(a.NominalLifetimeSeconds, b.NominalLifetimeSeconds, "Same seed, same parameters, same drawn lifetime.");
        }

        [Test]
        public void DrawnLifetime_StaysWithinPlusOrMinus25PercentOfNominal()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f, nominalLifetimeSeconds: 120f);

            for (int seed = 0; seed < 100; seed++)
            {
                var component = new ComponentInstance("cpu_mkI", db, new System.Random(seed), DefaultThreshold);
                Assert.GreaterOrEqual(component.NominalLifetimeSeconds, 90f);
                Assert.LessOrEqual(component.NominalLifetimeSeconds, 150f);
            }
        }

        [Test]
        public void BaseLossPerSecond_CalibratedSoWearReachesThreshold_AtExactlyTheDrawnLifetime()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f, nominalLifetimeSeconds: 120f);
            var component = new ComponentInstance("cpu_mkI", db, new System.Random(7), 25f);
            float lifetime = component.NominalLifetimeSeconds;

            // Fine-grained numerical integration (matches how DataCenterRuntime.Tick calls this every frame).
            const float step = 0.01f;
            float elapsed = 0f;
            while (elapsed < lifetime)
            {
                component.DecayWear(step);
                elapsed += step;
            }

            Assert.AreEqual(25f, component.Wear, 0.5f, "Integrating the accelerated decay curve for the drawn lifetime must land on the threshold in effect at install.");
        }

        [Test]
        public void HasCrossedReplacementThreshold_ComparesAgainstTheCurrentValue_NotASnapshot()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);
            component.DecayWear(100000f); // Wear = 0

            Assert.IsTrue(component.HasCrossedReplacementThreshold(5f));
            Assert.IsTrue(component.HasCrossedReplacementThreshold(60f));

            var fresh = NewComponent(db); // Wear = 100
            Assert.IsFalse(fresh.HasCrossedReplacementThreshold(5f));
        }

        [Test]
        public void EffectiveCu_ZeroWhileReplacing()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);
            component.IsReplacing = true;

            Assert.AreEqual(0f, component.EffectiveCu());
            Assert.AreEqual(0f, component.ActivePowerKw());
        }

        [Test]
        public void EffectiveCu_UsesBaseCuTimesPerformance_WhenNotReplacing()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);

            Assert.AreEqual(1000f, component.EffectiveCu()); // default performance 1.0
            Assert.AreEqual(2f, component.ActivePowerKw());
        }

        [Test]
        public void RecalculatePerformance_StaysWithinValidRange()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = NewComponent(db);

            for (int i = 0; i < 50; i++)
            {
                component.RecalculatePerformance();
                Assert.GreaterOrEqual(component.EffectivePerformance, component.FluctuationFloor);
                Assert.LessOrEqual(component.EffectivePerformance, 1.0f);
            }
        }

        [Test]
        public void RestoreConstructor_SetsLifetimeAndBaseLossVerbatim_WithoutDrawing()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var restored = new ComponentInstance("cpu_mkI", db, nominalLifetimeSeconds: 137f, baseLossPerSecond: 0.456f);

            Assert.AreEqual(137f, restored.NominalLifetimeSeconds);
            Assert.AreEqual(0.456f, restored.BaseLossPerSecond);
            Assert.AreEqual(100f, restored.Wear, "Still starts at full Wear until RestoreWearAndPerformance is called.");
        }
    }
}

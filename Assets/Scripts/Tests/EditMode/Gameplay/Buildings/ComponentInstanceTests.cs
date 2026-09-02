using Game.Data;
using Game.Gameplay.Buildings;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Buildings
{
    public class ComponentInstanceTests
    {
        static ItemDatabase NewCpuDatabase(float cuOutput, float powerKw)
        {
            ItemDefinition cpu = TestDataFactory.NewItem("cpu_mkI", ItemType.Component);
            var so = new UnityEditor.SerializedObject(cpu);
            so.FindProperty("cuOutput").floatValue = cuOutput;
            so.FindProperty("powerKw").floatValue = powerKw;
            so.ApplyModifiedPropertiesWithoutUndo();
            return TestDataFactory.NewItemDatabase(cpu);
        }

        [Test]
        public void Constructor_SnapshotsCuAndPowerFromItemDatabase()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = new ComponentInstance("cpu_mkI", db);

            Assert.AreEqual(1000f, component.BaseCu);
            Assert.AreEqual(2f, component.PowerKw);
            Assert.AreEqual(80f, component.Stability);
            Assert.AreEqual(100f, component.Wear);
            Assert.AreEqual(1f, component.EffectivePerformance);
        }

        [Test]
        public void DecayWear_FloorsAtZero()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = new ComponentInstance("cpu_mkI", db);

            for (int i = 0; i < 200; i++) component.DecayWear();

            Assert.AreEqual(0f, component.Wear);
        }

        [Test]
        public void HasCrossedReplacementThreshold_TrueAtOrBelow5Percent()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = new ComponentInstance("cpu_mkI", db);

            for (int i = 0; i < 95; i++) component.DecayWear(); // wear = 5

            Assert.IsTrue(component.HasCrossedReplacementThreshold);
        }

        [Test]
        public void EffectiveCu_ZeroWhileReplacing()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = new ComponentInstance("cpu_mkI", db) { IsReplacing = true };

            Assert.AreEqual(0f, component.EffectiveCu());
            Assert.AreEqual(0f, component.ActivePowerKw());
        }

        [Test]
        public void EffectiveCu_UsesBaseCuTimesPerformance_WhenNotReplacing()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = new ComponentInstance("cpu_mkI", db);

            Assert.AreEqual(1000f, component.EffectiveCu()); // default performance 1.0
            Assert.AreEqual(2f, component.ActivePowerKw());
        }

        [Test]
        public void RecalculatePerformance_StaysWithinValidRange()
        {
            ItemDatabase db = NewCpuDatabase(1000f, 2f);
            var component = new ComponentInstance("cpu_mkI", db);

            for (int i = 0; i < 50; i++)
            {
                component.RecalculatePerformance();
                Assert.GreaterOrEqual(component.EffectivePerformance, 0.70f);
                Assert.LessOrEqual(component.EffectivePerformance, 1.0f);
            }
        }
    }
}

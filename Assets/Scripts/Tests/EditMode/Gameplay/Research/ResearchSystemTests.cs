using Game.Data;
using Game.Gameplay.Compute;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Research
{
    public class ResearchSystemTests
    {
        [Test]
        public void Tick_ProgressesAtMinOfAbsorptionAndAvailable_NeverMore()
        {
            var compute = new ComputeSystem(); // full reserve, far more than the absorption ceiling could ever draw in one tick
            var research = new ResearchSystem(compute);
            ResearchDefinition def = TestDataFactory.NewResearch("test", cuCost: 1000f, absorptionRatePerSecond: 40f);
            research.Enqueue(def); // starts synchronously - nothing else was active

            research.Tick(1f); // 1s at the 40 CU/s ceiling, reserve has plenty more available
            Assert.AreEqual(40f, research.GetProgress() * def.CuCost, 0.001f);
            Assert.AreEqual(ComputeSystem.ReserveCap - 40f, compute.Reserve);

            research.Tick(1f); // a second second absorbs another 40, never more than the ceiling
            Assert.AreEqual(80f, research.GetProgress() * def.CuCost, 0.001f);
            Assert.AreEqual(ComputeSystem.ReserveCap - 80f, compute.Reserve);
        }

        [Test]
        public void Tick_AtZeroCu_PausesAndPreservesProgressExactly()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition def = TestDataFactory.NewResearch("test", cuCost: 1000f, absorptionRatePerSecond: 100f);
            research.Enqueue(def); // starts synchronously - nothing else was active

            research.Tick(1f); // absorbs 100 CU
            float absorbedBeforeStarvation = research.GetProgress() * def.CuCost;
            Assert.AreEqual(100f, absorbedBeforeStarvation, 0.001f);

            compute.Spend(compute.Reserve); // reserve now exactly 0 - starves the research
            research.Tick(5f); // would have absorbed 500 more CU if the reserve had it

            Assert.IsTrue(research.HasActiveResearch(), "A starved research must stay active, not fail or reset.");
            Assert.AreEqual(absorbedBeforeStarvation, research.GetProgress() * def.CuCost, 0.001f, "Progress must not move while the reserve is at zero.");
        }

        [Test]
        public void Tick_ResumesExactlyWhereItLeftOff_OnceCuIsAvailableAgain()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition def = TestDataFactory.NewResearch("test", cuCost: 1000f, absorptionRatePerSecond: 100f);
            research.Enqueue(def); // starts synchronously - nothing else was active

            research.Tick(1f); // absorbs 100 CU
            compute.Spend(compute.Reserve); // starve it
            research.Tick(5f); // paused, no progress lost

            compute.Grant(1000f); // CU flows back in
            research.Tick(1f); // resumes: 100 more CU absorbed, on top of the exact same 100 already banked

            Assert.AreEqual(200f, research.GetProgress() * def.CuCost, 0.001f);
        }

        [Test]
        public void ArePrerequisitesMet_RequiresEveryPrerequisite_NotJustOne()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition prereqA = TestDataFactory.NewResearch("a", 10f);
            ResearchDefinition prereqB = TestDataFactory.NewResearch("b", 10f);
            ResearchDefinition combined = TestDataFactory.NewResearch("combined", 10f, prerequisites: new[] { prereqA, prereqB });

            Assert.IsFalse(research.ArePrerequisitesMet(combined));

            research.Enqueue(prereqA);
            research.Tick(1f);
            Assert.IsTrue(research.IsUnlocked("a"));
            Assert.IsFalse(research.ArePrerequisitesMet(combined), "Only one of two prerequisites is done.");

            research.Enqueue(prereqB);
            research.Tick(1f);
            Assert.IsTrue(research.IsUnlocked("b"));
            Assert.IsTrue(research.ArePrerequisitesMet(combined));
        }

        [Test]
        public void Enqueue_Fails_WhenAPrerequisiteIsMissing()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition prereq = TestDataFactory.NewResearch("prereq", 10f);
            ResearchDefinition gated = TestDataFactory.NewResearch("gated", 10f, prerequisites: new[] { prereq });

            Assert.IsFalse(research.Enqueue(gated));
            Assert.IsFalse(research.HasActiveResearch());
        }

        [Test]
        public void Enqueue_Fails_WhenAlreadyUnlocked()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Enqueue(def);
            research.Tick(1f);
            Assert.IsTrue(research.IsUnlocked("test"));

            Assert.IsFalse(research.Enqueue(def));
        }

        [Test]
        public void Enqueue_OnlyOneResearchActiveAtATime_SecondGoesToQueue()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition first = TestDataFactory.NewResearch("first", 1000f, absorptionRatePerSecond: 1f);
            ResearchDefinition second = TestDataFactory.NewResearch("second", 10f);

            Assert.IsTrue(research.Enqueue(first));
            Assert.AreSame(first, research.GetActiveResearch());

            Assert.IsTrue(research.Enqueue(second));
            Assert.AreSame(first, research.GetActiveResearch(), "Enqueuing a second research must not preempt the active one.");
            CollectionAssert.Contains(research.GetQueue(), second);
        }

        [Test]
        public void Tick_QueueChainsInOrder_OnceTheActiveResearchCompletes()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition first = TestDataFactory.NewResearch("first", 10f);
            ResearchDefinition second = TestDataFactory.NewResearch("second", 10f);
            research.Enqueue(first);
            research.Enqueue(second);

            research.Tick(1f); // completes "first" (huge default absorption rate)
            Assert.IsTrue(research.IsUnlocked("first"));
            Assert.IsFalse(research.IsUnlocked("second"));
            Assert.IsNull(research.GetActiveResearch(), "The next queued research starts on the following tick, not the same one.");

            research.Tick(1f); // pulls "second" off the queue and starts it
            Assert.AreSame(second, research.GetActiveResearch());

            research.Tick(1f); // completes "second"
            Assert.IsTrue(research.IsUnlocked("second"));
        }

        [Test]
        public void ResearchCompleted_FiresExactlyOnce_WithTheCompletedId()
        {
            var compute = new ComputeSystem();
            var research = new ResearchSystem(compute);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Enqueue(def);

            int fireCount = 0;
            string completedId = null;
            research.ResearchCompleted += id =>
            {
                fireCount++;
                completedId = id;
            };

            research.Tick(1f); // completes immediately - default absorption ceiling dwarfs cuCost

            Assert.AreEqual(1, fireCount);
            Assert.AreEqual("test", completedId);

            research.Tick(1f); // nothing left active or queued - must not fire again
            Assert.AreEqual(1, fireCount);
        }
    }
}

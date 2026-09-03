using Game.Data;
using Game.Gameplay.Research;
using Game.Tests.EditMode.TestSupport;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Research
{
    public class ResearchSystemTests
    {
        [Test]
        public void Start_Fails_WhenInsufficientRp()
        {
            var research = new ResearchSystem();
            ResearchDefinition def = TestDataFactory.NewResearch("test", 50f);

            Assert.IsFalse(research.Start(def));
            Assert.IsFalse(research.HasActiveResearch());
        }

        [Test]
        public void Start_Succeeds_DeductsCost()
        {
            var research = new ResearchSystem();
            research.AddRp(50f);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 50f);

            Assert.IsTrue(research.Start(def));
            Assert.AreEqual(0f, research.Rp);
            Assert.AreSame(def, research.GetActiveResearch());
        }

        [Test]
        public void Start_Fails_WhenAlreadyActive()
        {
            var research = new ResearchSystem();
            research.AddRp(100f);
            ResearchDefinition first = TestDataFactory.NewResearch("first", 50f);
            ResearchDefinition second = TestDataFactory.NewResearch("second", 50f);
            research.Start(first);

            Assert.IsFalse(research.Start(second));
        }

        [Test]
        public void Start_Fails_WhenAlreadyUnlocked()
        {
            var research = new ResearchSystem();
            research.AddRp(1000f);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Start(def);
            research.ReportActiveLab();
            research.Tick(60f);
            Assert.IsTrue(research.IsUnlocked("test"));

            Assert.IsFalse(research.Start(def));
        }

        [Test]
        public void Tick_OneActiveLab_CompletesAfter60Seconds()
        {
            var research = new ResearchSystem();
            research.AddRp(10f);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Start(def);

            research.ReportActiveLab();
            research.Tick(59f);
            Assert.IsFalse(research.IsUnlocked("test"));

            research.ReportActiveLab();
            research.Tick(1f);
            Assert.IsTrue(research.IsUnlocked("test"));
            Assert.IsFalse(research.HasActiveResearch());
        }

        [Test]
        public void Tick_TwoActiveLabs_CompletesTwiceAsFast()
        {
            var research = new ResearchSystem();
            research.AddRp(10f);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Start(def);

            research.ReportActiveLab();
            research.ReportActiveLab();
            research.Tick(29f);
            Assert.IsFalse(research.IsUnlocked("test"));

            research.ReportActiveLab();
            research.ReportActiveLab();
            research.Tick(1f);
            Assert.IsTrue(research.IsUnlocked("test"));
        }

        [Test]
        public void ReportActiveLab_IsOneFrameLagged_LikePowerAndCompute()
        {
            var research = new ResearchSystem();
            research.AddRp(10f);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Start(def);

            research.ReportActiveLab();
            Assert.AreEqual(0, research.GetActiveLabCount()); // not settled yet

            research.Tick(0f);
            Assert.AreEqual(1, research.GetActiveLabCount());
        }

        [Test]
        public void ResearchCompleted_EventFires_WithCompletedId()
        {
            var research = new ResearchSystem();
            research.AddRp(10f);
            ResearchDefinition def = TestDataFactory.NewResearch("test", 10f);
            research.Start(def);

            string completedId = null;
            research.ResearchCompleted += id => completedId = id;

            research.ReportActiveLab();
            research.Tick(60f);

            Assert.AreEqual("test", completedId);
        }
    }
}

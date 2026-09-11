using System.Collections.Generic;
using Game.Gameplay.Power;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Power
{
    /// <summary>
    /// The one thing about the priority order worth pinning: what a saved order does when the game
    /// it was saved from is no longer the game being loaded.
    ///
    /// An order of identifiers is what makes the list extensible without a migration, and these are
    /// the three rules that claim holds on. Positions would need none of this and would break on the
    /// first type anybody added.
    /// </summary>
    public class PowerPriorityOrderTests
    {
        /// <summary>
        /// A save from before a type existed, loaded into a game that also lost one: the arrangement
        /// the player made survives, the newcomer lands at the bottom, and the departed is ignored.
        /// </summary>
        [Test]
        public void RestoringASavedOrder_KeepsWhatItKnows_AppendsWhatIsNew_AndDropsWhatIsGone()
        {
            var order = new PowerPriorityOrder();

            // The player had arranged four types. "assembler" has since been removed from the game,
            // and "datacenter" has since been added to it.
            var saved = new List<string> { "factory", "assembler", "extractor", "foundry" };
            var known = new List<string> { "extractor", "foundry", "factory", "datacenter" };

            order.RestoreState(saved, known);

            CollectionAssert.AreEqual(
                new[] { "factory", "extractor", "foundry", "datacenter" },
                order.Order,
                "The saved order holds for everything still in the game, in the order it was saved - "
                + "the removed type simply is not there, and the new one is last rather than in the middle.");
        }

        [Test]
        public void ATypeAddedSinceTheSave_GoesToTheBottom_RatherThanDisplacingAnything()
        {
            var order = new PowerPriorityOrder();
            order.RestoreState(new List<string> { "foundry", "extractor" },
                new List<string> { "extractor", "foundry", "brand_new" });

            Assert.AreEqual(0, order.RankOf("foundry"), "What the player put first stays first.");
            Assert.AreEqual(1, order.RankOf("extractor"));
            Assert.AreEqual(2, order.RankOf("brand_new"), "And the newcomer is behind both.");
        }

        [Test]
        public void ATypeInTheSaveThatNoLongerExists_IsIgnoredSilently()
        {
            var order = new PowerPriorityOrder();
            order.RestoreState(new List<string> { "gone", "foundry" }, new List<string> { "foundry" });

            CollectionAssert.AreEqual(new[] { "foundry" }, order.Order);
            Assert.AreEqual(int.MaxValue, order.RankOf("gone"), "It is not known, so it ranks after everything.");
        }

        /// <summary>No save at all - a fresh run, or one from before this was persisted - is the catalogue's own order.</summary>
        [Test]
        public void WithNoSavedOrder_TheCatalogueOrderIsTheDefault()
        {
            var order = new PowerPriorityOrder();
            order.RestoreState(null, new List<string> { "extractor", "foundry", "factory" });

            CollectionAssert.AreEqual(new[] { "extractor", "foundry", "factory" }, order.Order);
        }

        [Test]
        public void MoveTo_SlidesTheRestAlong_AndACaptureRoundTripReproducesIt()
        {
            var order = new PowerPriorityOrder();
            order.EnsureKnows(new[] { "extractor", "foundry", "factory", "datacenter" });

            order.MoveTo("datacenter", 0);

            CollectionAssert.AreEqual(new[] { "datacenter", "extractor", "foundry", "factory" }, order.Order);

            var restored = new PowerPriorityOrder();
            restored.RestoreState(order.CaptureState(), new[] { "extractor", "foundry", "factory", "datacenter" });

            CollectionAssert.AreEqual(order.Order, restored.Order, "What is saved is exactly what comes back.");
        }

        /// <summary>A duplicate in a hand-edited or older blob is not a reason to refuse the whole order.</summary>
        [Test]
        public void ADuplicateInTheSavedOrder_IsKeptOnce()
        {
            var order = new PowerPriorityOrder();
            order.RestoreState(new List<string> { "foundry", "foundry", "extractor" },
                new List<string> { "extractor", "foundry" });

            CollectionAssert.AreEqual(new[] { "foundry", "extractor" }, order.Order);
        }
    }
}

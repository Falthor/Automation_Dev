using Game.Core;
using Game.Grid;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Grid
{
    /// <summary>
    /// The third state: observed against remembered.
    ///
    /// <b>What these are really guarding is that observation is never stored.</b> The whole design
    /// rests on it being recomputed from where the observers are, so the failures worth catching are
    /// the ones a stored field would cause: a cell that stays lit after everything walks away, a cell
    /// that never lights again, discovery quietly lost when observation drops, and observation
    /// leaking into the save. None of the four throws - they are all just a wrong picture.
    /// </summary>
    public class ObservationRuntimeTests
    {
        const int MapSize = 64;
        const int ChunkSize = 64;

        static DiscoveryRuntime NewDiscovery() => new DiscoveryRuntime(MapSize, ChunkSize);

        /// <summary>An observer standing on a cell, with a radius wide enough to cover it and its neighbours.</summary>
        static void Observe(ObservationRuntime observation, Vector2 centreCells, float radiusCells = 3f)
        {
            observation.BeginRebuild();
            observation.Add(centreCells, radiusCells);
            observation.EndRebuild();
        }

        static void ObserveNothing(ObservationRuntime observation)
        {
            observation.BeginRebuild();
            observation.EndRebuild();
        }

        // ---- The three states ----

        [Test]
        public void ACellNobodyHasSeenIsUnknown_EvenWithAnObserverOnIt()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var cell = new GridCoord(10, 10);

            Observe(observation, new Vector2(10.5f, 10.5f));

            Assert.IsTrue(observation.IsObserved(cell), "The disc does cover it.");
            Assert.AreEqual(DiscoveryState.Unknown, observation.StateOf(cell, discovery),
                "Never discovered stays unknown: discovery gates observation, not the other way round.");
        }

        [Test]
        public void ADiscoveredCellUnderAnObserverIsObserved()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var cell = new GridCoord(10, 10);

            discovery.Reveal(cell);
            Observe(observation, new Vector2(10.5f, 10.5f));

            Assert.AreEqual(DiscoveryState.Observed, observation.StateOf(cell, discovery));
        }

        [Test]
        public void ADiscoveredCellWithNothingWatchingIsRemembered()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var cell = new GridCoord(10, 10);

            discovery.Reveal(cell);
            ObserveNothing(observation);

            Assert.AreEqual(DiscoveryState.Remembered, observation.StateOf(cell, discovery));
        }

        // ---- Leaving and coming back ----

        /// <summary>The headline behaviour: what the player knew stays known, and stops being live.</summary>
        [Test]
        public void ACellLeftByEveryObserver_TurnsFromObservedToRemembered_AndStaysDiscovered()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var cell = new GridCoord(20, 20);

            // A robot walks over it, revealing as it goes.
            discovery.RevealDisc(new Vector2(20.5f, 20.5f), 3f);
            Observe(observation, new Vector2(20.5f, 20.5f));
            Assert.AreEqual(DiscoveryState.Observed, observation.StateOf(cell, discovery));

            // ...and keeps walking, far enough that its disc no longer reaches.
            Observe(observation, new Vector2(60.5f, 60.5f));

            Assert.AreEqual(DiscoveryState.Remembered, observation.StateOf(cell, discovery),
                "Out of every radius, it freezes on what was known.");
            Assert.IsTrue(discovery.IsDiscovered(cell),
                "And it is still discovered - nothing was erased, the observer simply left.");
        }

        [Test]
        public void ACellBecomesObservedAgain_WhenARobotComesBack()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var cell = new GridCoord(20, 20);

            discovery.Reveal(cell);

            Observe(observation, new Vector2(20.5f, 20.5f));
            Assert.AreEqual(DiscoveryState.Observed, observation.StateOf(cell, discovery));

            Observe(observation, new Vector2(60.5f, 60.5f));
            Assert.AreEqual(DiscoveryState.Remembered, observation.StateOf(cell, discovery));

            Observe(observation, new Vector2(20.5f, 20.5f));
            Assert.AreEqual(DiscoveryState.Observed, observation.StateOf(cell, discovery),
                "Nothing had to be un-erased: the cell is simply covered again.");
        }

        [Test]
        public void SeveralObserversOverlap_AndOneLeavingDoesNotUnobserveWhatTheOtherStillCovers()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var cell = new GridCoord(20, 20);

            discovery.Reveal(cell);

            observation.BeginRebuild();
            observation.Add(new Vector2(20.5f, 20.5f), 3f);
            observation.Add(new Vector2(22.5f, 20.5f), 3f);
            observation.EndRebuild();
            Assert.AreEqual(DiscoveryState.Observed, observation.StateOf(cell, discovery));

            // The first one goes; the second still reaches.
            Observe(observation, new Vector2(22.5f, 20.5f));
            Assert.AreEqual(DiscoveryState.Observed, observation.StateOf(cell, discovery));
        }

        // ---- The disc's edge ----

        /// <summary>Measured on the cell's centre, which is the rule RevealDisc uses - so what a robot sees and what it uncovers have one edge rather than two a half-cell apart.</summary>
        [Test]
        public void TheObservationDiscHasTheSameEdgeAsARevealedDisc()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();
            var centre = new Vector2(32f, 32f);

            discovery.RevealDisc(centre, 5f);
            Observe(observation, centre, 5f);

            for (int y = 25; y < 40; y++)
            {
                for (int x = 25; x < 40; x++)
                {
                    var cell = new GridCoord(x, y);
                    Assert.AreEqual(discovery.IsDiscovered(cell), observation.IsObserved(cell),
                        $"cell ({x},{y}) - the two discs disagree about their own edge.");
                }
            }
        }

        [Test]
        public void AnObserverWithNoRadiusSeesNothing()
        {
            var observation = new ObservationRuntime();

            observation.BeginRebuild();
            observation.Add(new Vector2(10.5f, 10.5f), 0f);
            observation.EndRebuild();

            Assert.AreEqual(0, observation.ObserverCount, "A disc that can contain nothing is not kept.");
            Assert.IsFalse(observation.IsObserved(new GridCoord(10, 10)));
        }

        // ---- The version, which is what the fog re-uploads on ----

        [Test]
        public void RebuildingTheSameSet_DoesNotMoveTheVersion()
        {
            var observation = new ObservationRuntime();

            Observe(observation, new Vector2(10.5f, 10.5f));
            int version = observation.Version;

            Observe(observation, new Vector2(10.5f, 10.5f));

            Assert.AreEqual(version, observation.Version,
                "A still fleet must not make the fog re-upload sixty times a second.");
        }

        [Test]
        public void AnObserverMoving_MovesTheVersion()
        {
            var observation = new ObservationRuntime();

            Observe(observation, new Vector2(10.5f, 10.5f));
            int version = observation.Version;

            Observe(observation, new Vector2(10.6f, 10.5f));

            Assert.AreNotEqual(version, observation.Version, "A tenth of a cell is still a move.");
        }

        [Test]
        public void AnObserverAppearingOrLeaving_MovesTheVersion()
        {
            var observation = new ObservationRuntime();

            ObserveNothing(observation);
            int empty = observation.Version;

            Observe(observation, new Vector2(10.5f, 10.5f));
            Assert.AreNotEqual(empty, observation.Version, "One appeared.");

            int one = observation.Version;
            ObserveNothing(observation);
            Assert.AreNotEqual(one, observation.Version, "And it left again.");
        }

        [Test]
        public void AddingOutsideARebuild_IsIgnoredRatherThanCorruptingTheSet()
        {
            var observation = new ObservationRuntime();

            observation.Add(new Vector2(10.5f, 10.5f), 3f);

            Assert.AreEqual(0, observation.ObserverCount);
            Assert.IsFalse(observation.IsObserved(new GridCoord(10, 10)));
        }

        [Test]
        public void ANullDiscovery_AnswersUnknownRatherThanThrowing()
        {
            var observation = new ObservationRuntime();
            Observe(observation, new Vector2(10.5f, 10.5f));

            Assert.AreEqual(DiscoveryState.Unknown, observation.StateOf(new GridCoord(10, 10), null));
        }

        // ---- Nothing of this is saved ----

        /// <summary>
        /// The observation field cannot reach the save, and this is the assertion that would break if
        /// it ever did: what discovery captures is byte-identical whether anything is watching or not.
        ///
        /// The other half is pinned elsewhere - SaveFormatTests holds the save file's exact key set,
        /// so an observation field appearing at the top level fails there. Between the two there is
        /// nowhere for it to hide.
        /// </summary>
        [Test]
        public void ObservationNeverReachesTheSave()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            var observation = new ObservationRuntime();

            discovery.RevealDisc(new Vector2(32f, 32f), 6f);
            string withoutObservers = discovery.CaptureState();

            observation.BeginRebuild();
            observation.Add(new Vector2(32f, 32f), 6f);
            observation.Add(new Vector2(10.5f, 10.5f), 3f);
            observation.EndRebuild();

            Assert.AreEqual(withoutObservers, discovery.CaptureState(),
                "What is captured is what was discovered, and observers are not part of it.");
        }

        /// <summary>
        /// A reloaded world has no observation to restore and does not need any: the first rebuild
        /// re-derives the whole field from the observers the load put back. Stated as a test because
        /// "there is nothing to restore" is exactly the kind of claim that quietly stops being true.
        /// </summary>
        [Test]
        public void AFreshFieldObservesNothing_SoARestoredWorldStartsAllRemembered()
        {
            DiscoveryRuntime discovery = NewDiscovery();
            discovery.RevealDisc(new Vector2(32f, 32f), 6f);

            var observation = new ObservationRuntime();

            Assert.AreEqual(0, observation.ObserverCount);
            Assert.AreEqual(DiscoveryState.Remembered, observation.StateOf(new GridCoord(32, 32), discovery));
        }
    }
}

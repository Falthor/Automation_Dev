using Game.Gameplay.Exploration;
using Game.Gameplay.Selection;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode.Presentation
{
    /// <summary>
    /// The priority stack, which is the whole point of the arbiter.
    ///
    /// <b>What is pinned here and what cannot be.</b> These tests drive
    /// <see cref="EscapeArbiter.ClaimantFor"/> and <see cref="EscapeArbiter.Claimant"/>, never
    /// <c>IsClaimedBy</c>: that one reads the physical key, and an EditMode test has no keyboard to
    /// press. It is not a gap in the coverage, because the exclusivity does not live in the key read.
    /// <c>IsClaimedBy</c> is <c>Claimant == mine &amp;&amp; the key is down</c>, and
    /// <see cref="EscapeArbiter.Claimant"/> is a single value - so pinning the claimant pins that at
    /// most one reader can ever act, which is exactly what fourteen independent <c>if</c>s could not
    /// promise.
    /// </summary>
    public class EscapeArbiterTests
    {
        // ---- The stack, exhaustively ----

        /// <summary>
        /// Every combination of the three facts, as literals. Written out rather than looped so that
        /// a change to the order fails against a table someone decided, not against a rule the test
        /// recomputes the same way the code does.
        /// </summary>
        [TestCase(false, false, false, EscapeClaimant.None)]
        [TestCase(false, false, true, EscapeClaimant.GlobalPanel)]
        [TestCase(false, true, false, EscapeClaimant.ContextualPanel)]
        [TestCase(false, true, true, EscapeClaimant.ContextualPanel)]
        [TestCase(true, false, false, EscapeClaimant.ArmedTool)]
        [TestCase(true, false, true, EscapeClaimant.ArmedTool)]
        [TestCase(true, true, false, EscapeClaimant.ArmedTool)]
        [TestCase(true, true, true, EscapeClaimant.ArmedTool)]
        public void TheStackIsToolThenContextualThenGlobal(bool tool, bool contextual, bool global, EscapeClaimant expected)
        {
            Assert.AreEqual(expected, EscapeArbiter.ClaimantFor(tool, contextual, global));
        }

        /// <summary>
        /// The one overlap that actually happens. Nothing disarms a construction tool when a panel
        /// opens, and clicking a datacard notification opens a robot's own panel - so a player can
        /// have a ghost on the cursor and a panel docked at the same time. Escape drops the ghost:
        /// it is the more transient of the two, and the one the cursor is carrying.
        /// </summary>
        [Test]
        public void AnArmedToolOutranksAPanelThatIsAlsoOpen()
        {
            Assert.AreEqual(EscapeClaimant.ArmedTool,
                EscapeArbiter.ClaimantFor(aToolIsArmed: true, aContextualPanelIsOpen: true, aGlobalPanelIsOpen: false));

            Assert.AreEqual(EscapeClaimant.ArmedTool,
                EscapeArbiter.ClaimantFor(aToolIsArmed: true, aContextualPanelIsOpen: false, aGlobalPanelIsOpen: true));
        }

        /// <summary>
        /// The invariant the fourteen readers broke, stated directly: whatever the state, at most one
        /// tier is the claimant. Two readers can never both act on one keypress.
        /// </summary>
        [Test]
        public void AtMostOneTierEverClaimsIt()
        {
            var tiers = new[] { EscapeClaimant.ArmedTool, EscapeClaimant.ContextualPanel, EscapeClaimant.GlobalPanel };

            for (int state = 0; state < 8; state++)
            {
                EscapeClaimant claimant = EscapeArbiter.ClaimantFor(
                    (state & 1) != 0, (state & 2) != 0, (state & 4) != 0);

                int actors = 0;
                foreach (EscapeClaimant tier in tiers)
                {
                    if (claimant == tier) actors++;
                }

                Assert.LessOrEqual(actors, 1, $"state {state} awarded Escape to {actors} tiers");
            }
        }

        /// <summary>Nothing open and nothing armed: the key belongs to nobody, and None is never something a reader can claim.</summary>
        [Test]
        public void WithNothingOpen_NobodyTakesIt()
        {
            var arbiter = new EscapeArbiter(new SelectionRuntime(), null);

            Assert.AreEqual(EscapeClaimant.None, arbiter.Claimant);
            Assert.IsFalse(arbiter.IsClaimedBy(EscapeClaimant.None), "None is a description of the state, not a tier that acts.");
        }

        // ---- Read against the live selection ----

        /// <summary>
        /// The arbiter over a real <see cref="SelectionRuntime"/>. A null construction service is the
        /// truthful shape of a scene without construction, and means no tool can be armed.
        /// </summary>
        [Test]
        public void ItReadsTheLiveSelection()
        {
            var selection = new SelectionRuntime();
            var arbiter = new EscapeArbiter(selection, null);

            selection.SelectExplorerRobot(new ExplorerRobotRuntime(0, Vector2.zero));
            Assert.AreEqual(EscapeClaimant.ContextualPanel, arbiter.Claimant);

            selection.Clear();
            Assert.AreEqual(EscapeClaimant.None, arbiter.Claimant);

            selection.OpenGlobalPanel("storage");
            Assert.AreEqual(EscapeClaimant.GlobalPanel, arbiter.Claimant);
        }

        /// <summary>
        /// Why the contextual-before-global order never decides anything today: opening either one
        /// closes the other, inside <see cref="SelectionRuntime"/>. Pinned so that the day that stops
        /// being true, this fails here rather than showing up as two panels closing on one keypress.
        /// </summary>
        [Test]
        public void AContextualAndAGlobalPanel_CannotBothBeOpen()
        {
            var selection = new SelectionRuntime();
            var arbiter = new EscapeArbiter(selection, null);

            selection.SelectExplorerRobot(new ExplorerRobotRuntime(0, Vector2.zero));
            selection.OpenGlobalPanel("research");

            Assert.IsNull(selection.SelectedExplorerRobot, "Opening a global panel clears the contextual slots.");
            Assert.AreEqual(EscapeClaimant.GlobalPanel, arbiter.Claimant);

            selection.SelectExplorerRobot(new ExplorerRobotRuntime(1, Vector2.zero));

            Assert.IsNull(selection.ActiveGlobalPanel, "And inspecting something closes the global panel.");
            Assert.AreEqual(EscapeClaimant.ContextualPanel, arbiter.Claimant);
        }
    }
}

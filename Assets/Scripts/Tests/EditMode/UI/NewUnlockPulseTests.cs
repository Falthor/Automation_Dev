using Game.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Game.Tests.EditMode.UI
{
    /// <summary>
    /// The "this is new" blink on a control the player has just been handed - the Research menu the
    /// Core's first directive grants, on the Top Bar card and the Bottom Nav icon at once.
    /// </summary>
    public class NewUnlockPulseTests
    {
        [Test]
        public void ItActuallyBlinks_RatherThanStayingLit()
        {
            bool lit = false;
            bool dark = false;

            // A couple of full cycles, sampled finely enough to land in both halves of each.
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                if (NewUnlockPulse.IsLitAt(t)) lit = true;
                else dark = true;
            }

            Assert.IsTrue(lit, "Never lit - nothing would be announced.");
            Assert.IsTrue(dark, "Never dark - a permanent frame is a badge, not a signal.");
        }

        /// <summary>
        /// Every element reads the same clock, so the Top Bar card and the Bottom Nav icon light and
        /// go dark together. Two independent timers would drift and read as two unrelated alarms.
        /// </summary>
        [Test]
        public void EveryElementSharesOnePhase()
        {
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                Assert.AreEqual(NewUnlockPulse.IsLitAt(t), NewUnlockPulse.IsLitAt(t), $"t={t}");
            }

            // The same instant one period later is the same point in the cycle.
            for (float t = 0f; t < 1f; t += 0.05f)
            {
                Assert.AreEqual(NewUnlockPulse.IsLitAt(t), NewUnlockPulse.IsLitAt(t + 1f), $"t={t}");
            }
        }

        [Test]
        public void NothingNew_LeavesTheElementAlone()
        {
            var element = new VisualElement();

            NewUnlockPulse.Apply(element, isNew: false);

            Assert.IsFalse(element.ClassListContains(NewUnlockPulse.ClassName));
        }

        /// <summary>A null element is not an error: a controller may run before its own tree is built, and a missing widget must not take the frame down with it.</summary>
        [Test]
        public void ANullElement_IsIgnored()
        {
            Assert.DoesNotThrow(() => NewUnlockPulse.Apply(null, isNew: true));
        }
    }
}

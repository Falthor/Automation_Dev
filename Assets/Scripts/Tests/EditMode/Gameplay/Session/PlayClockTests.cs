using Game.Gameplay.Session;
using NUnit.Framework;

namespace Game.Tests.EditMode.Gameplay.Session
{
    /// <summary>
    /// The run chronometer. Everything here is arithmetic on the deltaTime it is handed, which is
    /// the point: pause is not a state this type knows about, it is a deltaTime of zero arriving
    /// from a Time.timeScale the simulation already froze.
    /// </summary>
    public class PlayClockTests
    {
        [Test]
        public void ANewRun_StartsAtZero()
        {
            Assert.AreEqual(0f, new PlayClock().ElapsedSeconds);
        }

        [Test]
        public void ItAccumulatesTheTimeItIsGiven()
        {
            var clock = new PlayClock();

            clock.Advance(0.5f);
            clock.Advance(0.25f);

            Assert.AreEqual(0.75f, clock.ElapsedSeconds, 0.0001f);
        }

        /// <summary>
        /// The whole requirement, expressed the way the game expresses it: pause sets
        /// Time.timeScale to 0, so every frame of a paused game hands the clock a zero. It must
        /// stand still through them and pick up exactly where it stopped, with nothing resetting.
        /// </summary>
        [Test]
        public void APausedFrame_LeavesItWhereItStood_AndItResumesFromThere()
        {
            var clock = new PlayClock();
            clock.Advance(3f);

            for (int pausedFrame = 0; pausedFrame < 100; pausedFrame++) clock.Advance(0f);

            Assert.AreEqual(3f, clock.ElapsedSeconds, 0.0001f, "A paused game must not age the run.");

            clock.Advance(2f);
            Assert.AreEqual(5f, clock.ElapsedSeconds, 0.0001f, "And resuming continues from where it stopped.");
        }

        [Test]
        public void ANegativeDelta_NeverWindsItBack()
        {
            var clock = new PlayClock();
            clock.Advance(4f);

            clock.Advance(-10f);

            Assert.AreEqual(4f, clock.ElapsedSeconds, 0.0001f);
        }

        [Test]
        public void Restore_TakesASavedRunsElapsedTime()
        {
            var clock = new PlayClock();
            clock.Restore(125f);

            Assert.AreEqual(125f, clock.ElapsedSeconds, 0.0001f);

            clock.Advance(1f);
            Assert.AreEqual(126f, clock.ElapsedSeconds, 0.0001f, "And keeps counting from there.");
        }

        [Test]
        public void Restore_OfAnAbsentValue_StartsAtZeroRatherThanThrowing()
        {
            var clock = new PlayClock();
            clock.Advance(9f);

            clock.Restore(null);

            Assert.AreEqual(0f, clock.ElapsedSeconds, "A save from before the clock existed is not a broken save.");
        }

        [Test]
        public void Format_ShowsMinutesAndSecondsBelowAnHour()
        {
            Assert.AreEqual("00:00", PlayClock.Format(0f));
            Assert.AreEqual("00:07", PlayClock.Format(7f));
            Assert.AreEqual("01:05", PlayClock.Format(65f));
            Assert.AreEqual("59:59", PlayClock.Format(3599f));
        }

        [Test]
        public void Format_GrowsToHoursRatherThanLettingMinutesRunPastSixty()
        {
            Assert.AreEqual("1:00:00", PlayClock.Format(3600f));
            Assert.AreEqual("1:23:20", PlayClock.Format(5000f));
            Assert.AreEqual("12:00:00", PlayClock.Format(43200f));
        }

        /// <summary>
        /// Floored, not rounded. Rounding would show 00:01 half a second into the run, putting every
        /// reading up to half a second ahead of the simulation it is there to time - which is the one
        /// thing a measuring instrument may not do.
        /// </summary>
        [Test]
        public void Format_FloorsRatherThanRounding()
        {
            Assert.AreEqual("00:00", PlayClock.Format(0.9f));
            Assert.AreEqual("00:01", PlayClock.Format(1.99f));
        }

        [Test]
        public void Format_OfANegativeValue_ReadsZeroRatherThanAMinusSign()
        {
            Assert.AreEqual("00:00", PlayClock.Format(-5f));
        }
    }
}

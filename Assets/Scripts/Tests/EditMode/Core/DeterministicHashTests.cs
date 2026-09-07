using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode.Core
{
    /// <summary>
    /// The one test in this project whose expected values are written by hand on purpose.
    ///
    /// Everything derived rather than stored depends on this function answering the same thing
    /// forever: the terrain is recomputed at every load, and a sector's name and risk are computed on
    /// demand rather than saved. A test that recomputed its own expectation would move with the
    /// function and prove nothing — only literals can catch a runtime that has changed its mind, or a
    /// well-meaning "cleanup" of the arithmetic.
    ///
    /// <b>If one of these fails, do not update the number.</b> It means every existing world has just
    /// changed shape underneath buildings that were saved. Find out what moved.
    /// </summary>
    public class DeterministicHashTests
    {
        [TestCase(1, 0, 0u, 179721900u)]
        [TestCase(0, 1, 0u, 782447033u)]
        [TestCase(0, 0, 1u, 1617762472u)]
        [TestCase(20260907, 42, 0x9E3779B9u, 3133227730u)]
        [TestCase(-1, -1, 0xFFFFFFFFu, 3159913515u)]
        public void MixIsFrozen(int seed, int index, uint salt, uint expected)
        {
            Assert.AreEqual(expected, DeterministicHash.Mix(seed, index, salt));
        }

        /// <summary>
        /// A known property, asserted so it is not a surprise: all-zero in gives zero out. It is the
        /// usual weakness of a multiply-xorshift mixer and it is <b>unreachable here</b> - every
        /// caller passes a non-zero constant salt, precisely so that a zero seed (which is
        /// TerrainGenerationSettings' own default) still mixes.
        ///
        /// Left as-is rather than fixed with a leading constant: fixing it would change the answer
        /// for every input, which means renaming every sector in every world. If a caller ever needs
        /// salt 0, give it a salt instead of changing this.
        /// </summary>
        [Test]
        public void AllZeroInputGivesZero_WhichIsWhyEverySaltIsNonZero()
        {
            Assert.AreEqual(0u, DeterministicHash.Mix(0, 0, 0u));
            Assert.AreNotEqual(0u, DeterministicHash.Mix(0, 0, 0x9E3779B9u), "a zero seed still mixes, thanks to the salt");
        }

        [Test]
        public void UnitStaysBelowOne_SoACallerScalingItCannotOverrunAnArray()
        {
            for (int seed = 0; seed < 500; seed++)
            {
                double value = DeterministicHash.Unit(seed, seed * 7, 0x85EBCA6Bu);

                Assert.GreaterOrEqual(value, 0.0);
                Assert.Less(value, 1.0, "seed " + seed);
            }
        }

        [Test]
        public void OneBitOfInputChangesAboutHalfTheOutputBits()
        {
            int differingBits = 0;
            const int Samples = 200;

            for (int seed = 0; seed < Samples; seed++)
            {
                uint a = DeterministicHash.Mix(seed, 0, 0u);
                uint b = DeterministicHash.Mix(seed ^ 1, 0, 0u);

                uint diff = a ^ b;
                for (int bit = 0; bit < 32; bit++)
                {
                    if ((diff & (1u << bit)) != 0) differingBits++;
                }
            }

            float average = differingBits / (float)Samples;
            Assert.That(average, Is.InRange(12f, 20f),
                "a one-bit change should avalanche to roughly half of 32 bits, got " + average);
        }

        /// <summary>Different salts have to give genuinely different draws, or "independent" values would move together.</summary>
        [Test]
        public void DifferentSaltsGiveDifferentDraws()
        {
            const uint SaltA = 0x9E3779B9;
            const uint SaltB = 0x85EBCA6B;

            int same = 0;
            for (int seed = 0; seed < 500; seed++)
            {
                if (DeterministicHash.Mix(seed, 0, SaltA) == DeterministicHash.Mix(seed, 0, SaltB)) same++;
            }

            Assert.AreEqual(0, same);
        }
    }
}

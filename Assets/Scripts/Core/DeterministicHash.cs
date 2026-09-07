namespace Game.Core
{
    /// <summary>
    /// Turns integers into well-distributed integers, with the same answer forever.
    ///
    /// <b>"Forever" is the whole point.</b> Anything derived rather than stored has to be rebuilt
    /// identically on a machine and a runtime that do not exist yet - the terrain is re-derived at
    /// every load, and a sector's name and risk are computed on demand rather than saved. If the
    /// function underneath ever changes answer, the world recomposes underneath buildings that
    /// <b>were</b> saved, and a player finds their base straddling ground they do not recognise.
    ///
    /// So the arithmetic is written out here rather than borrowed. Neither
    /// <c>System.Random</c> nor <c>string.GetHashCode</c> may be used for anything derived: both are
    /// explicitly free to change between runtime versions, and a Unity upgrade would be enough. They
    /// are fine for what is thrown away or saved, and only for that.
    ///
    /// Pinned by tests with hard-coded expected values - the only kind of test that can catch a
    /// runtime changing its mind, since any test that recomputes the expectation would change with it.
    /// </summary>
    public static class DeterministicHash
    {
        /// <summary>
        /// Mixes three integers into one. A splitmix-style avalanche: multiply, xor-shift, repeat -
        /// enough that one bit changing in any input changes about half the output bits, which is all
        /// any caller here needs.
        ///
        /// <c>unchecked</c> is explicit rather than relying on the project's overflow setting: the
        /// wrapping <b>is</b> the mixing, and a build configured to throw on overflow would turn this
        /// into an exception instead of a number.
        /// </summary>
        public static uint Mix(int seed, int index, uint salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= (uint)index * 2246822519u;
                h ^= salt;
                h ^= h >> 15;
                h *= 2246822519u;
                h ^= h >> 13;
                h *= 3266489917u;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>
        /// A value in [0, 1) from the same mix. Divided by 2^32 rather than by uint.MaxValue so the
        /// result can never reach exactly 1, which a caller scaling it into an array index would have
        /// to special-case.
        /// </summary>
        public static double Unit(int seed, int index, uint salt)
            => Mix(seed, index, salt) / 4294967296.0;
    }
}

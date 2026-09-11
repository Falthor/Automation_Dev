using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// How much derived ore a sector holds, and how that changes with distance from the Core.
    ///
    /// <b>A cluster grows the further out it is, and that is the whole reason this is a profile
    /// rather than two numbers.</b> Near the Core's edge a patch is six to ten tiles; at the limit a
    /// robot will wander to it is ten to fifteen. Distance is the only thing the game asks a player
    /// to spend on exploration, so it has to be the thing that pays - a constant size makes the far
    /// half of the map the same as the near half with a longer walk.
    ///
    /// <b>Grouped because these seven numbers only ever travel together.</b> The alternative was a
    /// nine-argument constructor on <c>SectorCatalog</c> with the formula buried in a private method;
    /// here the formula has a name, and a test can drive it without building a world.
    ///
    /// The radii are not this object's to invent: the near one is the Core's furthest reach
    /// (<c>CoreRuntime.ExtendedActionRadiusCells</c>) and the far one is how far a robot wanders
    /// (<c>ExplorerRobotSettings.MaxRadiusCells</c>). They are handed in so that each figure exists
    /// once, in the system that owns it.
    /// </summary>
    public readonly struct OreClusterProfile
    {
        /// <summary>One sector in this many carries a cluster. Flat with distance - what grows is the size, not the frequency.</summary>
        public readonly int OneSectorIn;

        public readonly int NearMinTiles;
        public readonly int NearMaxTiles;
        public readonly int FarMinTiles;
        public readonly int FarMaxTiles;

        /// <summary>Where the near band applies: derived ore starts no closer than this, so this is the first distance a cluster can exist at.</summary>
        public readonly float NearRadiusCells;

        /// <summary>Where the far band applies, and past which nothing grows further.</summary>
        public readonly float FarRadiusCells;

        public OreClusterProfile(int oneSectorIn,
            int nearMinTiles, int nearMaxTiles, int farMinTiles, int farMaxTiles,
            float nearRadiusCells, float farRadiusCells)
        {
            OneSectorIn = Mathf.Max(1, oneSectorIn);

            NearMinTiles = Mathf.Max(1, nearMinTiles);
            NearMaxTiles = Mathf.Max(NearMinTiles, nearMaxTiles);
            FarMinTiles = Mathf.Max(1, farMinTiles);
            FarMaxTiles = Mathf.Max(FarMinTiles, farMaxTiles);

            NearRadiusCells = Mathf.Max(0f, nearRadiusCells);
            FarRadiusCells = Mathf.Max(NearRadiusCells + 1f, farRadiusCells);
        }

        /// <summary>
        /// How far along the near-to-far ramp a distance sits, clamped at both ends.
        ///
        /// Clamped rather than extrapolated on purpose. Inside the near radius there is no derived
        /// ore at all, so the value there is only ever asked for by a caller that has not checked;
        /// beyond the far radius a robot cannot reach, and a cluster that kept growing off the end
        /// would be a promise the game never keeps.
        /// </summary>
        public float RampAt(float distanceCells)
            => Mathf.Clamp01((distanceCells - NearRadiusCells) / (FarRadiusCells - NearRadiusCells));

        /// <summary>The smallest a cluster at this distance may be.</summary>
        public int MinTilesAt(float distanceCells)
            => Mathf.RoundToInt(Mathf.Lerp(NearMinTiles, FarMinTiles, RampAt(distanceCells)));

        /// <summary>The largest a cluster at this distance may be.</summary>
        public int MaxTilesAt(float distanceCells)
            => Mathf.Max(MinTilesAt(distanceCells), Mathf.RoundToInt(Mathf.Lerp(NearMaxTiles, FarMaxTiles, RampAt(distanceCells))));
    }
}

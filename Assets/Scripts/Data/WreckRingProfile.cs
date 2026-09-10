using UnityEngine;

namespace Game.Data
{
    /// <summary>One band of the disc around the Core, and how many wrecks it holds.</summary>
    [System.Serializable]
    public struct WreckRing
    {
        [Min(0f)] public float InnerRadiusCells;
        [Min(0f)] public float OuterRadiusCells;
        [Min(0)] public int Count;

        public WreckRing(float innerRadiusCells, float outerRadiusCells, int count)
        {
            InnerRadiusCells = innerRadiusCells;
            OuterRadiusCells = outerRadiusCells;
            Count = count;
        }
    }

    /// <summary>
    /// Where the wrecks are, as a set of concentric rings.
    ///
    /// <b>Rings rather than a density, because a density cannot give both answers.</b> Uniform over
    /// a 330-cell disc, the same figure that puts a wreck within the first few minutes puts a hundred
    /// of them on the map, and the figure that makes eight rare puts the first one three quarters of
    /// an hour in. The rings decouple the two: the innermost is deliberately tight, so a robot -
    /// which always starts there - crosses one almost immediately, while the outer band is wide
    /// enough that its four are a long-term prospect.
    ///
    /// <b>The structure does the separating, so nothing has to check.</b> Rings separate radially. A
    /// wreck's angle is its rank's share of the circle plus a jitter bounded to
    /// <see cref="AngularJitterFraction"/> of that share - which is what separates them angularly,
    /// by construction. No proximity test, no register of what is already placed, no rejection loop:
    /// a loop would do the same job worse, and would make the result depend on the order things were
    /// drawn in.
    ///
    /// <b>The minimum angular separation is derived and deliberately not a setting.</b> It falls out
    /// of the count and the jitter (<see cref="MinimumSeparationDegrees"/>); exposing it as well
    /// would allow three numbers that contradict each other.
    /// </summary>
    public readonly struct WreckRingProfile
    {
        /// <summary>
        /// How far a wreck's angle may stray from its share of the circle, as a fraction of that
        /// share. A third: two wrecks in a ring sit 180° apart and may each wander 60°, so they can
        /// never come closer than 60°; four sit 90° apart and can never come closer than 30°.
        ///
        /// Not a setting - see the class summary.
        /// </summary>
        public const float AngularJitterFraction = 1f / 3f;

        readonly WreckRing[] _rings;

        public WreckRingProfile(WreckRing[] rings)
        {
            _rings = rings ?? System.Array.Empty<WreckRing>();
        }

        public int RingCount => _rings.Length;

        public WreckRing Ring(int ring) => _rings[ring];

        /// <summary>How many wrecks in total. Eight at the shipped settings.</summary>
        public int TotalCount
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _rings.Length; i++) total += Mathf.Max(0, _rings[i].Count);
                return total;
            }
        }

        /// <summary>
        /// Turns a flat site index into the ring it belongs to and its rank within that ring - the
        /// pair everything about a wreck is derived from.
        /// </summary>
        public bool Locate(int siteIndex, out int ring, out int rankInRing)
        {
            ring = 0;
            rankInRing = 0;
            if (siteIndex < 0) return false;

            int remaining = siteIndex;
            for (int i = 0; i < _rings.Length; i++)
            {
                int count = Mathf.Max(0, _rings[i].Count);
                if (remaining < count)
                {
                    ring = i;
                    rankInRing = remaining;
                    return true;
                }

                remaining -= count;
            }

            return false;
        }

        /// <summary>The share of the circle one rank of this ring gets.</summary>
        public float SpacingDegrees(int ring)
        {
            int count = Mathf.Max(1, _rings[ring].Count);
            return 360f / count;
        }

        /// <summary>
        /// The closest two wrecks of this ring can ever be, in degrees. Derived: the share of the
        /// circle, less twice the jitter either of them may take out of it.
        /// </summary>
        public float MinimumSeparationDegrees(int ring)
            => SpacingDegrees(ring) * (1f - 2f * AngularJitterFraction);
    }
}

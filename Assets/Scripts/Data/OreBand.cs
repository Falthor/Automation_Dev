using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// One guaranteed band of ore beyond the Core's starting cluster (WorldGenerator, MAP.md): a
    /// ring of distance from the Core, and how many deposits of each resource it guarantees there.
    /// Each resource's count is drawn once per world within its own min-max spread rather than
    /// fixed, so no two worlds carry exactly the same amount - min == max for a resource this band
    /// does not vary, and min == max == 0 for one it does not guarantee at all.
    ///
    /// A list of these (<see cref="WorldGenerationSettings.OreBands"/>) is what lets a second,
    /// further tier be added later as one more entry, rather than a rewrite - the same reason
    /// <see cref="WreckRing"/> is a list on <see cref="WreckRingProfile"/> instead of two named
    /// rings hand-written into the code that places them.
    /// </summary>
    [System.Serializable]
    public struct OreBand
    {
        [Min(0f)] public float MinDistanceCells;
        [Min(0f)] public float MaxDistanceCells;

        [Min(0)] public int IronDepositsMin;
        [Min(0)] public int IronDepositsMax;
        [Min(0)] public int CopperDepositsMin;
        [Min(0)] public int CopperDepositsMax;
        [Min(0)] public int CoalDepositsMin;
        [Min(0)] public int CoalDepositsMax;

        public OreBand(float minDistanceCells, float maxDistanceCells,
            int ironMin, int ironMax, int copperMin, int copperMax, int coalMin, int coalMax)
        {
            MinDistanceCells = minDistanceCells;
            MaxDistanceCells = maxDistanceCells;
            IronDepositsMin = ironMin;
            IronDepositsMax = ironMax;
            CopperDepositsMin = copperMin;
            CopperDepositsMax = copperMax;
            CoalDepositsMin = coalMin;
            CoalDepositsMax = coalMax;
        }
    }
}

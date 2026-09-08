namespace Game.Gameplay.Expeditions
{
    /// <summary>
    /// One angular slice of ground around the Core: the unit the player chooses and cartographs.
    ///
    /// <b>Called an expedition zone, and never a bare "zone".</b> Three divisions cohabit and each
    /// answers a different question: a <b>sector</b> (<c>Game.Grid.SectorGrid</c>) is the square a
    /// mission is aimed at, a <b>signal zone</b> is the territory a Core or an AI agent holds, and this
    /// is the wedge a run explores. <c>MAP.md</c> already forbids the bare word for the second reason;
    /// the qualified name is what keeps the design's own vocabulary without inheriting the ambiguity.
    ///
    /// <b>A value, not an object with behaviour.</b> Whether a point falls in this zone is asked of
    /// <see cref="ExpeditionZoneSystem.ZoneAt"/>, which partitions the whole circle in one arithmetic
    /// step - six independent containment tests would be six chances to leave a gap or an overlap at a
    /// boundary.
    /// </summary>
    public readonly struct ExpeditionZone
    {
        public readonly int Index;

        /// <summary>The bearing the slice is centred on, in degrees, measured the way <c>Atan2</c> measures: 0 is +X, growing anticlockwise.</summary>
        public readonly float CentreDegrees;

        /// <summary>Half the slice. The zone spans <c>CentreDegrees ± HalfAngleDegrees</c>.</summary>
        public readonly float HalfAngleDegrees;

        /// <summary>Where the zone starts: the Core's own ground ends here. Frozen at layout - see <see cref="ExpeditionZoneSystem.InnerRadiusCells"/>.</summary>
        public readonly float InnerRadiusCells;

        /// <summary>Where it ends. Derived, and what makes a zone finite and therefore cartographable at all.</summary>
        public readonly float OuterRadiusCells;

        public ExpeditionZone(int index, float centreDegrees, float halfAngleDegrees,
            float innerRadiusCells, float outerRadiusCells)
        {
            Index = index;
            CentreDegrees = centreDegrees;
            HalfAngleDegrees = halfAngleDegrees;
            InnerRadiusCells = innerRadiusCells;
            OuterRadiusCells = outerRadiusCells;
        }
    }

    /// <summary>How far a zone has been mapped: ground seen against ground there is. Never a count of sites - see <see cref="ExpeditionZoneSystem.CartographyOf"/>.</summary>
    public readonly struct ExpeditionZoneCartography
    {
        public readonly int DiscoveredCells;
        public readonly int TotalCells;

        public ExpeditionZoneCartography(int discoveredCells, int totalCells)
        {
            DiscoveredCells = discoveredCells;
            TotalCells = totalCells;
        }

        /// <summary>0 to 1. A zone with no ground in it reads as fully mapped rather than dividing by zero: there is nothing left to see.</summary>
        public float Ratio => TotalCells <= 0 ? 1f : DiscoveredCells / (float)TotalCells;
    }

    /// <summary>Why a zone could not be chosen. Named rather than boolean so a caller can say what is wrong.</summary>
    public enum ZoneChoiceRefusal
    {
        None,
        NotAZone,

        /// <summary>A zone is already chosen and this is not it. The other five are locked until the current one is mapped.</summary>
        AlreadyChosen
    }

    /// <summary>Why a sector may not be targeted. The zone half of the launch gate - <c>MissionSystem.CanLaunch</c> is what applies it.</summary>
    public enum ExpeditionZoneRefusal
    {
        None,

        /// <summary>The six zones are still on offer. Nothing launches until the player has picked a direction.</summary>
        NoZoneChosen,

        /// <summary>In a zone, but not the one being mapped - or in none at all, which is ground no zone covers.</summary>
        OutsideChosenZone
    }
}

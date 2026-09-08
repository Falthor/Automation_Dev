using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// How the ground around the Core is cut into expedition zones, and how much of each kind of site
    /// one zone holds.
    ///
    /// <b>Nothing derived lives here.</b> The slice angle is <see cref="SliceDegrees"/>, computed from
    /// the count; the exploration threshold and a zone's outer edge are not here at all - they come
    /// from the Core's maximum radius and the territory gap, through
    /// <c>Game.Gameplay.Sectors.SectorMissionRange</c>. Exposing either as a setting would allow a
    /// combination that contradicts itself: six zones with a 90° slice, or an outer edge inside the
    /// threshold it is supposed to sit beyond.
    ///
    /// The map's own size is not here either, for the same reason it is not on
    /// <see cref="SectorSettings"/> - it belongs to the terrain that actually builds the world.
    /// </summary>
    [CreateAssetMenu(fileName = "ExpeditionZoneSettings", menuName = "Game/World/Expedition Zone Settings")]
    public sealed class ExpeditionZoneSettings : ScriptableObject
    {
        [Header("Découpage")]

        /// <summary>
        /// How many angular slices the ring around the Core is cut into. Six by design - one secondary
        /// Core site per zone, and choosing a direction is choosing a future neighbour - but a count
        /// rather than the word "six" anywhere, so the whole geometry follows a change here.
        /// </summary>
        [SerializeField, Min(1)] int zoneCount = 6;

        /// <summary>How far a site may be pushed in or out from where the even ladder would put it, in cells.</summary>
        [SerializeField, Min(0f)] float radiusJitterCells = 20f;

        /// <summary>
        /// How far a site may be swung off its nominal bearing, as a fraction of one slice. Capped
        /// below a half-slice because the sites of a zone must stay inside that zone: past that a
        /// jitter could throw one into the neighbouring slice, where nothing would ever reach it.
        /// </summary>
        [SerializeField, Range(0f, 0.4f)] float angleJitterFraction = 0.1f;

        [Header("Contenu d'une zone")]

        [SerializeField, Min(0)] int prospectionSites = 3;

        /// <summary>
        /// The far reconnaissances, on the zone's outer border. Two is what makes the secondary Core
        /// site worth drawing between them rather than announcing: some runs find it on the first,
        /// some on the second, and the spread between two players is one mission.
        /// </summary>
        [SerializeField, Min(0)] int farExplorationSites = 2;

        [SerializeField, Min(0)] int recoverySitesMin = 2;
        [SerializeField, Min(0)] int recoverySitesMax = 3;

        [SerializeField, Min(0)] int civilisationStudiesMin = 1;
        [SerializeField, Min(0)] int civilisationStudiesMax = 2;

        [Header("Le stock caché")]

        /// <summary>
        /// Sites a zone holds without showing them: a field study can turn one up, and once the stock
        /// is spent the studies find only ground.
        ///
        /// <b>Finite, and drawn with everything else at derivation.</b> That is what keeps a zone
        /// bounded while still being richer than its first survey said - and it is why cartography is
        /// measured in ground rather than in sites, since this number can only ever make the site count
        /// go up.
        /// </summary>
        [SerializeField, Min(0)] int hiddenSitesMin = 3;
        [SerializeField, Min(0)] int hiddenSitesMax = 4;

        public int ZoneCount => zoneCount;
        public float RadiusJitterCells => radiusJitterCells;
        public float AngleJitterFraction => angleJitterFraction;

        public int ProspectionSites => prospectionSites;
        public int FarExplorationSites => farExplorationSites;
        public int RecoverySitesMin => recoverySitesMin;
        public int RecoverySitesMax => recoverySitesMax;
        public int CivilisationStudiesMin => civilisationStudiesMin;
        public int CivilisationStudiesMax => civilisationStudiesMax;
        public int HiddenSitesMin => hiddenSitesMin;
        public int HiddenSitesMax => hiddenSitesMax;

        /// <summary>
        /// One slice, in degrees. Derived, so it is a property and never a field anyone can set: an
        /// angle entered beside the count is a second figure that stops agreeing the day the count
        /// moves, and six 90° slices do not cover a circle.
        /// </summary>
        public float SliceDegrees => 360f / Mathf.Max(1, zoneCount);

        void OnValidate()
        {
            if (recoverySitesMin > recoverySitesMax || civilisationStudiesMin > civilisationStudiesMax
                || hiddenSitesMin > hiddenSitesMax)
            {
                Debug.LogWarning($"{name}: a site count's minimum is above its maximum. The draw reads "
                    + "the pair as a range and an inverted one yields the minimum alone.", this);
            }
        }
    }
}

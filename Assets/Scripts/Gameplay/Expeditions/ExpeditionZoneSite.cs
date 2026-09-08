using Game.Core;

namespace Game.Gameplay.Expeditions
{
    /// <summary>
    /// What a site in a zone is for. The four a robot can actually carry out plus the one that needs
    /// units, which is placed now and stays locked - a mission that promises reads better than one that
    /// appears out of nowhere.
    /// </summary>
    public enum ExpeditionSiteKind
    {
        /// <summary>A deposit worth digging. In the mining band, where a prospection is allowed to go.</summary>
        Prospection,

        /// <summary>On the zone's outer border, past the exploration threshold - where a far reconnaissance is allowed to go, and the only kind that is.</summary>
        ExplorationLointaine,

        Recuperation,

        /// <summary>Needs units, which need the weapons branch. Placed with everything else and unreachable for the whole introduction, deliberately.</summary>
        EtudeCivilisation,

        /// <summary>
        /// A field study - <c>MissionKind.EtudeDeTerrain</c>. An ordinary site in the near stretch, like
        /// a prospection's: the design gives it no free targeting and no designation mode of its own.
        /// What makes it different is that it is the only mission that can turn up a hidden site.
        /// </summary>
        EtudeDeTerrain
    }

    /// <summary>
    /// What a far reconnaissance puts on the map when it lands.
    ///
    /// <b>Both far explorations carry one, and that is the point.</b> The secondary Core site is drawn
    /// between them, so one player finds it on the first and another on the second - and the one that
    /// does not carry it must still be worth flying, or half the runs discover that a quest was empty.
    /// The other therefore carries the trace that puts the zone's civilisation study on the map: the
    /// same kind of answer, from content the zone already holds, rather than a consolation prize.
    /// </summary>
    public enum ExpeditionFinding
    {
        None,
        SiteNoyauSecondaire,
        TraceCivilisation
    }

    /// <summary>
    /// One point of interest in a zone.
    ///
    /// <b>Identity is derived, state is not.</b> Kind, cell and finding are pure functions of the world
    /// seed and the zone's index, exactly as a sector's identity is; what the player has done to the
    /// site is the only thing that enters the save. Splitting them that way is what lets a zone be
    /// re-derived at load and then have its history laid back over it.
    /// </summary>
    public sealed class ExpeditionZoneSite
    {
        public int ZoneIndex { get; }

        /// <summary>Position in its zone's own list. Stable, because the list is derived in a fixed order - which is what a save can refer to.</summary>
        public int IndexInZone { get; }

        public ExpeditionSiteKind Kind { get; }

        public GridCoord Cell { get; }

        public ExpeditionFinding Finding { get; }

        /// <summary>Part of the zone's finite hidden stock: on the map only once a field study has turned it up.</summary>
        public bool IsHidden { get; }

        /// <summary>Whether the player can see it. True from the start for everything but the hidden stock.</summary>
        public bool IsRevealed { get; private set; }

        /// <summary>Whether it has already given what it holds. A consumed site stays on the map as a mark of what was done there.</summary>
        public bool IsConsumed { get; private set; }

        public ExpeditionZoneSite(int zoneIndex, int indexInZone, ExpeditionSiteKind kind, GridCoord cell,
            ExpeditionFinding finding, bool isHidden)
        {
            ZoneIndex = zoneIndex;
            IndexInZone = indexInZone;
            Kind = kind;
            Cell = cell;
            Finding = finding;
            IsHidden = isHidden;
            IsRevealed = !isHidden;
        }

        /// <summary>Returns whether this changed anything, so the caller can tell a real find from turning up a site already known.</summary>
        internal bool Reveal()
        {
            if (IsRevealed) return false;

            IsRevealed = true;
            return true;
        }

        internal bool Consume()
        {
            if (IsConsumed) return false;

            IsConsumed = true;
            return true;
        }

        /// <summary>Used only when restoring: state comes from the save rather than from play.</summary>
        internal void RestoreState(bool revealed, bool consumed)
        {
            IsRevealed = revealed;
            IsConsumed = consumed;
        }
    }
}

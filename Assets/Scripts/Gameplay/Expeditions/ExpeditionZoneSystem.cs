using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Sectors;
using Game.Grid;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Gameplay.Expeditions
{
    /// <summary>
    /// The ring of ground around the Core cut into expedition zones, what each one holds, and which one
    /// the player is working.
    ///
    /// <b>No interface, on purpose.</b> A zone has bounds, a derived content and a choice; all three are
    /// testable with no screen. The map comes afterwards and must not get to shape the model - the same
    /// rule the mission process was built under.
    ///
    /// <b>The outer edge is derived and never entered.</b> It is the exploration threshold plus one
    /// maximum Core radius, and the threshold itself is read off <see cref="SectorMissionRange"/> rather
    /// than recomputed here: two copies of "2 x max radius + territory gap" are two things that stop
    /// agreeing the day a Core reaches further. That edge is the whole reason a zone is finite, and
    /// therefore the reason it can be mapped to the end at all - an unbounded 60° wedge runs to the
    /// corner of the map and never finishes.
    ///
    /// <b>The inner edge is frozen at layout, and that is a deliberate reading of the design.</b> The
    /// directive says a zone starts at the Core's *current* radius. Taken live, the zone would shrink
    /// under the player every time research widened the Core: sites near the inner edge would fall out
    /// of their own zone, and - measurably - cartography would go backwards, because dropping ground
    /// that is fully discovered from both halves of a ratio lowers it. So the radius is read once, when
    /// the zones are laid out, and travels in the save. See <see cref="CartographyOf"/>.
    /// </summary>
    public sealed class ExpeditionZoneSystem
    {
        // Distinct salts, so a zone's site counts, its bearings, its radii and which exploration
        // carries the secondary Core are independent draws rather than views of one number.
        const uint RecoveryCountSalt = 0x1B873593;
        const uint StudyCountSalt = 0xCC9E2D51;
        const uint HiddenCountSalt = 0xE6546B64;
        const uint CarrierSalt = 0x85EBCA77;
        const uint BearingSalt = 0x2545F491;
        const uint RadiusSalt = 0x9E3779B1;
        const uint HiddenKindSalt = 0xA24BAED4;

        readonly ExpeditionZoneSettings _settings;
        readonly SectorGrid _grid;
        readonly SectorMissionRange _range;
        readonly Vector2 _coreCentreCells;
        readonly int _seed;

        /// <summary>Derived on first request, per zone. A run only ever works one zone, so deriving all six up front would be work for five of them nobody asked for.</summary>
        readonly List<ExpeditionZoneSite>[] _sites;

        readonly int[] _totalCells;
        readonly int[] _discoveredCells;

        /// <summary>Which zones a robot has reported from - see <see cref="IsSurveyed"/>.</summary>
        readonly bool[] _surveyed;

        /// <summary>Whether the game has already put a site forward once - see <see cref="HighlightedSite"/>. Once spent it never lights again, in this zone or any other.</summary>
        bool _highlightSpent;

        /// <summary>The discovery version the counts above were measured at, and -1 for never.</summary>
        int _measuredAtDiscoveryVersion = -1;

        /// <summary>Bumped by a restore, which can move the inner edge and therefore every count. Paired with the version above so neither alone can pass a stale measurement.</summary>
        int _layoutVersion;
        int _measuredAtLayoutVersion = -1;

        public ExpeditionZoneSystem(ExpeditionZoneSettings settings, SectorGrid grid, SectorMissionRange range,
            Vector2 coreCentreCells, float innerRadiusCells, int seed)
        {
            _settings = settings;
            _grid = grid;
            _range = range;
            _coreCentreCells = coreCentreCells;
            _seed = seed;

            InnerRadiusCells = Mathf.Max(0f, innerRadiusCells);

            int count = Mathf.Max(0, settings != null ? settings.ZoneCount : 0);
            _sites = new List<ExpeditionZoneSite>[count];
            _totalCells = new int[count];
            _discoveredCells = new int[count];
            _surveyed = new bool[count];
        }

        // ---- Geometry ----

        public int ZoneCount => _sites.Length;

        /// <summary>One slice, in degrees. Read off the settings, which derive it from the count - not recomputed here.</summary>
        public float SliceDegrees => _settings != null ? _settings.SliceDegrees : 0f;

        /// <summary>
        /// Where every zone starts. Frozen when the zones were laid out and carried in the save - see
        /// the class summary for why it is not read live off the Core.
        /// </summary>
        public float InnerRadiusCells { get; private set; }

        /// <summary>
        /// Where every zone ends: the exploration threshold plus one maximum Core radius, so a zone
        /// covers the whole mining band and reaches exactly one Core's worth past the threshold - far
        /// enough to hold a secondary Core site, and no further.
        ///
        /// A property and never a field, like the threshold it is built on.
        /// </summary>
        public float OuterRadiusCells
            => _range != null ? _range.ExplorationMinimumCells + _range.MaxCoreRadiusCells : 0f;

        /// <summary>Where the far reconnaissances live: past the threshold, which is the only band a far exploration may be sent into. A site is never placed where its own kind of mission cannot go.</summary>
        float FarStretchInner => Mathf.Clamp(_range != null ? _range.ExplorationMinimumCells : 0f,
            InnerRadiusCells, OuterRadiusCells);

        public ExpeditionZone ZoneOf(int index)
        {
            if (index < 0 || index >= ZoneCount) return default;

            return new ExpeditionZone(index, index * SliceDegrees, SliceDegrees * 0.5f,
                InnerRadiusCells, OuterRadiusCells);
        }

        /// <summary>
        /// The zone a point in cell space falls in, or -1 for ground no zone covers.
        ///
        /// <b>One arithmetic step, not one test per zone.</b> The bearing is folded into [0, 360) and
        /// divided by the slice, so the circle is partitioned by construction: no boundary can be
        /// claimed twice or by nobody, whatever the count and whatever floating point does at the seam.
        /// </summary>
        public int ZoneAt(Vector2 cellPosition)
        {
            int count = ZoneCount;
            if (count <= 0) return -1;

            Vector2 offset = cellPosition - _coreCentreCells;
            float distance = offset.magnitude;

            // Same open/closed convention as SectorMissionRange's band: the inner edge belongs to the
            // Core's own ground, the outer edge belongs to the zone.
            if (distance <= InnerRadiusCells || distance > OuterRadiusCells) return -1;

            float slice = SliceDegrees;
            if (slice <= 0f) return -1;

            float degrees = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            int index = Mathf.FloorToInt(Mathf.Repeat(degrees + slice * 0.5f, 360f) / slice);
            return Mathf.Clamp(index, 0, count - 1);
        }

        /// <summary>
        /// The zone a sector belongs to, measured on the sector's <b>centre</b> - the same rule
        /// <see cref="SectorMissionRange"/> uses for its bands. A sector straddles a boundary constantly,
        /// and a per-cell rule would make membership both fuzzy and expensive.
        /// </summary>
        public int ZoneOfSector(int sectorIndex)
            => _grid == null || !_grid.ContainsIndex(sectorIndex) ? -1 : ZoneAt(_grid.CenterCells(sectorIndex));

        // ---- The choice ----

        /// <summary>Which zone the player is working, or -1 while the six are still on offer.</summary>
        public int ChosenZone { get; private set; } = -1;

        /// <summary>
        /// Whether the zone being worked has been mapped far enough to open the other five again.
        ///
        /// <b>The lock is a wait, not a forfeit.</b> The design asks that the current zone be mapped
        /// before moving on, and the first screen promises the others back at the end of this one -
        /// so it lifts on cartography rather than never. Measured in surface like everything else here.
        /// </summary>
        public bool ChosenZoneIsMapped(DiscoveryRuntime discovery)
        {
            if (ChosenZone < 0 || _settings == null) return false;

            return CartographyOf(ChosenZone, discovery).Ratio >= _settings.ZoneReleaseRatio;
        }

        /// <summary>
        /// Whether a zone may still be chosen: every zone before a choice, only the chosen one while it
        /// is being worked, and every zone again once it has been mapped.
        /// </summary>
        public bool IsAvailable(int zoneIndex, DiscoveryRuntime discovery)
        {
            if (zoneIndex < 0 || zoneIndex >= ZoneCount) return false;
            if (ChosenZone < 0 || ChosenZone == zoneIndex) return true;

            return ChosenZoneIsMapped(discovery);
        }

        /// <summary>
        /// Picks a direction, which locks the other five.
        ///
        /// During the introduction the zones are equivalent, so this is a choice of heading and nothing
        /// else - what it buys is that everything afterwards has one place to happen.
        /// </summary>
        public ZoneChoiceRefusal Choose(int zoneIndex, DiscoveryRuntime discovery)
        {
            if (zoneIndex < 0 || zoneIndex >= ZoneCount) return ZoneChoiceRefusal.NotAZone;

            // Moving to another zone is allowed once the current one has been mapped - the lock is a
            // wait, not a forfeit.
            if (ChosenZone >= 0 && ChosenZone != zoneIndex && !ChosenZoneIsMapped(discovery))
            {
                return ZoneChoiceRefusal.AlreadyChosen;
            }

            bool changed = ChosenZone != zoneIndex;
            ChosenZone = zoneIndex;

            // <b>The ordering constraint, held by construction rather than by remembering.</b> A zone's
            // content is derived on first request and kept; anything that had already asked about this
            // one - a screen offering the six, a hover - would have cached the derived content and the
            // composition would have arrived too late to be seen. Dropping it here means the order in
            // which the two happen cannot matter, which is the same hazard MAP.md §6 names for placed
            // sector content and the reason it is a rule about data rather than a step to perform first.
            //
            // Nothing is lost by re-deriving: no mission can have launched before a zone was chosen, so
            // no site can carry state yet. A test states that rather than leaving it assumed.
            if (changed) _sites[zoneIndex] = null;

            return ZoneChoiceRefusal.None;
        }

        /// <summary>
        /// Whether a robot has reported from this zone.
        ///
        /// <b>What the first mission is for.</b> A zone that has been chosen is still only a direction:
        /// its content exists - it was derived the moment it was picked - but nobody has been to see it.
        /// The discovery comes back with the list, and that is when the zone's sites become something the
        /// player can be shown and can aim at. Before it lands there is one thing on offer, which is
        /// going to look.
        ///
        /// Per zone rather than a single flag: moving on to another zone once this one is mapped starts
        /// that one unsurveyed, and coming back to one already visited must not un-know it.
        /// </summary>
        public bool IsSurveyed(int zoneIndex)
            => zoneIndex >= 0 && zoneIndex < _surveyed.Length && _surveyed[zoneIndex];

        /// <summary>Records that a mission has reported from this zone. Idempotent: the second report tells the player nothing new.</summary>
        public void MarkSurveyed(int zoneIndex)
        {
            if (zoneIndex < 0 || zoneIndex >= _surveyed.Length) return;

            _surveyed[zoneIndex] = true;
        }

        /// <summary>
        /// The one site the game puts forward, or null.
        ///
        /// <b>The game shows once.</b> After the first report the map opens with one recovery site set
        /// apart from the others, so a player who has just been handed a list of marks has somewhere to
        /// start. It stays a choice: a point put forward is not a quest marker, and passing it over
        /// costs nothing and needs no refusing.
        ///
        /// <b>It goes out at the first launch, not at the completion</b>, and never comes back - for
        /// either reason it would stop being a suggestion: kept until the site is exploited it becomes a
        /// rail, offered again in the next zone it becomes a tutorial that never ends.
        ///
        /// Derived rather than stored: the first recovery still standing in the chosen zone. Only
        /// whether it has been spent is state, which is the one thing that cannot be recomputed.
        /// </summary>
        public ExpeditionZoneSite HighlightedSite
        {
            get
            {
                if (_highlightSpent || ChosenZone < 0 || !IsSurveyed(ChosenZone)) return null;

                foreach (ExpeditionZoneSite site in SitesOf(ChosenZone))
                {
                    if (site.Kind != ExpeditionSiteKind.Recuperation) continue;
                    if (!site.IsRevealed || site.IsConsumed) continue;

                    return site;
                }
                return null;
            }
        }

        /// <summary>
        /// Puts the highlight out for good, at the launch that follows it.
        ///
        /// <b>Spent only while one is actually showing.</b> The discovery is a launch too, and it goes
        /// out before the zone has been surveyed - so an unconditional spend would put out a highlight
        /// that had never been lit, and the player would never see it at all.
        /// </summary>
        public void SpendHighlight()
        {
            if (HighlightedSite != null) _highlightSpent = true;
        }

        /// <summary>
        /// Whether this zone carries the composed content rather than the derived one: it is the zone
        /// the player picked, and the settings ask for a composition.
        ///
        /// <b>A rule about the data, never about the geometry.</b> There is no zone 0 and no "is this
        /// the starting slice" test - the six are equivalent until the choice, and it is the choice that
        /// decides. That is what keeps it durable: no starting perimeter to maintain, and it survives
        /// the player picking any direction.
        /// </summary>
        public bool IsComposed(int zoneIndex)
            => _settings != null && _settings.ComposeFirstChosenZone
               && zoneIndex >= 0 && zoneIndex == ChosenZone;

        /// <summary>
        /// The zone half of the launch gate. <c>MissionSystem.CanLaunch</c> is what applies it, and a
        /// test that starts anywhere but <c>TryLaunch</c> proves nothing about whether it is applied at
        /// all (DEVELOPMENT_RULES.md §7).
        /// </summary>
        public ExpeditionZoneRefusal MayTarget(int sectorIndex)
        {
            int zone = ZoneOfSector(sectorIndex);

            // Ground no zone covers is refused whatever has been chosen: inside the Core's own reach,
            // or past the ring altogether.
            if (zone < 0) return ExpeditionZoneRefusal.OutsideChosenZone;

            // <b>While nothing is chosen, every zone is a legal target</b> - because sending the first
            // robot somewhere is what chooses it. A separate "choose" gesture would be a second click
            // meaning something a click does not otherwise mean on this map, and a decision taken with
            // nothing to tell the six apart. See WouldChoose.
            if (ChosenZone < 0) return ExpeditionZoneRefusal.None;

            if (zone == ChosenZone) return ExpeditionZoneRefusal.None;

            // Once the chosen zone is mapped the others open again, and aiming into one of them chooses
            // it exactly as the first launch did. Nothing here is permanent.
            return ChosenZoneIsMapped(_releaseDiscovery)
                ? ExpeditionZoneRefusal.None
                : ExpeditionZoneRefusal.OutsideChosenZone;
        }

        /// <summary>
        /// The discovery the release condition is read against.
        ///
        /// Held rather than passed to <see cref="MayTarget"/>, which is called from a launch path that
        /// has no reason to know about cartography - and set once by the owner, so there is one answer
        /// to "is the current zone finished" rather than one per caller.
        /// </summary>
        DiscoveryRuntime _releaseDiscovery;

        /// <summary>Hands the system the discovery its release condition reads. Called once by GameRuntime, beside the constructor.</summary>
        public void UseDiscovery(DiscoveryRuntime discovery) => _releaseDiscovery = discovery;

        /// <summary>
        /// Whether launching at this sector would commit the choice - true only while none has been made
        /// and the sector falls in a zone.
        ///
        /// Read by the screen so it can say what the click is about to cost before it is made. The lock
        /// is irreversible, and a lock arrived at as the side effect of an unannounced launch would be
        /// the worst way to meet it.
        /// </summary>
        public bool WouldChoose(int sectorIndex)
        {
            int zone = ZoneOfSector(sectorIndex);
            if (zone < 0) return false;

            return ChosenZone < 0 || (zone != ChosenZone && ChosenZoneIsMapped(_releaseDiscovery));
        }

        /// <summary>
        /// Commits the choice the launch at this sector implies. Does nothing once a zone is chosen, and
        /// nothing for ground no zone covers.
        ///
        /// Called by <c>MissionSystem.TryLaunch</c> after the launch is known to succeed - a refused
        /// launch must not lock five zones.
        /// </summary>
        public void ChooseByLaunch(int sectorIndex, DiscoveryRuntime discovery)
        {
            if (!WouldChoose(sectorIndex)) return;

            Choose(ZoneOfSector(sectorIndex), discovery);
        }

        /// <summary>
        /// The sector a first mission into this zone is aimed at: on the zone's own bearing, just past
        /// the inner edge.
        ///
        /// <b>Derived, and identical for all six by construction.</b> The zones are the same wedge turned
        /// six times, so their entry sectors sit at the same distance from the Core and a mission to any
        /// of them takes the same time - which is exactly what the first screen has to be able to say:
        /// what is shown is true and identical everywhere, because there is nothing yet to tell the
        /// directions apart.
        ///
        /// One sector out from the edge rather than on it, so the target is clear of the Core's own
        /// ground whatever rounding does.
        /// </summary>
        public int EntrySectorOf(int zoneIndex)
        {
            if (_grid == null || zoneIndex < 0 || zoneIndex >= ZoneCount) return -1;

            float radians = ZoneOf(zoneIndex).CentreDegrees * Mathf.Deg2Rad;
            float radius = InnerRadiusCells + _grid.SectorSizeCells;

            var cell = new GridCoord(
                Mathf.RoundToInt(_coreCentreCells.x + Mathf.Cos(radians) * radius),
                Mathf.RoundToInt(_coreCentreCells.y + Mathf.Sin(radians) * radius));

            return _grid.IndexAt(cell);
        }

        // ---- Content ----

        /// <summary>
        /// Everything the zone holds, in a fixed order, derived from the world seed and the zone's index.
        ///
        /// Identical whatever order the zones are asked about, and identical after a reload: nothing here
        /// consults anything mutable. The list is built once per zone and kept, so a caller may read it
        /// every frame.
        /// </summary>
        public IReadOnlyList<ExpeditionZoneSite> SitesOf(int zoneIndex)
        {
            if (zoneIndex < 0 || zoneIndex >= ZoneCount) return System.Array.Empty<ExpeditionZoneSite>();

            return _sites[zoneIndex] ?? (_sites[zoneIndex] = Derive(zoneIndex));
        }

        /// <summary>How many of the zone's hidden stock a field study could still turn up. Finite by construction: the stock is a fixed-length part of a derived list, not a draw repeated on demand.</summary>
        public int HiddenSitesLeft(int zoneIndex)
        {
            IReadOnlyList<ExpeditionZoneSite> sites = SitesOf(zoneIndex);

            int left = 0;
            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i].IsHidden && !sites[i].IsRevealed) left++;
            }

            return left;
        }

        /// <summary>
        /// Turns up the next hidden site, or null once the stock is spent - after which a field study
        /// finds only ground, which is exactly what keeps the zone bounded.
        /// </summary>
        public ExpeditionZoneSite RevealNextHiddenSite(int zoneIndex)
        {
            IReadOnlyList<ExpeditionZoneSite> sites = SitesOf(zoneIndex);

            for (int i = 0; i < sites.Count; i++)
            {
                if (sites[i].IsHidden && sites[i].Reveal()) return sites[i];
            }

            return null;
        }

        /// <summary>Marks a site as having given what it holds. Returns false for a site already consumed, so a caller can tell a first visit from a repeat.</summary>
        public bool Consume(int zoneIndex, int indexInZone)
        {
            IReadOnlyList<ExpeditionZoneSite> sites = SitesOf(zoneIndex);
            if (indexInZone < 0 || indexInZone >= sites.Count) return false;

            return sites[indexInZone].Consume();
        }

        List<ExpeditionZoneSite> Derive(int zone)
        {
            var sites = new List<ExpeditionZoneSite>();
            if (_settings == null) return sites;

            ExpeditionZone bounds = ZoneOf(zone);

            // The one place the composed zone differs from the five derived ones: how many of each kind
            // the ladders lay out. Everything below - where a site goes, which exploration carries the
            // secondary Core, what a hidden site is - is the same code on the same seed, because a
            // position cannot be authored for a slice the player has not picked yet.
            bool composed = IsComposed(zone);

            int prospections = composed
                ? Mathf.Max(0, _settings.FirstZoneProspections)
                : Mathf.Max(0, _settings.ProspectionSites);

            int reconnaissances = composed
                ? Mathf.Max(0, _settings.FirstZoneFieldStudies)
                : Mathf.Max(0, _settings.FieldStudySites);

            int explorations = composed
                ? Mathf.Max(0, _settings.FirstZoneFarExplorations)
                : Mathf.Max(0, _settings.FarExplorationSites);

            int recoveries = composed
                ? Mathf.Max(0, _settings.FirstZoneRecoveries)
                : DrawCount(zone, _settings.RecoverySitesMin, _settings.RecoverySitesMax, RecoveryCountSalt);

            int studies = composed
                ? Mathf.Max(0, _settings.FirstZoneCivilisationStudies)
                : DrawCount(zone, _settings.CivilisationStudiesMin, _settings.CivilisationStudiesMax, StudyCountSalt);

            int hidden = composed
                ? Mathf.Max(0, _settings.FirstZoneHiddenSites)
                : DrawCount(zone, _settings.HiddenSitesMin, _settings.HiddenSitesMax, HiddenCountSalt);

            // Exactly one far exploration carries the secondary Core site; every other one carries the
            // trace that puts the zone's civilisation study on the map. Written for any count rather
            // than for two, so raising farExplorationSites does not quietly leave the extras empty -
            // which is the very failure the design names.
            int carrier = explorations > 0 ? (int)(Hash(zone, CarrierSalt) % (uint)explorations) : -1;

            AddKind(sites, zone, bounds, ExpeditionSiteKind.Prospection, prospections, carrier);
            AddKind(sites, zone, bounds, ExpeditionSiteKind.EtudeDeTerrain, reconnaissances, carrier);
            AddKind(sites, zone, bounds, ExpeditionSiteKind.ExplorationLointaine, explorations, carrier);
            AddKind(sites, zone, bounds, ExpeditionSiteKind.Recuperation, recoveries, carrier);
            AddKind(sites, zone, bounds, ExpeditionSiteKind.EtudeCivilisation, studies, carrier);

            // The hidden stock last, so its members sit at the end of the list and a save's index into
            // it stays meaningful even if a visible count is retuned.
            //
            // A hidden site is a deposit or a salvage point, never one of the other two: a field study
            // turning up a far reconnaissance on the border makes no sense of the border, and one
            // turning up a civilisation study would hand the player a find they cannot act on for the
            // whole introduction. These are also exactly the two the imprecise survey's upper bound is
            // promising - "three to five deposits" is the stock, seen from the outside.
            for (int i = 0; i < hidden; i++)
            {
                uint salt = SiteSalt(HiddenKindSalt, i);
                ExpeditionSiteKind kind = Hash(zone, salt) % 2u == 0u
                    ? ExpeditionSiteKind.Prospection
                    : ExpeditionSiteKind.Recuperation;

                GridCoord cell = Place(bounds, kind, i, hidden, salt);

                sites.Add(new ExpeditionZoneSite(zone, sites.Count, kind, cell, ExpeditionFinding.None, true));
            }

            return sites;
        }

        void AddKind(List<ExpeditionZoneSite> sites, int zone, ExpeditionZone bounds,
            ExpeditionSiteKind kind, int count, int secondaryCoreCarrier)
        {
            for (int i = 0; i < count; i++)
            {
                uint salt = SiteSalt(BearingSalt + (uint)kind * 0x01000193u, i);
                GridCoord cell = Place(bounds, kind, i, count, salt);

                ExpeditionFinding finding = kind != ExpeditionSiteKind.ExplorationLointaine
                    ? ExpeditionFinding.None
                    : i == secondaryCoreCarrier
                        ? ExpeditionFinding.SiteNoyauSecondaire
                        : ExpeditionFinding.TraceCivilisation;

                sites.Add(new ExpeditionZoneSite(zone, sites.Count, kind, cell, finding, false));
            }
        }

        /// <summary>
        /// Where site <paramref name="index"/> of <paramref name="count"/> of one kind stands.
        ///
        /// <b>The jitter can never throw a site out of its own zone or its own band.</b> The even ladder
        /// is laid across the span *minus* the jitter on both sides, so what is drawn on top of it lands
        /// back inside by construction - rather than being clamped afterwards, which would pile sites up
        /// on a boundary and hide the fact that the settings had outgrown the geometry.
        /// </summary>
        GridCoord Place(ExpeditionZone bounds, ExpeditionSiteKind kind, int index, int count, uint salt)
        {
            float slice = SliceDegrees;
            float angleJitter = _settings.AngleJitterFraction * slice;
            float usableHalf = Mathf.Max(0f, bounds.HalfAngleDegrees - angleJitter);

            float ladder = count <= 0 ? 0f : ((index + 0.5f) / count * 2f - 1f) * usableHalf;
            float degrees = bounds.CentreDegrees + ladder + Signed(bounds.Index, salt, AngleChannel) * angleJitter;

            // A far reconnaissance may only be sent past the exploration threshold, so that is where its
            // site goes; everything else belongs to the near stretch, where a prospection may be sent.
            bool far = kind == ExpeditionSiteKind.ExplorationLointaine;
            float stretchInner = far ? FarStretchInner : bounds.InnerRadiusCells;
            float stretchOuter = far ? bounds.OuterRadiusCells : FarStretchInner;

            float radiusJitter = _settings.RadiusJitterCells;
            float usableInner = stretchInner + radiusJitter;
            float usableOuter = stretchOuter - radiusJitter;

            if (usableOuter < usableInner)
            {
                // The stretch is thinner than the jitter it was asked to carry - which is the ordinary
                // case for the far stretch, one Core radius deep. Everything centres on it and the
                // jitter shrinks to what fits, instead of a site landing in the wrong band.
                float middle = (stretchInner + stretchOuter) * 0.5f;
                usableInner = middle;
                usableOuter = middle;
                radiusJitter = Mathf.Max(0f, (stretchOuter - stretchInner) * 0.5f);
            }

            float radius = usableInner + (usableOuter - usableInner) * (count <= 0 ? 0.5f : (index + 0.5f) / count)
                + Signed(bounds.Index, salt, RadiusChannel) * radiusJitter;

            return CellAt(degrees, radius);
        }

        /// <summary>Keeps the bearing's draw and the radius's independent for one and the same site.</summary>
        const uint AngleChannel = 0u;
        const uint RadiusChannel = 0x7F4A7C15u;

        /// <summary>
        /// A value in [-1, 1), for a jitter that has to be symmetric about the ladder it sits on.
        ///
        /// <b>The zone is the mixer's index, and leaving it out was a real defect while this was
        /// written.</b> Drawing on the salt alone gave every zone the same jitter for the same kind and
        /// ordinal, so the six were exact rotations of one another - a world that looks varied from
        /// inside one zone and is a stencil from above.
        /// </summary>
        float Signed(int zone, uint salt, uint channel)
            => (float)(DeterministicHash.Unit(_seed, zone, salt + channel) * 2.0 - 1.0);

        /// <summary>
        /// Polar to a cell. Clamped to the map, which on a world large enough for the zones to fit never
        /// fires; a Core close to the edge of a small map would see sites pushed onto the border, and
        /// there is nothing better to do with them than to keep them reachable.
        /// </summary>
        GridCoord CellAt(float degrees, float radiusCells)
        {
            float radians = degrees * Mathf.Deg2Rad;

            int x = Mathf.RoundToInt(_coreCentreCells.x + Mathf.Cos(radians) * radiusCells);
            int y = Mathf.RoundToInt(_coreCentreCells.y + Mathf.Sin(radians) * radiusCells);

            int last = Mathf.Max(0, (_grid?.MapSizeCells ?? 1) - 1);
            return new GridCoord(Mathf.Clamp(x, 0, last), Mathf.Clamp(y, 0, last));
        }

        int DrawCount(int zone, int min, int max, uint salt)
        {
            int low = Mathf.Max(0, min);
            int high = Mathf.Max(low, max);

            return high == low ? low : low + (int)(Hash(zone, salt) % (uint)(high - low + 1));
        }

        static uint SiteSalt(uint baseSalt, int ordinal) => baseSalt + (uint)(ordinal + 1) * 0x9E3779B9u;

        uint Hash(int index, uint salt) => DeterministicHash.Mix(_seed, index, salt);

        // ---- Cartography ----

        /// <summary>
        /// How far a zone has been mapped, in <b>ground</b>: cells seen against cells there are.
        ///
        /// <b>Never in sites, and that is the trap this brick exists around.</b> A field study can add
        /// sites to a zone, so a bar drawn over a site count would go backwards the moment the player
        /// found something - the one thing a progress bar must never do. Over a fixed set of cells with
        /// discovery that only ever adds, monotonic stops being a property to test for and becomes one
        /// there is no way to break.
        ///
        /// Measured on demand and cached against <c>DiscoveryRuntime.Version</c>, which already exists
        /// to answer "has anything actually changed" - so reading this every frame costs one comparison.
        /// The measurement itself walks the bounding box of the outer disc once for all zones, which is
        /// what keeps the six of them from ever disagreeing about the partition. It allocates nothing.
        /// </summary>
        public ExpeditionZoneCartography CartographyOf(int zoneIndex, DiscoveryRuntime discovery)
        {
            if (zoneIndex < 0 || zoneIndex >= ZoneCount) return new ExpeditionZoneCartography(0, 0);

            Measure(discovery);
            return new ExpeditionZoneCartography(_discoveredCells[zoneIndex], _totalCells[zoneIndex]);
        }

        void Measure(DiscoveryRuntime discovery)
        {
            int version = discovery?.Version ?? 0;
            if (_measuredAtDiscoveryVersion == version && _measuredAtLayoutVersion == _layoutVersion) return;

            _measuredAtDiscoveryVersion = version;
            _measuredAtLayoutVersion = _layoutVersion;

            System.Array.Clear(_totalCells, 0, _totalCells.Length);
            System.Array.Clear(_discoveredCells, 0, _discoveredCells.Length);

            if (_grid == null || ZoneCount <= 0) return;

            float outer = OuterRadiusCells;
            int last = _grid.MapSizeCells - 1;
            if (last < 0) return;

            int minX = Mathf.Max(0, Mathf.FloorToInt(_coreCentreCells.x - outer));
            int maxX = Mathf.Min(last, Mathf.CeilToInt(_coreCentreCells.x + outer));
            int minY = Mathf.Max(0, Mathf.FloorToInt(_coreCentreCells.y - outer));
            int maxY = Mathf.Min(last, Mathf.CeilToInt(_coreCentreCells.y + outer));

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    // The cell's centre, the same convention DiscoveryRuntime.RevealDisc measures with -
                    // so a cell the Core revealed is a cell this counts.
                    int zone = ZoneAt(new Vector2(x + 0.5f, y + 0.5f));
                    if (zone < 0) continue;

                    _totalCells[zone]++;
                    if (discovery != null && discovery.IsDiscovered(new GridCoord(x, y))) _discoveredCells[zone]++;
                }
            }
        }

        // ---- Save / Restore (CONTRACTS.md §14) ----

        /// <summary>
        /// The choice, the frozen inner edge, and the sites whose state has moved off what the
        /// derivation gives. Everything else is a pure function of the world seed and comes back on its
        /// own.
        /// </summary>
        public JObject CaptureState()
        {
            var sites = new JArray();

            for (int zone = 0; zone < ZoneCount; zone++)
            {
                List<ExpeditionZoneSite> derived = _sites[zone];
                if (derived == null) continue;

                foreach (ExpeditionZoneSite site in derived)
                {
                    // Only what play has moved. A hidden site starts unrevealed and a visible one starts
                    // revealed, so "differs from its derived state" is the whole condition.
                    if (site.IsRevealed == !site.IsHidden && !site.IsConsumed) continue;

                    sites.Add(new JObject
                    {
                        ["z"] = site.ZoneIndex,
                        ["i"] = site.IndexInZone,
                        ["r"] = site.IsRevealed,
                        ["c"] = site.IsConsumed
                    });
                }
            }

            var surveyed = new JArray();
            for (int zone = 0; zone < _surveyed.Length; zone++)
            {
                if (_surveyed[zone]) surveyed.Add(zone);
            }

            return new JObject
            {
                ["chosen"] = ChosenZone,
                ["inner"] = InnerRadiusCells,
                ["sites"] = sites,
                ["surveyed"] = surveyed,
                ["highlightSpent"] = _highlightSpent
            };
        }

        /// <summary>
        /// Tolerant like every other Restore: a null or an absent key restores as a run where no zone has
        /// been chosen and nothing has been touched - which is exactly what a save from before the zones
        /// recorded.
        ///
        /// The chosen zone and the inner edge are restored <b>before</b> anything is re-derived: the
        /// first decides whether a zone gets the composed content or the derived one, the second is what
        /// every site's position and every cell count is measured from. Re-deriving in the wrong order
        /// would hand a reloaded run five derived zones and no composed one - the same hazard
        /// <see cref="Choose"/> guards on the live path.
        /// </summary>
        public void RestoreState(JObject state)
        {
            ChosenZone = -1;
            System.Array.Clear(_sites, 0, _sites.Length);
            System.Array.Clear(_surveyed, 0, _surveyed.Length);
            _highlightSpent = false;
            _layoutVersion++;

            if (state == null) return;

            int chosen = state.Value<int?>("chosen") ?? -1;
            ChosenZone = chosen >= 0 && chosen < ZoneCount ? chosen : -1;

            float? inner = state.Value<float?>("inner");
            if (inner.HasValue) InnerRadiusCells = Mathf.Max(0f, inner.Value);

            // Absent restores as not yet spent, which is the ordinary tolerant default: a save from
            // before the highlight comes from a run that was never shown one, so it is owed the once.
            _highlightSpent = state.Value<bool?>("highlightSpent") ?? false;

            // <b>An absent key restores the chosen zone as surveyed, not as unvisited.</b> The usual
            // tolerant default - "nothing has happened yet" - would be wrong here in the one way that
            // matters: a save written before this key comes from a build where choosing a zone showed
            // its sites, so restoring it as unsurveyed would take away what that run had already been
            // given. A run that has chosen nothing still surveys nothing.
            if (state["surveyed"] is JArray surveyed)
            {
                foreach (JToken token in surveyed) MarkSurveyed(token.Value<int?>() ?? -1);
            }
            else
            {
                MarkSurveyed(ChosenZone);
            }

            if (!(state["sites"] is JArray saved)) return;

            foreach (JToken token in saved)
            {
                if (!(token is JObject entry)) continue;

                int zone = entry.Value<int?>("z") ?? -1;
                int index = entry.Value<int?>("i") ?? -1;
                if (zone < 0 || zone >= ZoneCount) continue;

                IReadOnlyList<ExpeditionZoneSite> sites = SitesOf(zone);
                if (index < 0 || index >= sites.Count) continue;

                sites[index].RestoreState(
                    entry.Value<bool?>("r") ?? !sites[index].IsHidden,
                    entry.Value<bool?>("c") ?? false);
            }
        }
    }
}

using System.Collections.Generic;
using Game.Gameplay.Expeditions;
using Game.Gameplay.Missions;
using Game.Gameplay.Sectors;
using Game.Grid;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The zoomed-out map panel: where a mission's target is designated (SPEC_EXPEDITIONS.md §5.1).
    ///
    /// <b>Hovering never reveals what a robot has not reported.</b> An unreconnoitred sector shows its
    /// state and nothing else — no name, no risk, no available missions. That is not a display detail:
    /// the whole reason expeditions exist is that the Core is blind out there, and a tooltip that
    /// answered would make sending a robot pointless. The check is on the sector's discovery, and it
    /// is the first thing this class does with a hovered index.
    /// </summary>
    public sealed class SectorMapPanelController : MonoBehaviour
    {
        public const string PanelName = "sectormap";

        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        VisualElement _root;
        SectorMapElement _map;
        Label _hoverName;
        Label _hoverDetail;

        Label _targetName;
        Label _targetState;
        VisualElement _missionList;
        Label _fleet;

        VisualElement _crumbs;
        Label _zoneName;
        VisualElement _progressFill;
        Label _progress;
        VisualElement _siteCounts;
        Label _countsTitle;

        bool _bound;

        /// <summary>Reused each frame rather than allocated: this is rebuilt every Update and holds at most MaxConcurrentMissions entries.</summary>
        readonly List<int> _missionTargets = new List<int>();

        /// <summary>Reused for the same reason. A zone holds a dozen or so sites, and they are refilled every frame.</summary>
        readonly List<MapSiteMarker> _siteMarkers = new List<MapSiteMarker>();

        /// <summary>What the breadcrumb and the counts last said. Rebuilding a row per frame would allocate; comparing a string does not.</summary>
        string _crumbText;
        string _countsText;

        /// <summary>Which zone the view is framed on, or -1 at the whole-world scale. The breadcrumb's middle segment.</summary>
        int _framedZone = -1;

        /// <summary>Every kind, always in this order, so the same mission is always in the same place in the list.</summary>
        static readonly MissionKind[] Kinds =
        {
            MissionKind.Prospection,
            MissionKind.EtudeDeTerrain,
            MissionKind.ExplorationLointaine,
            MissionKind.Recuperation
        };

        void Start()
        {
            VisualElement panelRoot = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(panelRoot);
            panelRoot.StretchToParentSize();
            panelRoot.pickingMode = PickingMode.Ignore;

            _root = panelRoot.Q<VisualElement>("SectorMapPanelRoot");
            _hoverName = panelRoot.Q<Label>("SectorMapHoverName");
            _hoverDetail = panelRoot.Q<Label>("SectorMapHoverDetail");
            panelRoot.Q<Button>("SectorMapCloseButton").clicked += Hide;

            _targetName = panelRoot.Q<Label>("SectorMapTargetName");
            _targetState = panelRoot.Q<Label>("SectorMapTargetState");
            _missionList = panelRoot.Q<VisualElement>("SectorMapMissionList");
            _fleet = panelRoot.Q<Label>("SectorMapFleet");

            _crumbs = panelRoot.Q<VisualElement>("SectorMapCrumbs");
            _zoneName = panelRoot.Q<Label>("SectorMapZoneName");
            _progressFill = panelRoot.Q<VisualElement>("SectorMapProgressFill");
            _progress = panelRoot.Q<Label>("SectorMapProgress");
            _siteCounts = panelRoot.Q<VisualElement>("SectorMapSiteCounts");
            _countsTitle = panelRoot.Q<Label>("SectorMapCountsTitle");

            _map = new SectorMapElement();
            _map.HoveredSectorChanged += OnHoveredSectorChanged;
            _map.SelectedSectorChanged += _ => RenderTarget();
            _map.ZoneFramingRequested += FrameZoneAt;
            _map.SelectedZoneChanged += _ => RenderTarget();

            // The partition stays in the zone system; the element only asks.
            _map.ZoneResolver = cell => Zones != null ? Zones.ZoneAt(cell) : -1;
            panelRoot.Q<VisualElement>("SectorMapViewport").Add(_map);

            _root.EnableInClassList("hidden", true);
            gameRuntime.Selection.GlobalPanelChanged += OnGlobalPanelChanged;

            ShowNothingHovered();
        }

        void OnDestroy()
        {
            if (gameRuntime != null && gameRuntime.Selection != null)
                gameRuntime.Selection.GlobalPanelChanged -= OnGlobalPanelChanged;
        }

        void OnGlobalPanelChanged(string panelName)
        {
            bool visible = panelName == PanelName;
            _root.EnableInClassList("hidden", !visible);

            // While the map is up, ZQSD belongs to it. Set on the runtime rather than reached for by
            // the camera so it is cleared by the same event that closes the panel - a flag nobody
            // remembers to lower is a camera that never moves again.
            gameRuntime.KeyboardOwnedByPanel = visible;

            // Opening always finds the player, rather than wherever they last dragged to. A map that
            // opens somewhere unexpected costs a moment of "where am I" every single time. The aim
            // is dropped with it: a target chosen last time is a decision that has since gone stale.
            if (visible && Bind())
            {
                // Opens on the whole ring: the first thing to see is where one may go, not where one
                // already is. The Core's own ground is one click away on the breadcrumb.
                FrameWorld();
                _map.ClearSelection();
                _map.ClearZoneSelection();
                RenderTarget();
            }
        }

        void Hide()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            gameRuntime.Selection.CloseGlobalPanel();
        }

        /// <summary>Hands the element the map image once the world exists. Answers whether it is usable.</summary>
        bool Bind()
        {
            if (_bound) return true;
            if (gameRuntime.SectorMap == null || gameRuntime.Sectors == null || gameRuntime.World == null) return false;

            _map.Bind(gameRuntime.SectorMap, gameRuntime.Sectors.Columns,
                gameRuntime.Sectors.SectorSizeCells, gameRuntime.World.CoreCenterCells);

            _bound = true;
            return true;
        }

        void Update()
        {
            if (gameRuntime.Selection.ActiveGlobalPanel != PanelName) return;
            if (!Bind()) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Hide();
                return;
            }

            if (keyboard != null) _map.PanByKeyboard(PanDirection(keyboard), Time.unscaledDeltaTime);

            // The image rebuilds itself only when discovery moved, so this is a version comparison on
            // a still frame - see SectorMapImage. A rebuild can have brought a chunk into existence,
            // which is the only moment the element needs a child it does not already have.
            if (gameRuntime.SectorMap.Refresh()) _map.SyncTiles();
            _map.SetCoreRadius(gameRuntime.World.ActionRadiusCells);

            _missionTargets.Clear();
            if (gameRuntime.Missions != null)
            {
                foreach (MissionRuntime mission in gameRuntime.Missions.InFlight) _missionTargets.Add(mission.TargetSector);
            }
            _map.SetMissionTargets(_missionTargets);

            RenderZoneRing();
            RenderSites();
            RenderCrumbs();
            RenderCartography();
            RenderTarget();
        }

        // ---- The three scales ----

        ExpeditionZoneSystem Zones => gameRuntime.ExpeditionZones;

        Vector2 CoreCentre => gameRuntime.World.CoreCenterCells;

        /// <summary>
        /// The whole ring: the Core, its radius, the six zones. About twice the exploration threshold,
        /// which is where the zones stop meaning anything - a view of the entire world would be a speck
        /// in the middle of nothing.
        /// </summary>
        void FrameWorld()
        {
            _framedZone = -1;
            float reach = gameRuntime.MissionRange != null ? gameRuntime.MissionRange.ExplorationMinimumCells : 200f;
            _map.FrameCells(CoreCentre, reach * 2f);
        }

        /// <summary>The Core's own ground, at a scale the game camera cannot reach. Its radius is exactly what has to fit.</summary>
        void FrameCoreGround()
        {
            _framedZone = -1;
            float radius = Zones != null ? Zones.InnerRadiusCells : gameRuntime.World.ActionRadiusCells;
            _map.FrameCells(CoreCentre, Mathf.Max(1f, radius));
        }

        /// <summary>
        /// One zone. Framed on the middle of its own wedge, with a radius that fits whichever of its two
        /// extents is the larger - a 60° slice is wider across its arc than it is deep, and framing on
        /// the depth alone would cut both its sides off.
        /// </summary>
        void FrameZone(int zone)
        {
            if (Zones == null || zone < 0 || zone >= Zones.ZoneCount) return;

            _framedZone = zone;

            ExpeditionZone bounds = Zones.ZoneOf(zone);
            float midRadius = (bounds.InnerRadiusCells + bounds.OuterRadiusCells) * 0.5f;
            float radians = bounds.CentreDegrees * Mathf.Deg2Rad;

            var centre = CoreCentre + new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * midRadius;
            float halfDepth = (bounds.OuterRadiusCells - bounds.InnerRadiusCells) * 0.5f;
            float halfArc = midRadius * Mathf.Sin(bounds.HalfAngleDegrees * Mathf.Deg2Rad);

            _map.FrameCells(centre, Mathf.Max(halfDepth, halfArc));
        }

        /// <summary>A click at the whole-world scale: the zone it landed in becomes the frame. Off the ring, it takes the player back to their own ground.</summary>
        void FrameZoneAt(Vector2 cellPosition)
        {
            int zone = Zones != null ? Zones.ZoneAt(cellPosition) : -1;

            if (zone >= 0) FrameZone(zone);
            else FrameCoreGround();
        }

        // ---- The breadcrumb ----

        /// <summary>
        /// `Monde › Zone nord-est › Cratère de Suie`. The earlier segments reframe; the last is where one
        /// is, so it leads nowhere.
        ///
        /// Rebuilt only when the text actually changes - a row of buttons per frame would allocate for
        /// nothing, and this is asked every frame.
        /// </summary>
        void RenderCrumbs()
        {
            string zoneSegment = _framedZone >= 0 ? ZoneName(_framedZone) : null;
            string placeSegment = PlaceName();
            string text = $"{zoneSegment}|{placeSegment}";
            if (text == _crumbText) return;

            _crumbText = text;
            _crumbs.Clear();

            bool leafIsPlace = placeSegment != null;
            AddCrumb("Monde", inert: _framedZone < 0 && !leafIsPlace, FrameWorld);

            if (zoneSegment != null)
            {
                AddSeparator();
                AddCrumb(zoneSegment, inert: !leafIsPlace, () => FrameZone(_framedZone));
            }

            if (leafIsPlace)
            {
                AddSeparator();
                AddCrumb(placeSegment, inert: true, null);
            }
        }

        void AddCrumb(string text, bool inert, System.Action reframe)
        {
            if (inert || reframe == null)
            {
                var label = new Label(text);
                label.AddToClassList("sector-map-crumb-current");
                _crumbs.Add(label);
                return;
            }

            var button = new Button(() => reframe()) { text = text };
            button.AddToClassList("sector-map-crumb");
            _crumbs.Add(button);
        }

        void AddSeparator()
        {
            var separator = new Label("›");
            separator.AddToClassList("sector-map-crumb-separator");
            _crumbs.Add(separator);
        }

        /// <summary>The aimed sector's name, or nothing. Only a reconnoitred sector has one to give.</summary>
        string PlaceName()
        {
            int sector = _map.SelectedSector;
            if (sector < 0 || gameRuntime.SectorCatalog == null) return null;
            if (gameRuntime.Sectors.IsWhollyUnknown(sector, gameRuntime.Discovery)) return null;

            return gameRuntime.SectorCatalog.NameOf(sector);
        }

        /// <summary>
        /// A zone's name is its bearing - "Zone nord-est". Taken from the eight-point compass rather
        /// than from a list of six, so changing the zone count renames them instead of running out.
        /// </summary>
        static readonly string[] CompassPoints =
        {
            "est", "nord-est", "nord", "nord-ouest", "ouest", "sud-ouest", "sud", "sud-est"
        };

        string ZoneName(int zone)
        {
            if (Zones == null || zone < 0 || zone >= Zones.ZoneCount) return string.Empty;

            float degrees = Mathf.Repeat(Zones.ZoneOf(zone).CentreDegrees, 360f);
            int point = Mathf.RoundToInt(degrees / 45f) % CompassPoints.Length;
            return "Zone " + CompassPoints[point];
        }

        // ---- The layers the zones own ----

        void RenderZoneRing()
        {
            if (Zones == null) return;

            _map.SetZoneRing(Zones.InnerRadiusCells, Zones.OuterRadiusCells, Zones.ZoneCount, Zones.ChosenZone);
        }

        /// <summary>The colour of each kind of site. Here rather than in the element, beside the label it goes with, so a kind is named and coloured in one place.</summary>
        static Color TintOf(ExpeditionSiteKind kind)
        {
            switch (kind)
            {
                case ExpeditionSiteKind.Prospection: return new Color(0.937f, 0.624f, 0.153f, 1f);
                case ExpeditionSiteKind.EtudeDeTerrain: return new Color(0.333f, 0.867f, 0.961f, 1f);
                case ExpeditionSiteKind.ExplorationLointaine: return new Color(0.62f, 0.50f, 0.83f, 1f);
                case ExpeditionSiteKind.Recuperation: return new Color(0.44f, 0.78f, 0.51f, 1f);
                default: return new Color(0.55f, 0.58f, 0.62f, 1f);
            }
        }

        static string LabelOf(ExpeditionSiteKind kind)
        {
            switch (kind)
            {
                case ExpeditionSiteKind.Prospection: return "Prospection";
                case ExpeditionSiteKind.EtudeDeTerrain: return "Étude de terrain";
                case ExpeditionSiteKind.ExplorationLointaine: return "Exploration lointaine";
                case ExpeditionSiteKind.Recuperation: return "Récupération";
                default: return "Étude de civilisation";
            }
        }

        /// <summary>A site needing units. It is placed and shown from the start, locked - a mission that promises reads better than one that appears from nowhere.</summary>
        static bool NeedsUnits(ExpeditionSiteKind kind) => kind == ExpeditionSiteKind.EtudeCivilisation;

        /// <summary>
        /// The zone's sites, in the terms the map draws them in. Only the chosen zone's: the other five
        /// have no terrain shown and nothing to offer, and drawing their sites would invite a click that
        /// is refused.
        /// </summary>
        void RenderSites()
        {
            _siteMarkers.Clear();

            if (Zones != null && Zones.ChosenZone >= 0)
            {
                foreach (ExpeditionZoneSite site in Zones.SitesOf(Zones.ChosenZone))
                {
                    if (!site.IsRevealed) continue;

                    MapSiteState state =
                        site.IsConsumed ? MapSiteState.Done :
                        NeedsUnits(site.Kind) ? MapSiteState.Locked :
                                                MapSiteState.Available;

                    _siteMarkers.Add(new MapSiteMarker(
                        new Vector2(site.Cell.X + 0.5f, site.Cell.Y + 0.5f),
                        state, TintOf(site.Kind), LabelOf(site.Kind)));
                }
            }

            _map.SetSites(_siteMarkers);
        }

        /// <summary>
        /// How far the zone has been mapped, and what it still holds.
        ///
        /// <b>In surface, never in sites.</b> A field study adds sites, so a bar over a site count would
        /// go backwards the moment the player found something - see ExpeditionZoneSystem.CartographyOf.
        /// The counts beside it are a different statement: how much of what kind is left.
        /// </summary>
        void RenderCartography()
        {
            // The zone being *framed* when none has been chosen yet, so the pane describes what the
            // player is looking at. Saying "aucune zone choisie" while the breadcrumb read "Zone est"
            // put the same word on two different things one line apart.
            int zone = Zones == null ? -1 : Zones.ChosenZone >= 0 ? Zones.ChosenZone : _framedZone;

            if (zone < 0)
            {
                _zoneName.text = "—";
                _progress.text = "Aucune zone cadrée";
                _progressFill.style.width = new StyleLength(Length.Percent(0f));
                if (_countsText != null) { _siteCounts.Clear(); _countsText = null; }
                return;
            }

            // Chosen and framed are two states, and only one of them locks the other five. Said once,
            // here, rather than left for the player to infer from a refusal further down.
            _zoneName.text = Zones.ChosenZone == zone ? ZoneName(zone) : ZoneName(zone) + " · non choisie";

            ExpeditionZoneCartography mapped = Zones.CartographyOf(zone, gameRuntime.Discovery);
            _progressFill.style.width = new StyleLength(Length.Percent(mapped.Ratio * 100f));
            _progress.text = $"{mapped.Ratio * 100f:0.0} %".Replace('.', ',');

            // <b>An unchosen zone gives no counts.</b> The six are equivalent until a robot has been,
            // and a tally would say which direction is richest - the one thing the design refuses to
            // answer before the choice is made.
            if (Zones.ChosenZone != zone)
            {
                if (_countsText != null) { _siteCounts.Clear(); _countsText = null; }
                _countsTitle.text = "SITES CONNUS";
                return;
            }

            // The whole ring and one zone are different questions. Up there the player is choosing a
            // direction, so what matters is how much is left to do at all; inside a zone they are
            // choosing a target, so what matters is what kind of thing is left and where.
            bool wholeRing = _map.ShowsWholeRing;
            _countsTitle.text = wholeRing ? "MISSIONS DE LA ZONE" : "SITES CONNUS";

            if (wholeRing) RenderMissionStates(zone);
            else RenderSiteCounts(zone);
        }

        /// <summary>
        /// At the whole-ring scale: how many missions are launchable, how many are done, and how many
        /// need units - the third on its own line and never in the first, since a count that mixed them
        /// would promise something the whole introduction cannot deliver.
        /// </summary>
        void RenderMissionStates(int zone)
        {
            int available = 0;
            int done = 0;
            int locked = 0;

            foreach (ExpeditionZoneSite site in Zones.SitesOf(zone))
            {
                if (!site.IsRevealed) continue;

                if (site.IsConsumed) done++;
                else if (NeedsUnits(site.Kind)) locked++;
                else available++;
            }

            string text = $"states;{available};{done};{locked}";
            if (text == _countsText) return;

            _countsText = text;
            _siteCounts.Clear();

            _siteCounts.Add(BuildStateRow("Disponibles", available, new Color(0.333f, 0.867f, 0.961f, 1f), false));
            _siteCounts.Add(BuildStateRow("Faites", done, new Color(0.42f, 0.45f, 0.5f, 1f), false));
            _siteCounts.Add(BuildStateRow("Nécessitent des unités", locked, new Color(0.45f, 0.48f, 0.53f, 0.75f), true));
        }

        VisualElement BuildStateRow(string label, int count, Color tint, bool muted)
        {
            var row = new VisualElement();
            row.AddToClassList("sector-map-count-row");
            if (muted) row.AddToClassList("sector-map-count-locked");

            var dot = new VisualElement();
            dot.AddToClassList("sector-map-count-dot");
            dot.style.backgroundColor = tint;
            row.Add(dot);

            var name = new Label(label);
            name.AddToClassList("sector-map-count-name");
            row.Add(name);

            var value = new Label(count.ToString());
            value.AddToClassList("sector-map-count-value");
            value.AddToClassList("mono-value");
            row.Add(value);

            return row;
        }

        /// <summary>
        /// The count by kind, which is what makes a zone legible without hovering every point.
        ///
        /// <b>What needs units is a line of its own and never joins the others.</b> A count that
        /// included it would say a mission is available when it cannot be launched for the whole of the
        /// introduction.
        /// </summary>
        void RenderSiteCounts(int zone)
        {
            var tally = new int[SiteKinds.Length];
            var done = new int[SiteKinds.Length];

            foreach (ExpeditionZoneSite site in Zones.SitesOf(zone))
            {
                if (!site.IsRevealed) continue;

                int slot = System.Array.IndexOf(SiteKinds, site.Kind);
                if (slot < 0) continue;

                if (site.IsConsumed) done[slot]++;
                else tally[slot]++;
            }

            var text = new System.Text.StringBuilder();
            for (int i = 0; i < SiteKinds.Length; i++) text.Append(tally[i]).Append('/').Append(done[i]).Append(';');
            if (text.ToString() == _countsText) return;

            _countsText = text.ToString();
            _siteCounts.Clear();

            for (int i = 0; i < SiteKinds.Length; i++)
            {
                if (tally[i] == 0 && done[i] == 0) continue;
                _siteCounts.Add(BuildCountRow(SiteKinds[i], tally[i], done[i]));
            }
        }

        static readonly ExpeditionSiteKind[] SiteKinds =
        {
            ExpeditionSiteKind.Prospection,
            ExpeditionSiteKind.EtudeDeTerrain,
            ExpeditionSiteKind.ExplorationLointaine,
            ExpeditionSiteKind.Recuperation,
            ExpeditionSiteKind.EtudeCivilisation
        };

        VisualElement BuildCountRow(ExpeditionSiteKind kind, int left, int done)
        {
            var row = new VisualElement();
            row.AddToClassList("sector-map-count-row");
            if (NeedsUnits(kind)) row.AddToClassList("sector-map-count-locked");

            var dot = new VisualElement();
            dot.AddToClassList("sector-map-count-dot");
            dot.style.backgroundColor = NeedsUnits(kind) ? new Color(0.45f, 0.48f, 0.53f, 0.75f) : TintOf(kind);
            row.Add(dot);

            var name = new Label(NeedsUnits(kind) ? LabelOf(kind) + " · unités" : LabelOf(kind));
            name.AddToClassList("sector-map-count-name");
            row.Add(name);

            var value = new Label(done > 0 ? $"{left} · {done} faits" : left.ToString());
            value.AddToClassList("sector-map-count-value");
            value.AddToClassList("mono-value");
            row.Add(value);

            return row;
        }

        // ---- The aimed sector, and what may be sent to it ----

        /// <summary>
        /// Rebuilds the side pane for whatever is currently aimed at.
        ///
        /// Called on selection and every frame, because a refusal is not a property of the target: a
        /// slot frees when a mission lands, a robot's last charge is spent by the launch before it.
        /// A button that was enabled when the sector was picked would otherwise stay enabled after
        /// the reason for it went away.
        /// </summary>
        void RenderTarget()
        {
            if (_targetName == null || gameRuntime.Missions == null) return;

            _fleet.text = FleetLine();

            // <b>Before a zone is chosen the target is a direction, not a square.</b> There is nothing
            // to tell one sector from another out there, so asking the player to pick one would be
            // asking a question the map cannot answer yet. The zone's entry sector is what the mission
            // is actually aimed at, and it is the same distance in all six - which is why what the
            // panel says here is true and identical everywhere.
            if (_map.AimsAtZones)
            {
                RenderZoneTarget();
                return;
            }

            int sector = _map.SelectedSector;

            if (sector < 0)
            {
                _targetName.text = "Aucune cible";
                _targetState.text = "Cliquez un secteur sur la carte.";
                _missionList.Clear();
                return;
            }

            bool unknown = gameRuntime.Sectors.IsWhollyUnknown(sector, gameRuntime.Discovery);

            // Same rule as the hover, and for the same reason: what a robot has not reported is not
            // the Core's to know. A target can be aimed at without being described.
            _targetName.text = unknown ? "Secteur non reconnu" : gameRuntime.SectorCatalog.NameOf(sector);
            _targetState.text = unknown
                ? "Aucun robot n'y est allé."
                : $"Risque estimé : {RiskLabel(gameRuntime.SectorCatalog.RiskOf(sector))}";

            // In words as well as on the map: the amber outline says where, this says how long.
            string underway = MissionUnderwayTo(sector);
            if (underway != null) _targetState.text += "\n" + underway;

            RenderMissions(sector);
        }

        /// <summary>
        /// The first screen's answer: a direction, and what one mission into it would cost.
        ///
        /// <b>What is shown is true and identical everywhere</b>, because nothing yet distinguishes the
        /// six - no robot has been. Saying anything more would be inventing knowledge the Core does not
        /// have, which is the one thing this panel refuses to do.
        /// </summary>
        void RenderZoneTarget()
        {
            int zone = _map.SelectedZone;

            if (zone < 0)
            {
                _targetName.text = "Aucune direction";
                _targetState.text = "Survolez une zone, puis choisissez-en une.";
                _missionList.Clear();
                return;
            }

            _targetName.text = ZoneName(zone);
            _targetState.text = "Aucune donnée. Les six directions se valent tant qu'aucun robot n'y est allé.";

            int entry = Zones.EntrySectorOf(zone);
            if (entry < 0)
            {
                _missionList.Clear();
                return;
            }

            RenderMissions(entry);
        }

        void RenderMissions(int sector)
        {
            _missionList.Clear();

            float radius = gameRuntime.World.ActionRadiusCells;
            foreach (MissionKind kind in Kinds)
            {
                MissionSystem.LaunchRefusal refusal = gameRuntime.Missions.CanLaunch(kind, sector, radius);

                // A kind the target's distance simply does not admit is left out rather than listed
                // as impossible: the bands are a property of where you are pointing, not a fault to
                // report, and three greyed rows on every sector would drown the one that can go.
                if (refusal == MissionSystem.LaunchRefusal.WrongBand
                    || refusal == MissionSystem.LaunchRefusal.NotASector) continue;

                _missionList.Add(BuildMissionRow(kind, sector, refusal));
            }

            // <b>Said before the click, never after.</b> The first launch picks the zone it goes into
            // and locks the other five, and that is irreversible - a lock met as the unannounced side
            // effect of pressing "Lancer" would be the worst possible way to learn the rule.
            if (_missionList.childCount != 0 && Zones != null && Zones.WouldChoose(sector))
            {
                var warning = new Label(
                    $"Lancer ici choisit la {ZoneName(Zones.ZoneOfSector(sector)).ToLowerInvariant()} "
                    + "et verrouille les cinq autres.");
                warning.AddToClassList("sector-map-lock-warning");
                _missionList.Add(warning);
            }

            if (_missionList.childCount != 0) return;

            var none = new Label("Aucune mission possible sur cette cible.");
            none.AddToClassList("sector-map-mission-none");
            _missionList.Add(none);
        }

        VisualElement BuildMissionRow(MissionKind kind, int sector, MissionSystem.LaunchRefusal refusal)
        {
            var row = new VisualElement();
            row.AddToClassList("sector-map-mission-row");

            var name = new Label(KindLabel(kind));
            name.AddToClassList("sector-map-mission-name");
            row.Add(name);

            // Always "estimee". SPEC_EXPEDITIONS.md 5.2 forbids ever presenting a mission's numbers
            // as certainties - the duration is the one figure shown at all, and it is a forecast.
            var duration = new Label($"~ {FormatDuration(gameRuntime.Missions.DurationOf(kind, sector))}");
            duration.AddToClassList("sector-map-mission-duration");
            row.Add(duration);

            if (refusal == MissionSystem.LaunchRefusal.None)
            {
                var launch = new Button(() => Launch(kind, sector)) { text = "Lancer" };
                launch.AddToClassList("sector-map-mission-launch");
                row.Add(launch);
            }
            else
            {
                var why = new Label(RefusalLabel(refusal));
                why.AddToClassList("sector-map-mission-refusal");
                row.Add(why);
            }

            return row;
        }

        void Launch(MissionKind kind, int sector)
        {
            gameRuntime.Missions.TryLaunch(kind, sector, gameRuntime.World.ActionRadiusCells, out _);
            RenderTarget();
        }

        /// <summary>What is already on its way to this sector, or null when nothing is. Names the kind, because two different missions to one place are two different questions.</summary>
        string MissionUnderwayTo(int sector)
        {
            if (gameRuntime.Missions == null) return null;

            foreach (MissionRuntime mission in gameRuntime.Missions.InFlight)
            {
                if (mission.TargetSector != sector) continue;
                return $"{KindLabel(mission.Kind)} en cours · {FormatDuration(mission.RemainingSeconds)}";
            }
            return null;
        }

        /// <summary>What the fleet has left, which is the one number that decides whether any of this is possible at all.</summary>
        string FleetLine()
        {
            MissionSystem missions = gameRuntime.Missions;
            if (missions == null || !missions.RobotsHaveAppeared) return string.Empty;

            return $"{missions.InFlight.Count}/{missions.MaxConcurrentMissions} en cours · {missions.TotalChargesLeft} charges";
        }

        static string KindLabel(MissionKind kind)
        {
            switch (kind)
            {
                case MissionKind.Prospection: return "Prospection";
                case MissionKind.EtudeDeTerrain: return "Étude de terrain";
                case MissionKind.ExplorationLointaine: return "Exploration lointaine";
                default: return "Récupération";
            }
        }

        static string RefusalLabel(MissionSystem.LaunchRefusal refusal)
        {
            switch (refusal)
            {
                case MissionSystem.LaunchRefusal.RobotsHaveNotArrived: return "aucun robot";
                case MissionSystem.LaunchRefusal.AllSlotsBusy: return "emplacements occupés";
                case MissionSystem.LaunchRefusal.NoRobotAvailable: return "robots épuisés";
                case MissionSystem.LaunchRefusal.AlreadyReconnoitred: return "déjà reconnu";
                case MissionSystem.LaunchRefusal.NotYetReconnoitred: return "à reconnaître d'abord";
                case MissionSystem.LaunchRefusal.NothingToRecover: return "rien à récupérer";
                case MissionSystem.LaunchRefusal.AlreadyRecovered: return "déjà récupéré";

                // Not an interface for the zones - that is its own chantier. These two only keep this
                // switch from answering "impossible" to a refusal that has a reason worth reading.
                case MissionSystem.LaunchRefusal.OutsideChosenZone: return "hors de la zone choisie";
                default: return "impossible";
            }
        }

        /// <summary>Minutes and seconds, because a mission is minutes long and a bare count of seconds stops being readable past a hundred or so.</summary>
        static string FormatDuration(float seconds)
        {
            int whole = Mathf.RoundToInt(seconds);
            return whole < 60 ? $"{whole} s" : $"{whole / 60} min {whole % 60:00} s";
        }

        /// <summary>Same AZERTY layout the world camera uses, so the keys mean the same thing whichever is listening.</summary>
        static Vector2 PanDirection(Keyboard keyboard)
        {
            var move = Vector2.zero;
            if (keyboard.zKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.qKey.isPressed) move.x -= 1f;
            return move;
        }

        // ---- Hover ----

        void OnHoveredSectorChanged(int sector)
        {
            if (sector < 0)
            {
                ShowNothingHovered();
                return;
            }

            SectorGrid grid = gameRuntime.Sectors;

            // The one rule this panel exists to hold. Everything below the check is information a
            // robot brought back; above it, there is nothing to say but the state.
            if (grid.IsWhollyUnknown(sector, gameRuntime.Discovery))
            {
                _hoverName.text = "Secteur non reconnu";
                _hoverDetail.text = "Aucun robot n'y est allé.";
                return;
            }

            _hoverName.text = gameRuntime.SectorCatalog.NameOf(sector);
            _hoverDetail.text = $"Risque estimé : {RiskLabel(gameRuntime.SectorCatalog.RiskOf(sector))}";
        }

        /// <summary>
        /// Nothing under the pointer. The footer's own hint line says what the map does, so this says
        /// nothing at all rather than repeating it - the two were printed one above the other.
        /// </summary>
        void ShowNothingHovered()
        {
            _hoverName.text = string.Empty;
            _hoverDetail.text = string.Empty;
        }

        /// <summary>Qualitative, and always qualified as an estimate — §5.2 forbids ever showing a number.</summary>
        static string RiskLabel(SectorRisk risk)
        {
            switch (risk)
            {
                case SectorRisk.Low: return "faible";
                case SectorRisk.Moderate: return "modéré";
                case SectorRisk.High: return "élevé";
                default: return "critique";
            }
        }
    }
}

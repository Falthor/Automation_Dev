using System;
using System.Collections.Generic;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The zoomed-out map: one quad per discovered chunk, zoomable and pannable, with the Core and its
    /// action radius drawn over it.
    ///
    /// <b>There is no map underneath the tiles.</b> Nothing is drawn where nothing is discovered, so
    /// the black is the panel showing through - the absence of a map rather than a dark surface
    /// somebody painted. It is what gives the revealed patches their weight and makes the gap between
    /// them read as something to close.
    ///
    /// <b>The tiles are the whole terrain layer, and only the view moves.</b> Each holds one texel per
    /// cell of one chunk (16 KB), created when that chunk is first discovered; zooming and panning cost
    /// a rectangle per tile - a handful during the introduction - and no re-upload.
    ///
    /// <b>Zoom is not a nicety here, it is what makes the map readable at all.</b> Showing 10 000
    /// cells at once in a 900-pixel panel is 11 cells per pixel: the Core's radius would be under
    /// three pixels and the whole area a mission can reach about twenty-five. The player would look
    /// at a screen of fog with a speck in it. So the panel opens on the Core's neighbourhood and zooms
    /// out to the whole world only if asked.
    ///
    /// The Core marker and the radius ring are drawn in Painter2D rather than imported — the same
    /// gesture as `HatchFillElement` and `ClockGlyphElement`, and for the same reasons: exact at any
    /// size, no texture to maintain, no lifetime to manage.
    /// </summary>
    public sealed class SectorMapElement : VisualElement
    {
        /// <summary>
        /// Pixels per sector at the closest zoom.
        ///
        /// Three times what it was. A sector is not decoration on this map, it is the thing a mission
        /// is aimed at, and the scale has to be set by what stays comfortably clickable rather than by
        /// how much world fits. At the old sizes a sector shrank to a few pixels well before the map
        /// stopped being useful to read, so the two purposes fought each other.
        /// </summary>
        public const float MaxPixelsPerSector = 144f;

        /// <summary>Pixels per sector when the whole world is asked for. Three pixels is the floor because a target has to be hittable, not because there is nothing left to draw.</summary>
        public const float MinPixelsPerSector = 3f;

        /// <summary>What the panel opens on: close enough that the mission-reachable ring fills a good part of the view.</summary>
        public const float DefaultPixelsPerSector = 48f;

        /// <summary>
        /// How much one notch of the wheel changes the zoom. 8% rather than the 20% this started at:
        /// a map is read by creeping up on a scale, and at 20% two notches nearly halved the view,
        /// so finding a comfortable one meant overshooting and coming back.
        /// </summary>
        const float ZoomStep = 1.08f;

        /// <summary>Sectors crossed per second by the keyboard pan, at the default zoom. Scaled by the zoom so the map moves at a constant speed on screen rather than a constant speed in sectors.</summary>
        const float KeyboardPanSectorsPerSecond = 24f;

        static readonly Color CoreColour = new Color(0.85f, 0.45f, 0.95f, 1f);
        static readonly Color RadiusColour = new Color(0.333f, 0.867f, 0.961f, 0.85f);
        static readonly Color HoverColour = new Color(1f, 1f, 1f, 0.9f);

        /// <summary>The aimed sector. The panel's accent, and thicker than the hover: one is a glance, the other a decision.</summary>
        static readonly Color SelectionColour = new Color(0.333f, 0.867f, 0.961f, 1f);

        /// <summary>A sector a robot is on its way to. The amber this project already uses for "under way" - the research row in progress wears the same.</summary>
        static readonly Color MissionColour = new Color(0.937f, 0.624f, 0.153f, 1f);

        /// <summary>
        /// The zone separators. Deliberately dim: their job is to show that five other directions exist,
        /// not to compete with the one being worked.
        /// </summary>
        /// <summary>The base's own colours: everything that transforms or holds in blue, everything that carries in white.</summary>
        static readonly Color BuildingColour = new Color(0.30f, 0.52f, 0.93f, 1f);
        static readonly Color BeltColour = new Color(0.92f, 0.94f, 0.97f, 1f);

        /// <summary>Below this the base is not drawn at all - see DrawBuildings. One pixel per cell is the point at which a footprint is a shape rather than a speck.</summary>
        const float MinPixelsPerCellForBuildings = 1f;

        static readonly Color SeparatorColour = new Color(1f, 1f, 1f, 0.13f);

        /// <summary>The chosen zone's own two separators, a shade up from the rest so the worked slice reads without being announced.</summary>
        static readonly Color ChosenSeparatorColour = new Color(0.333f, 0.867f, 0.961f, 0.35f);

        /// <summary>A zone under the pointer while none has been chosen. Faint: it answers "this one", not "take this one".</summary>
        static readonly Color ZoneHoverFill = new Color(1f, 1f, 1f, 0.05f);

        /// <summary>The zone picked. The panel's own accent, because this is a decision rather than a glance.</summary>
        static readonly Color ZoneSelectedFill = new Color(0.333f, 0.867f, 0.961f, 0.10f);

        /// <summary>A site that has given what it held. Sourd, and never clickable - it stays as the trace of what was done there.</summary>
        static readonly Color DoneColour = new Color(0.42f, 0.45f, 0.5f, 1f);

        /// <summary>A site needing units. An empty circle: it promises rather than offers.</summary>
        static readonly Color LockedColour = new Color(0.45f, 0.48f, 0.53f, 0.75f);

        /// <summary>
        /// Holds one child per discovered chunk. <b>There is no map underneath them</b> - the black is
        /// the panel showing through, which is what makes an unexplored world read as absence rather
        /// than as a dark surface someone drew.
        /// </summary>
        readonly VisualElement _terrain = new VisualElement();

        /// <summary>Reused across rebuilds so panning and zooming allocate nothing. Only ever grows: a chunk once discovered stays discovered.</summary>
        readonly List<VisualElement> _tileViews = new List<VisualElement>();

        readonly VisualElement _overlay = new VisualElement();

        SectorMapImage _image;
        int _sizeSectors = 1;
        int _sectorSizeCells = 16;

        Vector2 _coreCentreCells;
        float _coreRadiusCells;

        /// <summary>Which sector the pointer is over, or -1. Read by the panel to fill its tooltip.</summary>
        public int HoveredSector { get; private set; } = -1;

        /// <summary>The sector a mission would be aimed at, or -1. Survives the pointer leaving, unlike the hover: it is a decision, not a glance.</summary>
        public int SelectedSector { get; private set; } = -1;

        /// <summary>Sectors a robot is currently on its way to. Marked so the player can see at a glance where the fleet already is, without reading a list.</summary>
        readonly List<int> _missionTargets = new List<int>();

        /// <summary>What the zones look like on the ground. Handed in rather than computed, so the one partition lives in ExpeditionZoneSystem and the map only draws it.</summary>
        float _zoneInnerRadiusCells;
        float _zoneOuterRadiusCells;
        int _zoneCount;
        int _chosenZone = -1;

        readonly List<MapSiteMarker> _sites = new List<MapSiteMarker>();

        /// <summary>Every cell the player's base stands on. The one thing on this map that is already theirs.</summary>
        readonly List<MapBuildingCell> _buildings = new List<MapBuildingCell>();

        /// <summary>The one label shown without hovering, for the site put forward. Painter2D draws no text, so it is a child element parked over the mark.</summary>
        readonly Label _highlightLabel = new Label();

        /// <summary>The site under the pointer, or -1. An index into the list the panel handed over.</summary>
        public int HoveredSite { get; private set; } = -1;

        /// <summary>The site aimed at, or -1. Survives the pointer leaving: it is a decision, not a glance.</summary>
        public int SelectedSite { get; private set; } = -1;

        public event Action<int> SelectedSiteChanged;

        /// <summary>
        /// How near the pointer has to be to a site's mark, in pixels, to be pointing at it.
        ///
        /// A little wider than the mark itself: the marks are small at every scale the map reaches, and
        /// a target that has to be hit exactly is a target the player fights.
        /// </summary>
        const float SitePickRadiusPixels = 13f;

        public event Action<int> HoveredSectorChanged;

        public event Action<int> SelectedSectorChanged;

        /// <summary>
        /// A click at the whole-world scale, in cell space. <b>A shortcut for framing, never a change of
        /// screen</b> - the panel turns it into a zone and reframes on it. The element does not decide
        /// which zone: the partition belongs to <c>ExpeditionZoneSystem</c> and a second copy of it here
        /// is exactly the kind of duplicate that stops agreeing.
        /// </summary>
        public event Action<Vector2> ZoneFramingRequested;

        /// <summary>The zone the pointer is over while none has been chosen, or -1.</summary>
        public int HoveredZone { get; private set; } = -1;

        /// <summary>The zone aimed at while none has been chosen, or -1. What the side pane offers its first mission for.</summary>
        public int SelectedZone { get; private set; } = -1;

        public event Action<int> SelectedZoneChanged;

        /// <summary>
        /// Answers which zone a point in cell space falls in. Set by the panel, because the partition
        /// belongs to <c>ExpeditionZoneSystem</c> - a second copy here is the duplicate that stops
        /// agreeing.
        /// </summary>
        public Func<Vector2, int> ZoneResolver { get; set; }

        /// <summary>
        /// Whether the map is aiming at whole zones rather than at squares inside one.
        ///
        /// <b>True until a zone has been chosen.</b> At that stage the player is picking a direction,
        /// not a destination: there is nothing to tell one sector from another, and asking them to click
        /// a 16-cell square would be asking a question the map cannot answer yet.
        /// </summary>
        public bool AimsAtZones => _chosenZone < 0 && _zoneCount > 0 && ZoneResolver != null;

        /// <summary>
        /// Whether the view currently shows the whole ring of zones - the scale the design calls
        /// "monde", and the only one the separators are drawn at.
        ///
        /// Derived from what actually fits rather than from a pixel threshold: the world scale is the
        /// one where a zone's whole depth is on screen, so it follows the zones if they ever move.
        /// </summary>
        public bool ShowsWholeRing =>
            _zoneOuterRadiusCells > 0f && VisibleHalfWidthCells >= _zoneOuterRadiusCells;

        /// <summary>Half the viewport's width, in cells. The one conversion between what is on screen and what the world measures in.</summary>
        float VisibleHalfWidthCells => contentRect.size.x * 0.5f / PixelsPerSector * _sectorSizeCells;

        /// <summary>Pixels per sector. The one number that says how zoomed in the map is.</summary>
        public float PixelsPerSector { get; private set; } = DefaultPixelsPerSector;

        /// <summary>Which sector coordinate sits at the centre of the view.</summary>
        public Vector2 ViewCentreSectors { get; private set; }

        bool _dragging;
        bool _dragMoved;
        Vector2 _dragStart;
        Vector2 _dragCentreAtStart;

        /// <summary>How far the pointer may travel between press and release and still count as a click rather than a pan.</summary>
        const float ClickSlopPixels = 4f;

        public SectorMapElement()
        {
            style.overflow = Overflow.Hidden;
            style.flexGrow = 1f;

            _terrain.pickingMode = PickingMode.Ignore;
            _terrain.style.position = Position.Absolute;
            _terrain.style.left = 0;
            _terrain.style.top = 0;
            _terrain.style.right = 0;
            _terrain.style.bottom = 0;
            Add(_terrain);

            _overlay.pickingMode = PickingMode.Ignore;
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = 0;
            _overlay.style.top = 0;
            _overlay.style.right = 0;
            _overlay.style.bottom = 0;
            _overlay.generateVisualContent += DrawOverlay;
            Add(_overlay);

            _highlightLabel.pickingMode = PickingMode.Ignore;
            _highlightLabel.style.position = Position.Absolute;
            _highlightLabel.AddToClassList("sector-map-site-label");
            _highlightLabel.style.display = DisplayStyle.None;
            Add(_highlightLabel);

            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(-1));
            RegisterCallback<GeometryChangedEvent>(_ => OnGeometryChanged());
        }

        /// <summary>Hands the element what it draws. Called once the world exists; safe to call again if the map is rebuilt.</summary>
        public void Bind(SectorMapImage image, int sizeSectors, int sectorSizeCells, Vector2 coreCentreCells)
        {
            _image = image;
            _sizeSectors = Mathf.Max(1, sizeSectors);
            _sectorSizeCells = Mathf.Max(1, sectorSizeCells);
            _coreCentreCells = coreCentreCells;

            ViewCentreSectors = coreCentreCells / _sectorSizeCells;

            SyncTiles();
            Layout();
        }

        /// <summary>
        /// Matches the child elements to the discovered chunks. Called when the image reports it
        /// rebuilt - twice per mission rather than every frame.
        ///
        /// Children are only ever added, because a chunk once discovered is never undiscovered. The
        /// texture reference is re-set each time because a tile's pixels change while its identity does
        /// not, and UI Toolkit will not notice a texture rewritten under it otherwise.
        /// </summary>
        public void SyncTiles()
        {
            if (_image == null) return;

            IReadOnlyList<SectorMapImage.Tile> tiles = _image.Tiles;

            for (int i = _tileViews.Count; i < tiles.Count; i++)
            {
                var view = new VisualElement { pickingMode = PickingMode.Ignore };
                view.style.position = Position.Absolute;
                _terrain.Add(view);
                _tileViews.Add(view);
            }

            for (int i = 0; i < tiles.Count; i++)
            {
                _tileViews[i].style.backgroundImage = new StyleBackground(tiles[i].Texture);
            }
        }

        /// <summary>The Core's reach, redrawn when research extends it. Cells, like everything else the map is told.</summary>
        public void SetCoreRadius(float radiusCells)
        {
            if (Mathf.Approximately(_coreRadiusCells, radiusCells)) return;

            _coreRadiusCells = radiusCells;
            _overlay.MarkDirtyRepaint();
        }

        /// <summary>Recentres on the Core - what the panel does when it opens, so the player never has to find themselves.</summary>
        public void CentreOnCore()
        {
            ViewCentreSectors = _coreCentreCells / _sectorSizeCells;
            Layout();
        }

        // ---- Zoom and pan ----

        void OnWheel(WheelEvent evt)
        {
            // Read the anchor BEFORE the zoom changes. SectorAt answers using the current
            // PixelsPerSector, so asking it afterwards answered the new zoom's question and named a
            // different sector - the point under the cursor crept away a little on every notch.
            Vector2 anchor = SectorAt(evt.localMousePosition);

            float previous = PixelsPerSector;
            float wanted = PixelsPerSector * (evt.delta.y < 0f ? ZoomStep : 1f / ZoomStep);
            PixelsPerSector = Mathf.Clamp(wanted, MinPixelsPerSector, MaxPixelsPerSector);

            if (!Mathf.Approximately(previous, PixelsPerSector))
            {
                // Zoom about the pointer rather than the centre: zooming towards what you are looking
                // at is the difference between exploring a map and fighting one.
                //
                // This inverts SectorAt at the new scale, so the anchor lands back under the pointer.
                // The Y term is *added* because SectorAt subtracts it - screen Y grows downward and
                // sector Y grows north. Reusing the same sign for both axes, as this did, slid the
                // view vertically on every notch, which is what read as the map wandering off.
                Vector2 fromCentre = evt.localMousePosition - contentRect.size * 0.5f;
                ViewCentreSectors = new Vector2(
                    anchor.x - fromCentre.x / PixelsPerSector,
                    anchor.y + fromCentre.y / PixelsPerSector);

                Layout();
            }

            evt.StopPropagation();
        }

        /// <summary>
        /// Moves the view by a screen distance expressed in seconds of held key, divided by the zoom
        /// so the map slides at the same speed on screen whatever scale it is at. Zoomed right out,
        /// a fixed number of sectors per second would be imperceptible; zoomed right in, unusable.
        /// </summary>
        public void PanByKeyboard(Vector2 direction, float deltaTime)
        {
            if (direction.sqrMagnitude <= 0f) return;

            float sectorsPerSecond = KeyboardPanSectorsPerSecond * DefaultPixelsPerSector / PixelsPerSector;
            ViewCentreSectors += direction.normalized * sectorsPerSecond * deltaTime;
            Layout();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            _dragging = true;
            _dragMoved = false;
            _dragStart = evt.localPosition;
            _dragCentreAtStart = ViewCentreSectors;
            this.CapturePointer(evt.pointerId);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (AimsAtZones)
            {
                int zone = ZoneResolver(CellAt(evt.localPosition));
                SetHoveredZone(zone);

                // <b>Hovering aims, and the aim survives the pointer leaving.</b> What the pane shows
                // about a direction carries the button that acts on it, so reaching for that button
                // must not blank the thing it acts on - and a wedge that emptied on the way to its own
                // "Explorer" would be unusable. Leaving a wedge keeps the last one read.
                if (zone >= 0) SetSelectedZone(zone);
            }
            else
            {
                SetHoveredSite(SiteAt(evt.localPosition));
                SetHovered(SectorIndexAt(evt.localPosition));
            }

            if (!_dragging) return;

            Vector2 movedPixels = (Vector2)evt.localPosition - _dragStart;

            // A drag and a click arrive as the same pair of events; only the distance between them
            // tells the two apart. Without this, panning the map also re-aimed the mission at
            // whatever sector the button happened to come up over.
            if (movedPixels.sqrMagnitude > ClickSlopPixels * ClickSlopPixels) _dragMoved = true;

            ViewCentreSectors = _dragCentreAtStart - movedPixels / PixelsPerSector;
            Layout();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            bool wasClick = _dragging && !_dragMoved;

            _dragging = false;
            this.ReleasePointer(evt.pointerId);

            if (!wasClick) return;

            // While no zone has been chosen, a click aims at the whole wedge. The view is not moved:
            // the player is picking a direction, and jumping the camera under the click they just made
            // would answer a question they did not ask.
            if (AimsAtZones)
            {
                SetSelectedZone(ZoneResolver(CellAt(evt.localPosition)));
                return;
            }

            // Afterwards, at the whole-world scale, a click frames the zone it fell in rather than
            // aiming at a sector: sectors are a few pixels across there, so a click could only ever
            // mean "take me closer". Nothing is stacked and nothing is left to quit.
            if (ShowsWholeRing)
            {
                ZoneFramingRequested?.Invoke(CellAt(evt.localPosition));
                return;
            }

            // A site, or nothing. There is no square to select: the sector division is how a mission is
            // aimed, never something the player points at.
            int site = SiteAt(evt.localPosition);
            if (site >= 0)
            {
                SetSelectedSite(site);
                SetSelected(SectorIndexAt(evt.localPosition));
            }
        }

        /// <summary>Aims at a sector, or at nothing when the click fell off the map. Clicking the sector already aimed at leaves it aimed at - only the panel's own close clears it.</summary>
        void SetSelected(int sector)
        {
            if (sector == SelectedSector) return;

            SelectedSector = sector;
            _overlay.MarkDirtyRepaint();
            SelectedSectorChanged?.Invoke(sector);
        }

        /// <summary>Drops the aim, for the panel to call when it opens on a new session of looking.</summary>
        public void ClearSelection() => SetSelected(-1);

        /// <summary>
        /// Tells the map where the fleet currently is. Safe to call every frame: it repaints only
        /// when the set actually changes, which is twice per mission - once on launch, once on
        /// landing.
        /// </summary>
        public void SetMissionTargets(IReadOnlyList<int> targets)
        {
            if (SameAsCurrent(targets)) return;

            _missionTargets.Clear();
            if (targets != null) _missionTargets.AddRange(targets);
            _overlay.MarkDirtyRepaint();
        }

        bool SameAsCurrent(IReadOnlyList<int> targets)
        {
            int count = targets?.Count ?? 0;
            if (count != _missionTargets.Count) return false;

            for (int i = 0; i < count; i++)
            {
                if (targets[i] != _missionTargets[i]) return false;
            }
            return true;
        }

        /// <summary>Whether a robot is on its way to this sector. Read by the panel so the side pane can say so in words as well.</summary>
        public bool HasMissionTo(int sector) => _missionTargets.Contains(sector);

        /// <summary>
        /// The ring the zones occupy, and which one is being worked. Cells, like everything else the map
        /// is told - the geometry is <c>ExpeditionZoneSystem</c>'s and this only draws it.
        /// </summary>
        public void SetZoneRing(float innerRadiusCells, float outerRadiusCells, int zoneCount, int chosenZone)
        {
            if (Mathf.Approximately(_zoneInnerRadiusCells, innerRadiusCells)
                && Mathf.Approximately(_zoneOuterRadiusCells, outerRadiusCells)
                && _zoneCount == zoneCount && _chosenZone == chosenZone) return;

            _zoneInnerRadiusCells = innerRadiusCells;
            _zoneOuterRadiusCells = outerRadiusCells;
            _zoneCount = zoneCount;
            _chosenZone = chosenZone;
            _overlay.MarkDirtyRepaint();
        }

        /// <summary>
        /// The cells the player's own base occupies. Safe to call every frame, on the same terms as the
        /// sites: it repaints only when the base has actually changed shape.
        /// </summary>
        public void SetBuildings(IReadOnlyList<MapBuildingCell> buildings)
        {
            if (SameBuildings(buildings)) return;

            _buildings.Clear();
            if (buildings != null)
            {
                for (int i = 0; i < buildings.Count; i++) _buildings.Add(buildings[i]);
            }

            _overlay.MarkDirtyRepaint();
        }

        bool SameBuildings(IReadOnlyList<MapBuildingCell> buildings)
        {
            int count = buildings?.Count ?? 0;
            if (count != _buildings.Count) return false;

            for (int i = 0; i < count; i++)
            {
                if (!buildings[i].SameAs(_buildings[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// The sites to draw. Safe to call every frame: it repaints only when one has actually moved or
        /// changed state, which happens when a mission lands rather than when a frame is drawn.
        /// </summary>
        public void SetSites(IReadOnlyList<MapSiteMarker> sites)
        {
            if (SameSites(sites)) return;

            _sites.Clear();
            if (sites != null)
            {
                for (int i = 0; i < sites.Count; i++) _sites.Add(sites[i]);
            }

            RefreshHighlightLabel();
            _overlay.MarkDirtyRepaint();
        }

        bool SameSites(IReadOnlyList<MapSiteMarker> sites)
        {
            int count = sites?.Count ?? 0;
            if (count != _sites.Count) return false;

            for (int i = 0; i < count; i++)
            {
                if (!sites[i].SameAs(_sites[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Puts the view on a disc of ground: what the three scales are, expressed as what each has to
        /// fit rather than as a zoom level. The panel supplies the centre and the radius from real
        /// quantities - the Core's own ground, the exploration threshold, a zone's extent - so no scale
        /// is a number somebody chose.
        /// </summary>
        public void FrameCells(Vector2 centreCells, float radiusCells)
        {
            ViewCentreSectors = centreCells / _sectorSizeCells;

            float halfViewport = Mathf.Min(contentRect.size.x, contentRect.size.y) * 0.5f;
            if (halfViewport > 0f && radiusCells > 0f)
            {
                PixelsPerSector = Mathf.Clamp(
                    halfViewport / radiusCells * _sectorSizeCells, MinPixelsPerSector, MaxPixelsPerSector);

                _pendingFrameRadiusCells = 0f;
            }
            else
            {
                // <b>Asked for before the panel has been laid out.</b> Opening the map frames the whole
                // ring, but that happens on the frame the panel becomes visible, when contentRect is
                // still zero - so the zoom was computed against nothing and quietly dropped, and the map
                // opened at whatever scale it was last left at. Kept and applied on the first geometry.
                _pendingFrameCentreCells = centreCells;
                _pendingFrameRadiusCells = radiusCells;
            }

            Layout();
        }

        /// <summary>A framing that arrived before there was a viewport to fit it into. Zero means none pending.</summary>
        Vector2 _pendingFrameCentreCells;
        float _pendingFrameRadiusCells;

        void OnGeometryChanged()
        {
            if (_pendingFrameRadiusCells > 0f && contentRect.size.x > 0f)
            {
                FrameCells(_pendingFrameCentreCells, _pendingFrameRadiusCells);
                return;
            }

            Layout();
        }

        /// <summary>The cell coordinate under a point in this element - what a framing click is asked about.</summary>
        public Vector2 CellAt(Vector2 localPoint) => SectorAt(localPoint) * _sectorSizeCells;

        void SetHovered(int sector)
        {
            if (sector == HoveredSector) return;

            HoveredSector = sector;
            _overlay.MarkDirtyRepaint();
            HoveredSectorChanged?.Invoke(sector);
        }

        /// <summary>
        /// The site whose mark is nearest the pointer and within reach of it, or -1.
        ///
        /// Nearest rather than first, so two marks close together resolve to the one actually being
        /// pointed at. A done site is skipped: it is not clickable, and letting it take the pick would
        /// shadow a live one behind it.
        /// </summary>
        int SiteAt(Vector2 localPoint)
        {
            int nearest = -1;
            float nearestDistance = SitePickRadiusPixels * SitePickRadiusPixels;

            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].State == MapSiteState.Done) continue;

                float distance = (PointAt(_sites[i].CellPosition) - localPoint).sqrMagnitude;
                if (distance > nearestDistance) continue;

                nearestDistance = distance;
                nearest = i;
            }

            return nearest;
        }

        void SetHoveredSite(int site)
        {
            if (site == HoveredSite) return;

            HoveredSite = site;
            _overlay.MarkDirtyRepaint();
        }

        void SetSelectedSite(int site)
        {
            if (site == SelectedSite) return;

            SelectedSite = site;
            _overlay.MarkDirtyRepaint();
            SelectedSiteChanged?.Invoke(site);
        }

        void SetHoveredZone(int zone)
        {
            if (zone == HoveredZone) return;

            HoveredZone = zone;
            _overlay.MarkDirtyRepaint();
        }

        void SetSelectedZone(int zone)
        {
            if (zone == SelectedZone) return;

            SelectedZone = zone;
            _overlay.MarkDirtyRepaint();
            SelectedZoneChanged?.Invoke(zone);
        }

        /// <summary>Drops the aim on both slots, for the panel to call when it opens on a new session of looking.</summary>
        public void ClearZoneSelection() => SetSelectedZone(-1);

        // ---- Geometry ----

        void Layout()
        {
            LayoutTiles();
            RefreshHighlightLabel();
            _overlay.MarkDirtyRepaint();
        }

        /// <summary>
        /// Parks the one permanent label over the site put forward, or hides it.
        ///
        /// <b>One label, and only one.</b> Everything else waits to be hovered - which is what lets a
        /// suggestion be visible without blinking, and what stops a zone full of names from reading as
        /// a list rather than a map.
        /// </summary>
        void RefreshHighlightLabel()
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                if (_sites[i].State != MapSiteState.Highlighted || string.IsNullOrEmpty(_sites[i].Label)) continue;

                Vector2 point = PointAt(_sites[i].CellPosition);
                _highlightLabel.text = _sites[i].Label;
                _highlightLabel.style.display = DisplayStyle.Flex;
                _highlightLabel.style.left = point.x + 12f;
                _highlightLabel.style.top = point.y - 9f;
                return;
            }

            _highlightLabel.style.display = DisplayStyle.None;
        }

        /// <summary>
        /// Places every tile at its own cell coordinates.
        ///
        /// One rectangle per discovered chunk rather than one for the whole world: there is no
        /// whole-world image any more, so a chunk's position is the only thing that says where its
        /// pixels belong. The texture's row 0 is the chunk's south edge and UI Toolkit draws a
        /// Texture2D the right way up, so the screen's top edge is the chunk's *north* and the
        /// rectangle is anchored from there.
        /// </summary>
        void LayoutTiles()
        {
            if (_image == null) return;

            IReadOnlyList<SectorMapImage.Tile> tiles = _image.Tiles;
            float pixelsPerCell = PixelsPerSector / _sectorSizeCells;
            float side = _image.ChunkSizeCells * pixelsPerCell;

            for (int i = 0; i < _tileViews.Count; i++)
            {
                if (i >= tiles.Count)
                {
                    _tileViews[i].style.display = DisplayStyle.None;
                    continue;
                }

                SectorMapImage.Tile tile = tiles[i];
                Vector2 topLeft = PointAt(new Vector2(
                    tile.OriginCell.X,
                    tile.OriginCell.Y + tile.SizeCells));

                VisualElement view = _tileViews[i];
                view.style.display = DisplayStyle.Flex;
                view.style.left = topLeft.x;
                view.style.top = topLeft.y;
                view.style.width = side;
                view.style.height = side;
            }
        }

        /// <summary>The sector coordinate under a point in this element, in fractional sectors.</summary>
        public Vector2 SectorAt(Vector2 localPoint)
        {
            Vector2 fromCentre = localPoint - contentRect.size * 0.5f;
            return new Vector2(
                ViewCentreSectors.x + fromCentre.x / PixelsPerSector,
                ViewCentreSectors.y - fromCentre.y / PixelsPerSector);
        }

        /// <summary>The sector index under a point, or -1 when it falls outside the map.</summary>
        public int SectorIndexAt(Vector2 localPoint)
        {
            Vector2 sector = SectorAt(localPoint);
            int column = Mathf.FloorToInt(sector.x);
            int row = Mathf.FloorToInt(sector.y);

            if (column < 0 || row < 0 || column >= _sizeSectors || row >= _sizeSectors) return -1;
            return row * _sizeSectors + column;
        }

        /// <summary>Where a cell coordinate lands in this element, in pixels.</summary>
        Vector2 PointAt(Vector2 cells)
        {
            Vector2 sector = cells / _sectorSizeCells;
            Vector2 centre = contentRect.size * 0.5f;

            return new Vector2(
                centre.x + (sector.x - ViewCentreSectors.x) * PixelsPerSector,
                centre.y - (sector.y - ViewCentreSectors.y) * PixelsPerSector);
        }

        // ---- Overlay ----

        void DrawOverlay(MeshGenerationContext context)
        {
            if (_image == null) return;

            Painter2D painter = context.painter2D;

            // Bottom to top, and the order is the design's: the ground's own marks first, then what the
            // player can act on, then what they are pointing at.
            DrawBuildings(painter);
            DrawZoneSeparators(painter);
            DrawMissionTargets(painter);
            DrawSites(painter);
            DrawRadius(painter);
            DrawCore(painter);
        }

        /// <summary>
        /// Where the fleet already is.
        /// </summary>
        /// <remarks>
        /// Drawn as an outline <b>and</b> a centre mark, because the two answer at different scales:
        /// zoomed in, the outline says which square; zoomed out to the whole world, a sector is a few
        /// pixels across and only a mark of its own remains visible. Without either, the one thing a
        /// player most needs from this map - is a robot already handling that? - could only be
        /// answered by reading a list somewhere else.
        /// </remarks>
        void DrawMissionTargets(Painter2D painter)
        {
            if (_missionTargets.Count == 0) return;

            foreach (int sector in _missionTargets)
            {
                if (sector < 0 || sector >= _sizeSectors * _sizeSectors) continue;

                int column = sector % _sizeSectors;
                int row = sector / _sizeSectors;
                Vector2 centre = PointAt(new Vector2(
                    (column + 0.5f) * _sectorSizeCells,
                    (row + 0.5f) * _sectorSizeCells));

                // A ring and a dot rather than the sector's square. Nothing on this map is square: the
                // division is how a mission is aimed, not something the player should ever have to see.
                painter.strokeColor = MissionColour;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.Arc(centre, 11f, 0f, 360f);
                painter.Stroke();

                painter.fillColor = MissionColour;
                painter.BeginPath();
                painter.Arc(centre, 3.5f, 0f, 360f);
                painter.Fill();
            }
        }

        /// <summary>
        /// The base, cell by cell.
        ///
        /// <b>Only once a cell is worth a pixel.</b> At the whole-world scale a belt is a fraction of one,
        /// so hundreds of sub-pixel quads would cost a frame to draw a grey smudge over the Core's own
        /// mark - which already says "you are here", and says it better. The base appears as the player
        /// zooms towards it, which is also the only scale at which its shape means anything.
        /// </summary>
        void DrawBuildings(Painter2D painter)
        {
            if (_buildings.Count == 0) return;

            float pixelsPerCell = PixelsPerSector / _sectorSizeCells;
            if (pixelsPerCell < MinPixelsPerCellForBuildings) return;

            for (int i = 0; i < _buildings.Count; i++)
            {
                MapBuildingCell cell = _buildings[i];

                // The cell's north-west corner: the world's Y grows north and the screen's grows down,
                // so the top edge is the cell above.
                Vector2 corner = PointAt(new Vector2(cell.X, cell.Y + 1));

                painter.fillColor = cell.IsBelt ? BeltColour : BuildingColour;
                painter.BeginPath();
                painter.MoveTo(corner);
                painter.LineTo(corner + new Vector2(pixelsPerCell, 0f));
                painter.LineTo(corner + new Vector2(pixelsPerCell, pixelsPerCell));
                painter.LineTo(corner + new Vector2(0f, pixelsPerCell));
                painter.ClosePath();
                painter.Fill();
            }
        }

        /// <summary>
        /// Six lines from the edge of the Core's ground outward, at the whole-world scale only.
        ///
        /// <b>They are dim on purpose.</b> Their job is to show that other directions exist, not to
        /// offer them - the five that are not being worked have no terrain to show and must not ask the
        /// player for anything. Drawn from the inner edge rather than the Core, so the Core's own disc
        /// stays undivided: it belongs to no zone.
        /// </summary>
        void DrawZoneSeparators(Painter2D painter)
        {
            if (_zoneCount <= 0 || !ShowsWholeRing) return;

            Vector2 centre = PointAt(_coreCentreCells);
            float pixelsPerCell = PixelsPerSector / _sectorSizeCells;
            float inner = _zoneInnerRadiusCells * pixelsPerCell;
            float outer = _zoneOuterRadiusCells * pixelsPerCell;
            if (outer - inner < 2f) return;

            // The zone being pointed at or picked, filled behind everything else so the wedge reads as
            // one place rather than as two lines with something between them.
            if (AimsAtZones)
            {
                if (HoveredZone >= 0 && HoveredZone != SelectedZone) FillWedge(painter, HoveredZone, inner, outer, ZoneHoverFill);
                if (SelectedZone >= 0) FillWedge(painter, SelectedZone, inner, outer, ZoneSelectedFill);
            }

            // <b>The ring closes the zones.</b> Six lines that simply stop leave the eye to guess where
            // the reachable ground ends - and that edge is the whole reason a zone is finite. Drawn
            // before the separators so they cross it rather than being cut by it.
            painter.strokeColor = SeparatorColour;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.Arc(centre, outer, 0f, 360f);
            painter.Stroke();

            float slice = 360f / _zoneCount;
            painter.lineWidth = 1f;

            for (int i = 0; i < _zoneCount; i++)
            {
                // A separator sits between two zones, so it is half a slice off a zone's centre. Both
                // of the chosen zone's own edges therefore light up, which is what draws the wedge.
                float degrees = i * slice + slice * 0.5f;
                bool bordersChosen = _chosenZone >= 0 && (i == _chosenZone || (i + 1) % _zoneCount == _chosenZone);

                Vector2 direction = Bearing(degrees);

                painter.strokeColor = bordersChosen ? ChosenSeparatorColour : SeparatorColour;
                painter.BeginPath();
                painter.MoveTo(centre + direction * inner);
                painter.LineTo(centre + direction * outer);
                painter.Stroke();
            }
        }

        /// <summary>
        /// One zone's wedge, filled.
        ///
        /// Built from line segments along both arcs rather than from <c>Painter2D.Arc</c>: the screen's
        /// Y grows downward while the world's grows north, so an arc's sweep direction is mirrored here
        /// and the two edges would have to be swept opposite ways. Sampling the arc sidesteps the whole
        /// question and is exact enough at any zoom the map reaches.
        /// </summary>
        void FillWedge(Painter2D painter, int zone, float inner, float outer, Color colour)
        {
            const int Steps = 24;

            Vector2 centre = PointAt(_coreCentreCells);
            float slice = 360f / _zoneCount;
            float from = zone * slice - slice * 0.5f;
            float to = zone * slice + slice * 0.5f;

            painter.fillColor = colour;
            painter.BeginPath();

            for (int i = 0; i <= Steps; i++)
            {
                Vector2 point = centre + Bearing(Mathf.Lerp(from, to, i / (float)Steps)) * outer;
                if (i == 0) painter.MoveTo(point);
                else painter.LineTo(point);
            }

            for (int i = Steps; i >= 0; i--)
            {
                painter.LineTo(centre + Bearing(Mathf.Lerp(from, to, i / (float)Steps)) * inner);
            }

            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>A unit vector for a world bearing, in screen space - where Y grows downward and the world's grows north.</summary>
        static Vector2 Bearing(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians));
        }

        /// <summary>
        /// The four states of a site, each readable without a word: filled for available, a wide ring
        /// for the one put forward, muted for done, hollow for what needs units.
        /// </summary>
        void DrawSites(Painter2D painter)
        {
            for (int i = 0; i < _sites.Count; i++)
            {
                MapSiteMarker site = _sites[i];
                Vector2 point = PointAt(site.CellPosition);

                switch (site.State)
                {
                    case MapSiteState.Available:
                        painter.fillColor = site.Tint;
                        painter.BeginPath();
                        painter.Arc(point, 5f, 0f, 360f);
                        painter.Fill();
                        break;

                    case MapSiteState.Highlighted:
                        painter.fillColor = site.Tint;
                        painter.BeginPath();
                        painter.Arc(point, 5f, 0f, 360f);
                        painter.Fill();

                        painter.strokeColor = site.Tint;
                        painter.lineWidth = 2f;
                        painter.BeginPath();
                        painter.Arc(point, 10f, 0f, 360f);
                        painter.Stroke();
                        break;

                    case MapSiteState.Done:
                        // Kept, and kept quiet. The inner mark is what was found, still legible.
                        painter.strokeColor = DoneColour;
                        painter.lineWidth = 1.5f;
                        painter.BeginPath();
                        painter.Arc(point, 5f, 0f, 360f);
                        painter.Stroke();

                        painter.fillColor = DoneColour;
                        painter.BeginPath();
                        painter.Arc(point, 2f, 0f, 360f);
                        painter.Fill();
                        break;

                    default:
                        painter.strokeColor = LockedColour;
                        painter.lineWidth = 1.5f;
                        painter.BeginPath();
                        painter.Arc(point, 5f, 0f, 360f);
                        painter.Stroke();
                        break;
                }

                // The glance and the decision, both as rings around the mark - never as a square. The
                // selection is thicker and in the panel's accent, the hover thin and white.
                if (i == SelectedSite)
                {
                    painter.strokeColor = SelectionColour;
                    painter.lineWidth = 2.5f;
                    painter.BeginPath();
                    painter.Arc(point, 12f, 0f, 360f);
                    painter.Stroke();
                }
                else if (i == HoveredSite)
                {
                    painter.strokeColor = HoverColour;
                    painter.lineWidth = 1.5f;
                    painter.BeginPath();
                    painter.Arc(point, 10f, 0f, 360f);
                    painter.Stroke();
                }
            }
        }

        /// <summary>The Core's reach, dashed - a boundary the player owns rather than a wall.</summary>
        void DrawRadius(Painter2D painter)
        {
            if (_coreRadiusCells <= 0f) return;

            Vector2 centre = PointAt(_coreCentreCells);
            float radius = _coreRadiusCells / _sectorSizeCells * PixelsPerSector;
            if (radius < 2f) return;   // below this a dashed ring is just a smudge

            painter.strokeColor = RadiusColour;
            painter.lineWidth = 1.5f;

            const int Dashes = 48;
            for (int i = 0; i < Dashes; i += 2)
            {
                float from = i / (float)Dashes * Mathf.PI * 2f;
                float to = (i + 1) / (float)Dashes * Mathf.PI * 2f;

                painter.BeginPath();
                painter.Arc(centre, radius, from * Mathf.Rad2Deg, to * Mathf.Rad2Deg);
                painter.Stroke();
            }
        }

        void DrawCore(Painter2D painter)
        {
            Vector2 centre = PointAt(_coreCentreCells);

            painter.fillColor = CoreColour;
            painter.BeginPath();
            painter.Arc(centre, 4f, 0f, 360f);
            painter.Fill();
        }
    }
}

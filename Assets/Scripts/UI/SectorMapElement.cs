using System.Collections.Generic;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The zoomed-out map: one quad per discovered chunk, zoomable and pannable, with the Core, its
    /// action radius, the base, the explorer robots and the limit of their range drawn over it.
    ///
    /// <b>It is a map and nothing else.</b> Nothing here is aimed at, hovered or selected: the sector
    /// division is how the world is cut up internally and was never something the player should point
    /// at. What it answers is "what have I opened, and where are my robots" - and that is the whole
    /// job.
    ///
    /// <b>There is no map underneath the tiles.</b> Nothing is drawn where nothing is discovered, so
    /// the black is the panel showing through - the absence of a map rather than a dark surface
    /// somebody painted. It is what gives the revealed patches their weight and makes the gap between
    /// them read as something to close.
    ///
    /// <b>The tiles are the whole terrain layer, and only the view moves.</b> Each holds one texel per
    /// cell of one chunk (16 KB), created when that chunk is first discovered; zooming and panning cost
    /// a rectangle per tile and no re-upload.
    ///
    /// <b>Zoom is not a nicety here, it is what makes the map readable at all.</b> Showing 10 000
    /// cells at once in a 900-pixel panel is 11 cells per pixel: the Core's radius would be under
    /// three pixels. So the panel opens on the robots' range and zooms out to the whole world only if
    /// asked.
    ///
    /// The marks are drawn in Painter2D rather than imported — the same gesture as `HatchFillElement`
    /// and `ClockGlyphElement`, and for the same reasons: exact at any size, no texture to maintain,
    /// no lifetime to manage.
    /// </summary>
    public sealed class SectorMapElement : VisualElement
    {
        /// <summary>Pixels per sector at the closest zoom.</summary>
        public const float MaxPixelsPerSector = 144f;

        /// <summary>Pixels per sector when the whole world is asked for.</summary>
        public const float MinPixelsPerSector = 3f;

        /// <summary>What the panel opens on: close enough that the robots' range fills a good part of the view.</summary>
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

        /// <summary>The base's own colours: everything that transforms or holds in blue, everything that carries in white.</summary>
        static readonly Color BuildingColour = new Color(0.30f, 0.52f, 0.93f, 1f);
        static readonly Color BeltColour = new Color(0.92f, 0.94f, 0.97f, 1f);

        /// <summary>Below this the base is not drawn at all - see DrawBuildings. One pixel per cell is the point at which a footprint is a shape rather than a speck.</summary>
        const float MinPixelsPerCellForBuildings = 1f;

        /// <summary>The limit of where a robot will wander. Dim: it states where the ground the player can reach ends, it does not offer anything.</summary>
        static readonly Color OuterRingColour = new Color(1f, 1f, 1f, 0.16f);

        /// <summary>A robot out working. Amber, the colour this project already uses for "under way".</summary>
        static readonly Color RobotColour = new Color(0.937f, 0.624f, 0.153f, 1f);

        /// <summary>
        /// A wreck the player has found. Warm grey - a place, not an activity: it must read as
        /// somewhere to go back to rather than as something happening, which is what the amber of a
        /// working robot says.
        /// </summary>
        static readonly Color WreckColour = new Color(0.78f, 0.74f, 0.68f, 1f);

        /// <summary>
        /// How big a wreck's mark is. <b>Fixed in pixels, like a robot's</b>: at three cells across it
        /// would be under a pixel at the zoomed-out end, and a mark that vanishes at the scale you use
        /// to look for it is not a mark. Drawn as a square, so it is not mistaken for a robot.
        /// </summary>
        const float WreckHalfSizePixels = 4f;

        /// <summary>A robot parked at the base. Muted, because it is not doing anything.</summary>
        static readonly Color RobotRestingColour = new Color(0.55f, 0.58f, 0.62f, 1f);

        /// <summary>The ring on the robot whose panel is open - the map's half of the halo the world draws on it.</summary>
        static readonly Color RobotSelectedColour = new Color(0.333f, 0.867f, 0.961f, 1f);

        /// <summary>
        /// How big a robot's mark is. <b>Fixed in pixels, not in cells</b>: a robot is a thing to find
        /// on this map, not a thing with a size on the ground, and one that shrank with the zoom would
        /// vanish at exactly the scale where the player is looking for it.
        /// </summary>
        const float RobotRadiusPixels = 5f;

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
        int _sectorSizeCells = 16;

        Vector2 _coreCentreCells;
        float _coreRadiusCells;

        /// <summary>How far a robot will go from the Core. Handed in rather than computed - the figure belongs to ExplorerRobotSettings and this only draws it.</summary>
        float _outerRingRadiusCells;

        /// <summary>Every cell the player's base stands on. The one thing on this map that is already theirs.</summary>
        readonly List<MapBuildingCell> _buildings = new List<MapBuildingCell>();

        readonly List<MapRobotMark> _robots = new List<MapRobotMark>();

        /// <summary>Only the wrecks the player has found - see MapWreckMark.</summary>
        readonly List<MapWreckMark> _wrecks = new List<MapWreckMark>();

        /// <summary>Pixels per sector. The one number that says how zoomed in the map is.</summary>
        public float PixelsPerSector { get; private set; } = DefaultPixelsPerSector;

        /// <summary>Which sector coordinate sits at the centre of the view.</summary>
        public Vector2 ViewCentreSectors { get; private set; }

        /// <summary>UI Toolkit reports the pointer button as an integer, and zero is the left one. Named because a bare 0 in a button test says nothing.</summary>
        const int LeftMouseButton = 0;

        bool _dragging;
        Vector2 _dragStart;
        Vector2 _dragCentreAtStart;

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

            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<GeometryChangedEvent>(_ => OnGeometryChanged());
        }

        /// <summary>Hands the element what it draws. Called once the world exists; safe to call again if the map is rebuilt.</summary>
        public void Bind(SectorMapImage image, int sectorSizeCells, Vector2 coreCentreCells)
        {
            _image = image;
            _sectorSizeCells = Mathf.Max(1, sectorSizeCells);
            _coreCentreCells = coreCentreCells;

            ViewCentreSectors = coreCentreCells / _sectorSizeCells;

            SyncTiles();
            Layout();
        }

        /// <summary>
        /// Matches the child elements to the discovered chunks. Called when the image reports it
        /// rebuilt rather than every frame.
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

        /// <summary>How far the robots will wander, drawn as the map's outer limit.</summary>
        public void SetOuterRingRadius(float radiusCells)
        {
            if (Mathf.Approximately(_outerRingRadiusCells, radiusCells)) return;

            _outerRingRadiusCells = radiusCells;
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

        /// <summary>
        /// Left button only, like the world screen - it used to pan on any button, middle and right
        /// included, which no other drag in the game does.
        ///
        /// <b>Not routed through the binding table, and it cannot be.</b> This is a UI Toolkit
        /// pointer event, not an Input System read: the button arrives as an integer on the event.
        /// It is also deliberately not reassignable, so there is no binding to keep in step - the
        /// constant below is the whole declaration.
        /// </summary>
        void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != LeftMouseButton) return;

            _dragging = true;
            _dragStart = evt.localPosition;
            _dragCentreAtStart = ViewCentreSectors;
            this.CapturePointer(evt.pointerId);
        }

        /// <summary>
        /// Dragging pans, and that is the only thing a pointer does here. There is no click behaviour
        /// left to tell a drag apart from, so no slop and no click/drag arbitration.
        ///
        /// <b>The drag grabs the map, not the view</b> - the same gesture CameraPanController gives the
        /// world: the ground follows the hand, so the view centre travels the opposite way, and the
        /// point grabbed stays under the cursor for the whole drag.
        ///
        /// <b>The two axes need opposite signs, and subtracting the travel as a vector gives them the
        /// same one.</b> Screen Y grows downward while sector Y grows north, so a single subtraction
        /// reads as "grab" horizontally and "push" vertically: dragging right did move the map with
        /// the hand, and dragging up moved it against - the map went north where the world screen
        /// goes south. This inverts SectorAt per axis instead, which is the same asymmetry OnWheel
        /// already has to spell out.
        /// </summary>
        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!_dragging) return;

            Vector2 travel = ((Vector2)evt.localPosition - _dragStart) / PixelsPerSector;
            ViewCentreSectors = new Vector2(
                _dragCentreAtStart.x - travel.x,
                _dragCentreAtStart.y + travel.y);
            Layout();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (evt.button != LeftMouseButton) return;

            _dragging = false;
            this.ReleasePointer(evt.pointerId);
        }

        // ---- What is drawn ----

        /// <summary>
        /// The cells the player's own base occupies. Safe to call every frame: it repaints only when
        /// the base has actually changed shape.
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
        /// Where the robots are. Safe to call every frame: it repaints only when one has actually moved
        /// a tenth of a cell, which is what keeps a walking robot from repainting the overlay sixty
        /// times a second for movement nobody can see.
        /// </summary>
        /// <summary>
        /// The wrecks found so far. Safe to call every frame: it repaints only when the set has
        /// actually changed, which for wrecks means one more was found.
        /// </summary>
        public void SetWrecks(IReadOnlyList<MapWreckMark> wrecks)
        {
            if (SameWrecks(wrecks)) return;

            _wrecks.Clear();
            if (wrecks != null)
            {
                for (int i = 0; i < wrecks.Count; i++) _wrecks.Add(wrecks[i]);
            }

            _overlay.MarkDirtyRepaint();
        }

        bool SameWrecks(IReadOnlyList<MapWreckMark> wrecks)
        {
            int count = wrecks?.Count ?? 0;
            if (count != _wrecks.Count) return false;

            for (int i = 0; i < count; i++)
            {
                if (!_wrecks[i].SameAs(wrecks[i])) return false;
            }

            return true;
        }

        public void SetRobots(IReadOnlyList<MapRobotMark> robots)
        {
            if (SameRobots(robots)) return;

            _robots.Clear();
            if (robots != null)
            {
                for (int i = 0; i < robots.Count; i++) _robots.Add(robots[i]);
            }

            _overlay.MarkDirtyRepaint();
        }

        bool SameRobots(IReadOnlyList<MapRobotMark> robots)
        {
            int count = robots?.Count ?? 0;
            if (count != _robots.Count) return false;

            for (int i = 0; i < count; i++)
            {
                if (!robots[i].SameAs(_robots[i])) return false;
            }
            return true;
        }

        /// <summary>
        /// Puts the view on a disc of ground: a scale expressed as what it has to fit rather than as a
        /// zoom level. The panel supplies the centre and the radius from real quantities - the Core's
        /// own ground, the robots' range - so no scale is a number somebody chose.
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
                // range, but that happens on the frame the panel becomes visible, when contentRect is
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

        // ---- Geometry ----

        void Layout()
        {
            LayoutTiles();
            _overlay.MarkDirtyRepaint();
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

        /// <summary>The sector coordinate under a point in this element, in fractional sectors. Used by the zoom to hold a point under the pointer, and by nothing else.</summary>
        Vector2 SectorAt(Vector2 localPoint)
        {
            Vector2 fromCentre = localPoint - contentRect.size * 0.5f;
            return new Vector2(
                ViewCentreSectors.x + fromCentre.x / PixelsPerSector,
                ViewCentreSectors.y - fromCentre.y / PixelsPerSector);
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

            // Bottom to top: the ground's own marks, then the boundaries, then the things that move.
            DrawBuildings(painter);
            DrawOuterRing(painter);
            DrawRadius(painter);
            DrawCore(painter);
            // Under the robots: a robot moves and is what the player is following, so it must never
            // be hidden by a mark that never moves.
            DrawWrecks(painter);
            DrawRobots(painter);
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
        /// The limit of where a robot will go, as one ring.
        ///
        /// <b>Drawn at every scale.</b> Zoomed in it simply leaves the screen, which is the correct way
        /// for a boundary to be far away - and it is dim, because it states where the reachable ground
        /// ends rather than offering anything.
        /// </summary>
        void DrawOuterRing(Painter2D painter)
        {
            if (_outerRingRadiusCells <= 0f) return;

            float radius = _outerRingRadiusCells / _sectorSizeCells * PixelsPerSector;
            if (radius < 4f) return;   // below this it is a smudge around the Core's own mark

            painter.strokeColor = OuterRingColour;
            painter.lineWidth = 1f;
            painter.BeginPath();
            painter.Arc(PointAt(_coreCentreCells), radius, 0f, 360f);
            painter.Stroke();
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

        /// <summary>
        /// The robots. <b>The one thing on this map that moves</b>, and now the main reason to open it:
        /// a wandering robot is somewhere the player did not choose, so the map is the only way to find
        /// out where.
        ///
        /// A parked robot is muted rather than hidden - "the fleet is home" is an answer too, and
        /// hiding it would leave the player wondering whether the map had simply lost them.
        /// </summary>
        /// <summary>
        /// The wrecks, as small squares. A square rather than a disc so it is not read as a robot,
        /// and the same size at every zoom for the reason WreckHalfSizePixels gives.
        /// </summary>
        void DrawWrecks(Painter2D painter)
        {
            if (_wrecks.Count == 0) return;

            painter.fillColor = WreckColour;

            for (int i = 0; i < _wrecks.Count; i++)
            {
                Vector2 point = PointAt(_wrecks[i].CellPosition);

                painter.BeginPath();
                painter.MoveTo(new Vector2(point.x - WreckHalfSizePixels, point.y - WreckHalfSizePixels));
                painter.LineTo(new Vector2(point.x + WreckHalfSizePixels, point.y - WreckHalfSizePixels));
                painter.LineTo(new Vector2(point.x + WreckHalfSizePixels, point.y + WreckHalfSizePixels));
                painter.LineTo(new Vector2(point.x - WreckHalfSizePixels, point.y + WreckHalfSizePixels));
                painter.ClosePath();
                painter.Fill();
            }
        }

        void DrawRobots(Painter2D painter)
        {
            for (int i = 0; i < _robots.Count; i++)
            {
                MapRobotMark robot = _robots[i];
                Vector2 point = PointAt(robot.CellPosition);

                painter.fillColor = robot.IsOut ? RobotColour : RobotRestingColour;
                painter.BeginPath();
                painter.Arc(point, RobotRadiusPixels, 0f, 360f);
                painter.Fill();

                if (!robot.Selected) continue;

                painter.strokeColor = RobotSelectedColour;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.Arc(point, RobotRadiusPixels * 2.2f, 0f, 360f);
                painter.Stroke();
            }
        }
    }
}

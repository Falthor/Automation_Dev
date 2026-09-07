using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The zoomed-out map: one texel per sector, zoomable and pannable, with the Core and its action
    /// radius drawn over it.
    ///
    /// <b>The whole map is always in the texture; only the view moves.</b> `SectorMapImage` holds
    /// every sector of a 10 000-cell world in 390 KB, so zooming and panning cost nothing but a
    /// rectangle change — no streaming, no re-upload, no window to follow.
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
        /// <summary>Pixels per sector at the closest zoom. Past this the texels are so large the map reads as blocks rather than as ground.</summary>
        public const float MaxPixelsPerSector = 48f;

        /// <summary>Pixels per sector when the whole world is asked for. Below one texel per pixel there is nothing more to see.</summary>
        public const float MinPixelsPerSector = 1f;

        /// <summary>What the panel opens on: close enough that the mission-reachable ring fills a good part of the view.</summary>
        public const float DefaultPixelsPerSector = 16f;

        static readonly Color CoreColour = new Color(0.85f, 0.45f, 0.95f, 1f);
        static readonly Color RadiusColour = new Color(0.333f, 0.867f, 0.961f, 0.85f);
        static readonly Color HoverColour = new Color(1f, 1f, 1f, 0.9f);

        readonly VisualElement _image = new VisualElement();
        readonly VisualElement _overlay = new VisualElement();

        Texture2D _texture;
        int _sizeSectors = 1;
        int _sectorSizeCells = 16;

        Vector2 _coreCentreCells;
        float _coreRadiusCells;

        /// <summary>Which sector the pointer is over, or -1. Read by the panel to fill its tooltip.</summary>
        public int HoveredSector { get; private set; } = -1;

        public event Action<int> HoveredSectorChanged;

        /// <summary>Pixels per sector. The one number that says how zoomed in the map is.</summary>
        public float PixelsPerSector { get; private set; } = DefaultPixelsPerSector;

        /// <summary>Which sector coordinate sits at the centre of the view.</summary>
        public Vector2 ViewCentreSectors { get; private set; }

        bool _dragging;
        Vector2 _dragStart;
        Vector2 _dragCentreAtStart;

        public SectorMapElement()
        {
            style.overflow = Overflow.Hidden;
            style.flexGrow = 1f;

            _image.pickingMode = PickingMode.Ignore;
            _image.style.position = Position.Absolute;
            Add(_image);

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
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(-1));
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        /// <summary>Hands the element what it draws. Called once the world exists; safe to call again if the map is rebuilt.</summary>
        public void Bind(Texture2D texture, int sizeSectors, int sectorSizeCells, Vector2 coreCentreCells)
        {
            _texture = texture;
            _sizeSectors = Mathf.Max(1, sizeSectors);
            _sectorSizeCells = Mathf.Max(1, sectorSizeCells);
            _coreCentreCells = coreCentreCells;

            _image.style.backgroundImage = new StyleBackground(texture);
            ViewCentreSectors = coreCentreCells / _sectorSizeCells;

            Layout();
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
            float previous = PixelsPerSector;
            float wanted = PixelsPerSector * (evt.delta.y < 0f ? 1.2f : 1f / 1.2f);
            PixelsPerSector = Mathf.Clamp(wanted, MinPixelsPerSector, MaxPixelsPerSector);

            if (!Mathf.Approximately(previous, PixelsPerSector))
            {
                // Zoom about the pointer rather than the centre: zooming towards what you are looking
                // at is the difference between exploring a map and fighting one.
                Vector2 underPointer = SectorAt(evt.localMousePosition);
                Vector2 fromCentre = evt.localMousePosition - contentRect.size * 0.5f;
                ViewCentreSectors = underPointer - fromCentre / PixelsPerSector;

                Layout();
            }

            evt.StopPropagation();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            _dragging = true;
            _dragStart = evt.localPosition;
            _dragCentreAtStart = ViewCentreSectors;
            this.CapturePointer(evt.pointerId);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            SetHovered(SectorIndexAt(evt.localPosition));

            if (!_dragging) return;

            Vector2 movedPixels = (Vector2)evt.localPosition - _dragStart;
            ViewCentreSectors = _dragCentreAtStart - movedPixels / PixelsPerSector;
            Layout();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            _dragging = false;
            this.ReleasePointer(evt.pointerId);
        }

        void SetHovered(int sector)
        {
            if (sector == HoveredSector) return;

            HoveredSector = sector;
            _overlay.MarkDirtyRepaint();
            HoveredSectorChanged?.Invoke(sector);
        }

        // ---- Geometry ----

        void Layout()
        {
            if (_texture == null) return;

            float side = _sizeSectors * PixelsPerSector;
            Vector2 centre = contentRect.size * 0.5f;

            _image.style.width = side;
            _image.style.height = side;
            _image.style.left = centre.x - ViewCentreSectors.x * PixelsPerSector;

            // The texture's row 0 is the map's south edge, and UI Toolkit already draws a Texture2D
            // the right way up - so the element's own top edge is the map's *north*, and the offset
            // is measured from there. An extra flip was tried here and was wrong twice over: it put
            // north below the Core on screen, and it would have mirrored the hover against what was
            // drawn.
            _image.style.top = centre.y - (_sizeSectors - ViewCentreSectors.y) * PixelsPerSector;

            _overlay.MarkDirtyRepaint();
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
            if (_texture == null) return;

            Painter2D painter = context.painter2D;

            DrawHoveredSector(painter);
            DrawRadius(painter);
            DrawCore(painter);
        }

        void DrawHoveredSector(Painter2D painter)
        {
            if (HoveredSector < 0) return;

            int column = HoveredSector % _sizeSectors;
            int row = HoveredSector / _sizeSectors;

            Vector2 topLeft = PointAt(new Vector2(column * _sectorSizeCells, (row + 1) * _sectorSizeCells));
            float side = PixelsPerSector;

            painter.strokeColor = HoverColour;
            painter.lineWidth = 1.5f;
            painter.BeginPath();
            painter.MoveTo(topLeft);
            painter.LineTo(topLeft + new Vector2(side, 0f));
            painter.LineTo(topLeft + new Vector2(side, side));
            painter.LineTo(topLeft + new Vector2(0f, side));
            painter.ClosePath();
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
    }
}

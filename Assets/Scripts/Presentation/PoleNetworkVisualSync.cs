using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Power;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Draws every pole cable and a small per-pole indicator (ENERGIE.md): a handful of short
    /// rotated sprite segments sampling a quadratic Bezier between two poles' centres, sagging
    /// downward with distance - a straight line reads badly, a slight droop barely costs two more
    /// lines of math. Rebuilt only when the edge list itself changes (a pole placed or removed); a
    /// fed/unfed colour change is a plain tint applied every Refresh, never a rebuild - no
    /// per-frame allocation either way.
    ///
    /// <b>The indicator exists because a cable is not always there to carry the distinction.</b> An
    /// isolated pole - no neighbour in range, so no cable at all - still needs to read as fed or not,
    /// which a colour on a cable it does not have cannot show.
    ///
    /// Drawn in SortingBands.PoleCable, the Information band: above every building regardless of
    /// depth, the same "overlay, not a thing standing in the world" reasoning a placement preview
    /// already gets.
    /// </summary>
    public sealed class PoleNetworkVisualSync
    {
        const int SegmentsPerCable = 10;
        const float SegmentThickness = 0.06f;
        const float IndicatorSize = 0.28f;

        static readonly Color FedColor = new Color(85f / 255f, 221f / 255f, 245f / 255f, 1f);
        static readonly Color UnfedColor = new Color(107f / 255f, 114f / 255f, 128f / 255f, 0.85f);

        readonly GridRuntime _grid;
        readonly PoleNetworkSettings _settings;
        readonly ProceduralSpriteFactory _spriteFactory = new ProceduralSpriteFactory();
        readonly Transform _root;

        readonly Dictionary<(PoleRuntime A, PoleRuntime B), SpriteRenderer[]> _cables = new Dictionary<(PoleRuntime, PoleRuntime), SpriteRenderer[]>();
        readonly Dictionary<PoleRuntime, SpriteRenderer> _indicators = new Dictionary<PoleRuntime, SpriteRenderer>();

        readonly HashSet<(PoleRuntime A, PoleRuntime B)> _seenCables = new HashSet<(PoleRuntime, PoleRuntime)>();
        readonly HashSet<PoleRuntime> _seenPoles = new HashSet<PoleRuntime>();
        readonly List<(PoleRuntime, PoleRuntime)> _staleCables = new List<(PoleRuntime, PoleRuntime)>();
        readonly List<PoleRuntime> _stalePoles = new List<PoleRuntime>();

        public PoleNetworkVisualSync(GridRuntime grid, PoleNetworkSettings settings)
        {
            _grid = grid;
            _settings = settings;

            _root = new GameObject("PoleCables").transform;
        }

        public void Refresh(PoleNetworkSystem network)
        {
            if (network == null) return;

            RefreshCables(network);
            RefreshIndicators(network);
        }

        void RefreshCables(PoleNetworkSystem network)
        {
            _seenCables.Clear();

            foreach (var edge in network.Edges)
            {
                _seenCables.Add(edge);

                if (!_cables.TryGetValue(edge, out SpriteRenderer[] segments))
                {
                    segments = BuildSegments();
                    _cables[edge] = segments;
                }

                bool visible = !edge.A.IsUnderConstruction && !edge.B.IsUnderConstruction;
                if (visible) PositionCable(edge.A, edge.B, segments);

                Color color = network.IsNetworkFed(edge.A.NetworkId) ? FedColor : UnfedColor;
                for (int i = 0; i < segments.Length; i++)
                {
                    segments[i].enabled = visible;
                    segments[i].color = color;
                }
            }

            _staleCables.Clear();
            foreach (var key in _cables.Keys)
            {
                if (!_seenCables.Contains(key)) _staleCables.Add(key);
            }
            foreach (var key in _staleCables)
            {
                foreach (SpriteRenderer segment in _cables[key]) Object.Destroy(segment.gameObject);
                _cables.Remove(key);
            }
        }

        void RefreshIndicators(PoleNetworkSystem network)
        {
            _seenPoles.Clear();

            foreach (PoleRuntime pole in network.Poles)
            {
                _seenPoles.Add(pole);

                if (!_indicators.TryGetValue(pole, out SpriteRenderer indicator))
                {
                    indicator = BuildIndicator();
                    _indicators[pole] = indicator;
                }

                bool visible = !pole.IsUnderConstruction;
                indicator.enabled = visible;
                if (!visible) continue;

                indicator.transform.position = AttachmentPoint(pole);
                indicator.color = network.IsNetworkFed(pole.NetworkId) ? FedColor : UnfedColor;
            }

            _stalePoles.Clear();
            foreach (PoleRuntime pole in _indicators.Keys)
            {
                if (!_seenPoles.Contains(pole)) _stalePoles.Add(pole);
            }
            foreach (PoleRuntime pole in _stalePoles)
            {
                Object.Destroy(_indicators[pole].gameObject);
                _indicators.Remove(pole);
            }
        }

        SpriteRenderer[] BuildSegments()
        {
            var segments = new SpriteRenderer[SegmentsPerCable];
            Sprite sprite = _spriteFactory.CreateSolidSquareSprite(Color.white);

            for (int i = 0; i < SegmentsPerCable; i++)
            {
                var go = new GameObject("CableSegment");
                go.transform.SetParent(_root, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.sortingOrder = SortingBands.PoleCable;
                segments[i] = renderer;
            }

            return segments;
        }

        SpriteRenderer BuildIndicator()
        {
            var go = new GameObject("PoleIndicator");
            go.transform.SetParent(_root, false);
            go.transform.localScale = new Vector3(IndicatorSize, IndicatorSize, 1f);

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = _spriteFactory.CreateRadialGlowSprite(Color.white);
            renderer.sortingOrder = SortingBands.PoleCable;
            return renderer;
        }

        /// <summary>
        /// Lays SegmentsPerCable rotated/scaled bars along a quadratic Bezier from a to b, its
        /// control point offset downward from the midpoint by SagPerCellDistance times the span in
        /// cells - a short cable barely droops, a long one visibly hangs.
        /// </summary>
        /// <summary>Where a cable actually attaches - near the top of the pole's art, not the ground it stands on. See PoleNetworkSettings.CableAttachmentHeightCells.</summary>
        Vector3 AttachmentPoint(PoleRuntime pole)
        {
            Vector3 groundCentre = _grid.FootprintCenterToWorld(pole.Cell, Vector2Int.one);
            return groundCentre + new Vector3(0f, _settings.CableAttachmentHeightCells * _grid.CellSize, 0f);
        }

        void PositionCable(PoleRuntime a, PoleRuntime b, SpriteRenderer[] segments)
        {
            Vector3 from = AttachmentPoint(a);
            Vector3 to = AttachmentPoint(b);

            float distanceCells = Vector3.Distance(from, to) / _grid.CellSize;
            float sag = _settings.SagPerCellDistance * distanceCells;
            Vector3 control = (from + to) * 0.5f + new Vector3(0f, -sag, 0f);

            int count = segments.Length;
            Vector3 previous = from;

            for (int i = 0; i < count; i++)
            {
                float t = (i + 1) / (float)count;
                Vector3 point = Bezier(from, control, to, t);

                Vector3 delta = point - previous;
                float length = delta.magnitude;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                Transform segmentTransform = segments[i].transform;
                segmentTransform.position = (previous + point) * 0.5f;
                segmentTransform.rotation = Quaternion.Euler(0f, 0f, angle);
                segmentTransform.localScale = new Vector3(length, SegmentThickness, 1f);

                previous = point;
            }
        }

        static Vector3 Bezier(Vector3 from, Vector3 control, Vector3 to, float t)
        {
            float u = 1f - t;
            return u * u * from + 2f * u * t * control + t * t * to;
        }
    }
}

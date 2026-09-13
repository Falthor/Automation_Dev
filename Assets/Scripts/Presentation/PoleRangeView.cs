using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Grid;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The square outline showing a pole's power field (ENERGIE.md) - four thin bars around
    /// PoleNetworkSettings.PowerRangeCells, since that reach is a Chebyshev square ("5x5"), not a
    /// circle like the Core's own ring (ActionRadiusView). Shown two ways: clicking a built pole
    /// (BuildingSelectionInput) and previewing one while it is still the armed construction tool
    /// (ConstructionInputAdapter.UpdateGhost, following the ghost every frame) - the two never
    /// overlap, since a tool being armed already routes clicks away from building inspection.
    ///
    /// <b>Deliberately not SelectionRuntime.SelectedBuilding.</b> That slot is reserved for a
    /// building with a real info panel to clear it (BuildingSelectionInput's own documented rule) -
    /// a Pole has none. This view owns its own show/hide state instead and never blocks other world
    /// input the way an open panel does.
    ///
    /// <b>Every building the field touches gets its own halo</b> (the same four-bar frame
    /// BuildingHoverHighlightView draws, pooled here since several can be touched at once) - a
    /// distinct BuildingRuntime per footprint, however many of the field's cells it occupies, so a
    /// 2x2 Communication Relay gets one halo, not up to four.
    /// </summary>
    public sealed class PoleRangeView
    {
        const float LineThickness = 0.08f;
        const float HaloThickness = 0.08f;

        static readonly Color LineColor = new Color(0.2f, 0.85f, 1f, 0.6f); // same accent ActionRadiusView's own ring uses
        static readonly Color HaloColor = new Color(0.3f, 0.6f, 1f, 0.95f); // matches BuildingHoverHighlightView's own default

        readonly GridRuntime _grid;
        readonly PoleNetworkSettings _settings;
        readonly GameObject _root;
        readonly SpriteRenderer[] _borders = new SpriteRenderer[4];
        readonly Sprite _haloSprite;

        readonly List<SpriteRenderer[]> _haloPool = new List<SpriteRenderer[]>();
        readonly List<BuildingRuntime> _touchedScratch = new List<BuildingRuntime>();

        PoleRuntime _togglePole;
        bool _visible;

        public PoleRangeView(GridRuntime grid, PoleNetworkSettings settings)
        {
            _grid = grid;
            _settings = settings;

            _root = new GameObject("PoleRangeIndicator");
            _root.SetActive(false);

            var spriteFactory = new ProceduralSpriteFactory();
            Sprite lineSprite = spriteFactory.CreateSolidSquareSprite(LineColor);
            _haloSprite = spriteFactory.CreateSolidSquareSprite(HaloColor);

            for (int i = 0; i < _borders.Length; i++)
            {
                var go = new GameObject("Border");
                go.transform.SetParent(_root.transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = lineSprite;
                renderer.sortingOrder = SortingBands.ActionRadius;
                _borders[i] = renderer;
            }
        }

        /// <summary>Shows a built pole's range, or hides it if it was already the one shown - a second click on the same pole dismisses it.</summary>
        public void Toggle(PoleRuntime pole)
        {
            if (_visible && ReferenceEquals(_togglePole, pole))
            {
                Hide();
                return;
            }

            _togglePole = pole;
            Show(pole.Cell, pole);
        }

        /// <summary>Shows the range centred on a cell - a placed pole (via Toggle) or a placement-preview candidate that may not be built yet. exclude keeps the pole itself out of its own touched-buildings halo.</summary>
        public void Show(GridCoord centreCell, BuildingRuntime exclude)
        {
            _visible = true;
            _root.SetActive(true);

            Vector3 centre = _grid.FootprintCenterToWorld(centreCell, Vector2Int.one);
            float half = (_settings.PowerRangeCells + 0.5f) * _grid.CellSize;
            float side = half * 2f;

            Place(_borders[0], centre + new Vector3(0f, half, 0f), new Vector3(side, LineThickness, 1f));
            Place(_borders[1], centre + new Vector3(0f, -half, 0f), new Vector3(side, LineThickness, 1f));
            Place(_borders[2], centre + new Vector3(half, 0f, 0f), new Vector3(LineThickness, side, 1f));
            Place(_borders[3], centre + new Vector3(-half, 0f, 0f), new Vector3(LineThickness, side, 1f));

            ShowHalos(centreCell, exclude);
        }

        public void Hide()
        {
            if (!_visible) return;

            _visible = false;
            _togglePole = null;
            _root.SetActive(false);
            for (int i = 0; i < _haloPool.Count; i++) SetHaloActive(_haloPool[i], false);
        }

        void ShowHalos(GridCoord centreCell, BuildingRuntime exclude)
        {
            _touchedScratch.Clear();

            int r = _settings.PowerRangeCells;
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    var cell = new GridCoord(centreCell.X + dx, centreCell.Y + dy);
                    if (!(_grid.GetOccupant(cell) is BuildingRuntime building)) continue;
                    if (ReferenceEquals(building, exclude)) continue;
                    if (!_touchedScratch.Contains(building)) _touchedScratch.Add(building);
                }
            }

            for (int i = 0; i < _touchedScratch.Count; i++)
            {
                BuildingRuntime building = _touchedScratch[i];
                SpriteRenderer[] halo = GetOrCreateHalo(i);

                Vector3 min = _grid.CellToWorld(building.Cell);
                float width = building.Definition.FootprintSize.x * _grid.CellSize;
                float height = building.Definition.FootprintSize.y * _grid.CellSize;
                PlaceHaloRect(halo, min.x, min.y, width, height);
                SetHaloActive(halo, true);
            }

            for (int i = _touchedScratch.Count; i < _haloPool.Count; i++) SetHaloActive(_haloPool[i], false);
        }

        SpriteRenderer[] GetOrCreateHalo(int index)
        {
            if (index < _haloPool.Count) return _haloPool[index];

            var bars = new SpriteRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("TouchedBuildingHalo");
                go.transform.SetParent(_root.transform, false);
                var renderer = go.AddComponent<SpriteRenderer>();
                renderer.sprite = _haloSprite;
                renderer.sortingOrder = SortingBands.HoverOutline;
                bars[i] = renderer;
            }

            _haloPool.Add(bars);
            return bars;
        }

        static void PlaceHaloRect(SpriteRenderer[] bars, float minX, float minY, float width, float height)
        {
            Place(bars[0], new Vector3(minX + width * 0.5f, minY + height - HaloThickness * 0.5f, 0f), new Vector3(width, HaloThickness, 1f));
            Place(bars[1], new Vector3(minX + width * 0.5f, minY + HaloThickness * 0.5f, 0f), new Vector3(width, HaloThickness, 1f));
            Place(bars[2], new Vector3(minX + HaloThickness * 0.5f, minY + height * 0.5f, 0f), new Vector3(HaloThickness, height, 1f));
            Place(bars[3], new Vector3(minX + width - HaloThickness * 0.5f, minY + height * 0.5f, 0f), new Vector3(HaloThickness, height, 1f));
        }

        static void SetHaloActive(SpriteRenderer[] bars, bool active)
        {
            for (int i = 0; i < bars.Length; i++) bars[i].enabled = active;
        }

        static void Place(SpriteRenderer renderer, Vector3 position, Vector3 scale)
        {
            renderer.transform.position = position;
            renderer.transform.localScale = scale;
        }
    }
}

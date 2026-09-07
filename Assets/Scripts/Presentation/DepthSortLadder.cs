using System.Collections.Generic;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Hands out the sorted band's draw orders, against a window that follows the camera.
    ///
    /// <b>Why it exists.</b> Depth used to be ranked off absolute world Y. sortingOrder is a short,
    /// and a 10 000-cell map at four steps per unit and four sub-layers needs 160 000 values - so the
    /// scheme does not merely run out, it runs out silently, because the rank clamps and everything
    /// past the last row collapses onto one order.
    ///
    /// <b>The way out.</b> Only objects on screen at the same time need to be ordered against each
    /// other, and the zoom-out cap bounds how much of the world that can be. The ladder therefore
    /// ranks against a window a few times taller than the tallest possible view, and re-anchors that
    /// window when the camera approaches its edge. The window's size is a constant of the ladder, so
    /// a 300-cell map and a 10 000-cell map cost exactly the same orders.
    ///
    /// <b>Why panning does not re-rank anything.</b> Every rank shifts by the same amount when the
    /// window moves, so the relative order of two objects is unchanged - which is all Unity reads.
    /// Ranks are only recomputed on a re-anchoring, and that happens once per ~98 world units of
    /// vertical panning, not per frame.
    ///
    /// An instance rather than static state on <see cref="SortingBands"/>, deliberately: this project
    /// runs with Domain Reload disabled (DEVELOPMENT_RULES.md §5), so a mutable static window origin
    /// would survive into the next Play session and hand out ranks measured against a camera that no
    /// longer exists.
    /// </summary>
    public sealed class DepthSortLadder
    {
        /// <summary>One entry per renderer in the sorted band. The depth key is captured once: nothing in this band moves - a relocated building respawns its view, and robots are in the flying band.</summary>
        readonly struct Entry
        {
            public readonly SpriteRenderer Renderer;
            public readonly float WorldBottomY;
            public readonly int SubLayer;

            public Entry(SpriteRenderer renderer, float worldBottomY, int subLayer)
            {
                Renderer = renderer;
                WorldBottomY = worldBottomY;
                SubLayer = subLayer;
            }
        }

        /// <summary>The window's height in world units - the ladder's whole addressable span.</summary>
        public const float WindowWorldHeight = SortingBands.AddressableRows;

        readonly List<Entry> _entries = new List<Entry>();
        readonly float _maxViewHalfHeight;

        /// <summary>How far the camera may travel from the anchor before the window has to move. Derived, never set: it is what is left of the window once the tallest possible view is centred in it.</summary>
        readonly float _slack;

        float _windowTopY;
        float _anchorCameraY;
        bool _anchored;
        bool _reportedTooSmall;

        public DepthSortLadder(float maxViewHalfHeight)
        {
            _maxViewHalfHeight = Mathf.Max(0f, maxViewHalfHeight);
            _slack = WindowWorldHeight * 0.5f - _maxViewHalfHeight;

            // Centred on the origin until the first FollowCamera. Views spawned before then - in a
            // Start(), or in a test with no camera at all - still rank against each other correctly
            // as long as they are within half a window of y = 0, instead of all clamping to one
            // order and losing their depth entirely.
            _windowTopY = WindowWorldHeight * 0.5f;
        }

        /// <summary>How many renderers currently hold a rank from this ladder.</summary>
        public int TrackedCount => _entries.Count;

        /// <summary>World Y of the top of the current window. Ranks grow downward from it.</summary>
        public float WindowTopY => _windowTopY;

        /// <summary>Re-anchorings so far. Watched by a test: panning across a map must not re-anchor once per frame.</summary>
        public int AnchorCount { get; private set; }

        /// <summary>
        /// The rank for something whose lowest point is at <paramref name="worldBottomY"/>. Valid
        /// only against the current window - do not store it, and do not compare it with one taken
        /// before a re-anchoring.
        /// </summary>
        public int Order(float worldBottomY, int subLayer)
            => SortingBands.SortedFromDepth(_windowTopY - worldBottomY, subLayer);

        /// <summary>
        /// Starts ranking a renderer and applies its rank now. The depth key is passed in rather
        /// than read off the renderer's bounds: a building is ranked by the bottom of its
        /// <b>footprint</b>, which is not the bottom of art that overhangs it.
        /// </summary>
        public void Register(SpriteRenderer renderer, float worldBottomY, int subLayer)
        {
            if (renderer == null) return;

            _entries.Add(new Entry(renderer, worldBottomY, subLayer));
            renderer.sortingOrder = Order(worldBottomY, subLayer);
        }

        /// <summary>Stops ranking a renderer. Missing one is not a leak that grows without bound - a destroyed renderer is dropped at the next re-anchoring - but it does keep the list longer than it needs to be.</summary>
        public void Unregister(SpriteRenderer renderer)
        {
            if (renderer == null) return;

            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Renderer == renderer) _entries.RemoveAt(i);
            }
        }

        /// <summary>
        /// Moves the window if the camera has left its slack, and answers whether it did.
        ///
        /// Called every frame; it does nothing at all on the vast majority of them - one subtraction
        /// and one comparison. The work only happens on a re-anchoring.
        /// </summary>
        public bool FollowCamera(float cameraY)
        {
            if (_anchored && Mathf.Abs(cameraY - _anchorCameraY) <= _slack) return false;

            // A window that cannot contain the widest view would re-anchor every frame and still
            // clamp visible objects. Reported once rather than every frame, and the ladder keeps
            // working as well as it can - a wrong draw order beats a log flood.
            if (_slack <= 0f && !_reportedTooSmall)
            {
                _reportedTooSmall = true;
                Debug.LogError($"DepthSortLadder: the zoom-out cap ({_maxViewHalfHeight * 2f} world units tall) does not fit "
                    + $"in the {WindowWorldHeight}-unit ladder window. Raise SortingBands.AddressableRows or lower the cap.");
            }

            _anchorCameraY = cameraY;
            _windowTopY = cameraY + WindowWorldHeight * 0.5f;
            _anchored = true;
            AnchorCount++;

            Reapply();
            return true;
        }

        /// <summary>Re-ranks everything against the new window, dropping renderers destroyed since the last pass. Allocates nothing.</summary>
        void Reapply()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                Entry entry = _entries[i];

                if (entry.Renderer == null)
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                entry.Renderer.sortingOrder = Order(entry.WorldBottomY, entry.SubLayer);
            }
        }
    }
}

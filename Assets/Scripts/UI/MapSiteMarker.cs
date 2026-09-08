using UnityEngine;

namespace Game.UI
{
    /// <summary>
    /// What a site looks like on the map. Four states, all distinguishable without reading a word.
    ///
    /// <b>A site that is done does not disappear.</b> A point that vanished would make the zone look
    /// like it was emptying, when the player has in fact gained something there. Keeping the mark turns
    /// the map into the record of what has been done, and lets progress be read without the bar.
    /// </summary>
    public enum MapSiteState
    {
        /// <summary>Filled, in its kind's colour. Launchable now.</summary>
        Available,

        /// <summary>A wider ring and the one label shown without hovering. The game putting a first recovery forward, once.</summary>
        Highlighted,

        /// <summary>Muted and <b>never clickable</b>. It has given what it held.</summary>
        Done,

        /// <summary>An empty circle with a muted outline: it needs units, so it promises rather than offers.</summary>
        Locked
    }

    /// <summary>
    /// One site, in the terms the map draws it in.
    ///
    /// <b>A view model, filled by the panel from the zone system.</b> Which state a site is in is a UI
    /// reading of gameplay facts - consumed, hidden, needs units - and belongs beside the labels rather
    /// than inside the element, which only draws what it is handed.
    /// </summary>
    public readonly struct MapSiteMarker
    {
        /// <summary>Where it stands, in cell space - the same space the terrain and the Core are measured in.</summary>
        public readonly Vector2 CellPosition;

        public readonly MapSiteState State;

        /// <summary>Its kind's colour. Supplied rather than looked up, so kind-to-colour lives in one place with the kind's label.</summary>
        public readonly Color Tint;

        /// <summary>Shown permanently for <see cref="MapSiteState.Highlighted"/>, and on hover for the rest.</summary>
        public readonly string Label;

        public MapSiteMarker(Vector2 cellPosition, MapSiteState state, Color tint, string label)
        {
            CellPosition = cellPosition;
            State = state;
            Tint = tint;
            Label = label;
        }

        public bool SameAs(MapSiteMarker other)
            => CellPosition == other.CellPosition && State == other.State && Label == other.Label;
    }
}

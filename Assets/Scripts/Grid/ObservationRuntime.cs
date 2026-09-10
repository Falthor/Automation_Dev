using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Grid
{
    /// <summary>One thing that can see, and how far. A disc in cell space - not a building, not a robot, so anything that will ever see can be one without this knowing what it is.</summary>
    public readonly struct ObserverDisc
    {
        public readonly Vector2 CentreCells;
        public readonly float RadiusCells;

        public ObserverDisc(Vector2 centreCells, float radiusCells)
        {
            CentreCells = centreCells;
            RadiusCells = radiusCells;
        }

        public bool Matches(ObserverDisc other)
            => CentreCells == other.CentreCells && RadiusCells.Equals(other.RadiusCells);
    }

    /// <summary>
    /// Who is looking at what, right now.
    ///
    /// <b>Observation is computed, never stored.</b> Discovery is permanent and acquired
    /// (<see cref="DiscoveryRuntime"/>); this is rebuilt from the current position of the observers
    /// every frame and holds nothing per cell at all. Nothing is written when a robot advances and
    /// nothing erased when it moves away - the cell simply stops being covered by anything in the
    /// list. A stored observation state would be a second source of truth, free to contradict where
    /// the observers actually are.
    ///
    /// It follows that <b>nothing here enters the save</b>, and there is deliberately no
    /// Capture/Restore pair to forget to call: a loaded game rebuilds the field on its first frame
    /// from the observers it restored.
    ///
    /// <b>No allocation per frame.</b> Both lists keep their capacity across rebuilds, and every
    /// walk is an indexed <c>for</c> over a concrete <see cref="List{T}"/> - a <c>foreach</c> over
    /// the read-only interface would box an enumerator once a frame.
    /// </summary>
    public sealed class ObservationRuntime
    {
        readonly List<ObserverDisc> _observers = new List<ObserverDisc>();

        /// <summary>Last frame's set, kept so <see cref="Version"/> only moves when the answer actually changed - a still robot must not make the fog re-upload sixty times a second.</summary>
        readonly List<ObserverDisc> _previous = new List<ObserverDisc>();

        bool _rebuilding;

        /// <summary>
        /// Advances only when a rebuild produced a different set of observers than the one before it.
        /// The fog renderer compares it against what it last uploaded, exactly as it does with
        /// <see cref="DiscoveryRuntime.Version"/>.
        /// </summary>
        public int Version { get; private set; }

        public int ObserverCount => _observers.Count;

        public ObserverDisc ObserverAt(int index) => _observers[index];

        /// <summary>Starts a rebuild. Everything between this and <see cref="EndRebuild"/> replaces the whole set - there is no incremental add or remove, because the field is not state to maintain.</summary>
        public void BeginRebuild()
        {
            _observers.Clear();
            _rebuilding = true;
        }

        /// <summary>Adds one observer. A radius of zero or less sees nothing and is dropped rather than stored as a disc that can never contain anything.</summary>
        public void Add(Vector2 centreCells, float radiusCells)
        {
            if (!_rebuilding || radiusCells <= 0f) return;

            _observers.Add(new ObserverDisc(centreCells, radiusCells));
        }

        /// <summary>Closes the rebuild, and bumps <see cref="Version"/> if this set differs from the previous one.</summary>
        public void EndRebuild()
        {
            if (!_rebuilding) return;
            _rebuilding = false;

            if (SameAsPrevious()) return;

            _previous.Clear();
            for (int i = 0; i < _observers.Count; i++) _previous.Add(_observers[i]);

            Version++;
        }

        bool SameAsPrevious()
        {
            if (_observers.Count != _previous.Count) return false;

            for (int i = 0; i < _observers.Count; i++)
            {
                if (!_observers[i].Matches(_previous[i])) return false;
            }

            return true;
        }

        /// <summary>
        /// Whether anything is looking at this cell.
        ///
        /// Measured on the cell's <b>centre</b>, which is the same rule
        /// <see cref="DiscoveryRuntime.RevealDisc"/> uses to decide what a disc covers - so a robot's
        /// observation disc and the ground it reveals have the same edge rather than two edges a
        /// half-cell apart.
        ///
        /// This does not ask whether the cell was ever discovered: see <see cref="StateOf"/>, which
        /// is where the two facts meet.
        /// </summary>
        public bool IsObserved(GridCoord cell)
        {
            float x = cell.X + 0.5f;
            float y = cell.Y + 0.5f;

            for (int i = 0; i < _observers.Count; i++)
            {
                ObserverDisc observer = _observers[i];
                float dx = x - observer.CentreCells.x;
                float dy = y - observer.CentreCells.y;

                if (dx * dx + dy * dy <= observer.RadiusCells * observer.RadiusCells) return true;
            }

            return false;
        }

        /// <summary>
        /// The three-state answer: what the player should be shown for this cell.
        ///
        /// <b>Discovery gates observation, and that order is the rule.</b> A cell nobody has ever
        /// discovered is <see cref="DiscoveryState.Unknown"/> even with an observer standing on it -
        /// which cannot happen in the running game, because everything that observes also reveals,
        /// but it is what makes "never discovered never becomes remembered" true by construction
        /// rather than by everything happening to call things in the right order.
        ///
        /// A null discovery answers Unknown for everything: a world with no discovery state has
        /// nothing to remember, which is truthful rather than convenient.
        /// </summary>
        public DiscoveryState StateOf(GridCoord cell, DiscoveryRuntime discovery)
        {
            if (discovery == null || !discovery.IsDiscovered(cell)) return DiscoveryState.Unknown;

            return IsObserved(cell) ? DiscoveryState.Observed : DiscoveryState.Remembered;
        }
    }
}

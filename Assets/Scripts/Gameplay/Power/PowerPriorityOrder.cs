using System.Collections.Generic;

namespace Game.Gameplay.Power
{
    /// <summary>
    /// The order in which building types receive the current when there is not enough of it -
    /// the player's own arbitration, and the thing that keeps a shortage from becoming a dead end.
    ///
    /// <b>An order of type identifiers, never of positions.</b> That is what makes the list
    /// extensible without a migration: a type added to the game tomorrow has no position to
    /// restore, so it lands at the bottom rather than shifting everything the player arranged. The
    /// three restore rules are <see cref="RestoreState"/>'s whole job, and the one test this
    /// deserves.
    ///
    /// <b>It holds every known type, not only the ones on screen.</b> The panel may filter what it
    /// draws; the order underneath covers the whole catalogue, so a type the player has never built
    /// still has the place they gave it - and appears there the day they build one, instead of
    /// reshuffling the list under them.
    ///
    /// A type absent from this order is served last, after everything in it, in the order the power
    /// system happened to hear from them. That is the sane default for something nobody has
    /// arbitrated yet, and it is what a fresh run does before the player touches this screen.
    /// </summary>
    public sealed class PowerPriorityOrder
    {
        readonly List<string> _order = new List<string>();

        /// <summary>The current order, most-served first.</summary>
        public IReadOnlyList<string> Order => _order;

        /// <summary>
        /// Where a type sits, or <c>int.MaxValue</c> for one this order has never heard of - which
        /// sorts it after everything else rather than in front of it.
        /// </summary>
        public int RankOf(string typeId)
        {
            int index = _order.IndexOf(typeId);
            return index < 0 ? int.MaxValue : index;
        }

        /// <summary>
        /// Adds every type the order does not already know, at the bottom, in the order given.
        ///
        /// Called with the building catalogue so a fresh run has a complete, deterministic order
        /// without anybody writing one down: the catalogue's own order is the default arbitration,
        /// and the player rearranges from there.
        /// </summary>
        public void EnsureKnows(IEnumerable<string> typeIds)
        {
            if (typeIds == null) return;

            foreach (string id in typeIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (_order.Contains(id)) continue;
                _order.Add(id);
            }
        }

        /// <summary>
        /// Moves one type to an index, sliding the rest along - the drag gesture's one effect on
        /// the model. An unknown type is added rather than refused: the caller is a pointer, and it
        /// has nowhere to report to.
        /// </summary>
        public void MoveTo(string typeId, int index)
        {
            if (string.IsNullOrEmpty(typeId)) return;

            _order.Remove(typeId);
            if (index < 0) index = 0;
            if (index > _order.Count) index = _order.Count;
            _order.Insert(index, typeId);
        }

        /// <summary>What the save writes: the order as it stands, and nothing else.</summary>
        public List<string> CaptureState() => new List<string>(_order);

        /// <summary>
        /// Rebuilds the order from a save, against the types the game currently has.
        ///
        /// Three rules, and they are the reason this is an order of identifiers:
        /// <list type="bullet">
        /// <item>a saved identifier the game still has keeps its place;</item>
        /// <item>a type the game has and the save does not - added since - goes to the bottom,
        /// rather than breaking the arrangement the player made;</item>
        /// <item>a saved identifier the game no longer has is dropped without a word, because a
        /// removed building type is not an error in somebody's save.</item>
        /// </list>
        ///
        /// <paramref name="knownTypeIds"/> is the authority on what exists, and its order is the
        /// tie-break for everything the save did not mention.
        /// </summary>
        public void RestoreState(IReadOnlyList<string> savedOrder, IEnumerable<string> knownTypeIds)
        {
            var known = new List<string>();
            if (knownTypeIds != null)
            {
                foreach (string id in knownTypeIds)
                {
                    if (!string.IsNullOrEmpty(id) && !known.Contains(id)) known.Add(id);
                }
            }

            _order.Clear();

            if (savedOrder != null)
            {
                for (int i = 0; i < savedOrder.Count; i++)
                {
                    string id = savedOrder[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!known.Contains(id)) continue;      // gone from the game: dropped silently
                    if (_order.Contains(id)) continue;       // a duplicate in the blob is not fatal
                    _order.Add(id);
                }
            }

            EnsureKnows(known);                              // added since the save: to the bottom
        }
    }
}

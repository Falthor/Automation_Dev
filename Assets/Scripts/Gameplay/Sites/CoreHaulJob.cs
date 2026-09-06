using System.Collections.Generic;
using Game.Gameplay.Buildings;

namespace Game.Gameplay.Sites
{
    /// <summary>
    /// Materials to collect from the world's containers and carry to one building that already
    /// exists - what validating a Core directive creates. Served by the same robots, out of the
    /// same reserved stock, as a construction site.
    ///
    /// Not a construction site, though: nothing is built at the end, and the destination is a
    /// building that is already standing. Its bookkeeping is deliberately thinner than
    /// ConstructionSiteRuntime's rather than shared with it - a site tracks delivered/reserved/
    /// missing per ingredient because its panel shows all three and its segments materialize one at
    /// a time; a haul only has to know what is still unspoken for, what is promised, and what has
    /// landed. Sharing would have meant lifting a hundred lines of segment-shaped bookkeeping into
    /// something with no segments.
    ///
    /// The method names deliberately match the site's, because ConstructionSiteSystem's dispatch
    /// treats the two the same way and reads better when the two sides are spelled alike.
    /// </summary>
    public sealed class CoreHaulJob
    {
        readonly Dictionary<string, int> _needed = new Dictionary<string, int>();
        readonly Dictionary<string, int> _delivered = new Dictionary<string, int>();

        /// <summary>Earmarked in a container, or already riding in a robot's cargo. Same meaning as a site's committed count: this material is spoken for and nothing else may take it.</summary>
        readonly Dictionary<string, int> _committed = new Dictionary<string, int>();

        readonly List<Reservation> _reservations = new List<Reservation>();

        public BuildingRuntime Destination { get; }
        public IReadOnlyList<Reservation> Reservations => _reservations;
        public IReadOnlyDictionary<string, int> Needed => _needed;
        public IReadOnlyDictionary<string, int> Delivered => _delivered;

        public CoreHaulJob(BuildingRuntime destination, IReadOnlyDictionary<string, int> items)
        {
            Destination = destination;
            foreach (var kvp in items)
            {
                if (kvp.Value > 0) _needed[kvp.Key] = kvp.Value;
            }
        }

        public int DeliveredOf(string itemId) => _delivered.TryGetValue(itemId, out int amount) ? amount : 0;
        public int NeededOf(string itemId) => _needed.TryGetValue(itemId, out int amount) ? amount : 0;

        /// <summary>Still to earmark: what was asked for, minus what has landed, minus what is already promised.</summary>
        public int RemainingToReserve(string itemId)
        {
            int committed = _committed.TryGetValue(itemId, out int c) ? c : 0;
            return NeededOf(itemId) - DeliveredOf(itemId) - committed;
        }

        /// <summary>Every unit asked for has physically landed at the destination.</summary>
        public bool IsComplete
        {
            get
            {
                foreach (var kvp in _needed)
                {
                    if (DeliveredOf(kvp.Key) < kvp.Value) return false;
                }
                return true;
            }
        }

        /// <summary>Merged into an existing earmark for the same container and item, so a robot fills up in one trip instead of one unit per trip - the same reason ConstructionSiteRuntime.AddReservation merges.</summary>
        public void AddReservation(object container, string itemId, int amount)
        {
            if (amount <= 0) return;

            for (int i = 0; i < _reservations.Count; i++)
            {
                Reservation existing = _reservations[i];
                if (!ReferenceEquals(existing.Container, container) || existing.ItemId != itemId) continue;

                existing.Amount += amount;
                _reservations[i] = existing;
                _committed[itemId] = (_committed.TryGetValue(itemId, out int c) ? c : 0) + amount;
                return;
            }

            _reservations.Add(new Reservation { Container = container, ItemId = itemId, Amount = amount });
            _committed[itemId] = (_committed.TryGetValue(itemId, out int existingCommitted) ? existingCommitted : 0) + amount;
        }

        public int ReservedIn(object container, string itemId)
        {
            int total = 0;
            foreach (Reservation reservation in _reservations)
            {
                if (ReferenceEquals(reservation.Container, container) && reservation.ItemId == itemId) total += reservation.Amount;
            }
            return total;
        }

        /// <summary>A robot is on its way to collect these: the earmark leaves the container's books, the promise stays (it has simply moved into the robot).</summary>
        public int ReleaseReservationForPickup(object container, string itemId, int amount)
        {
            int released = 0;
            for (int i = _reservations.Count - 1; i >= 0 && released < amount; i--)
            {
                Reservation reservation = _reservations[i];
                if (!ReferenceEquals(reservation.Container, container) || reservation.ItemId != itemId) continue;

                int take = System.Math.Min(reservation.Amount, amount - released);
                released += take;
                reservation.Amount -= take;
                if (reservation.Amount <= 0) _reservations.RemoveAt(i);
                else _reservations[i] = reservation;
            }
            return released;
        }

        public void RegisterDelivery(string itemId, int amount)
        {
            if (amount <= 0) return;
            _delivered[itemId] = DeliveredOf(itemId) + amount;
            if (_committed.TryGetValue(itemId, out int committed))
            {
                _committed[itemId] = System.Math.Max(0, committed - amount);
            }
        }

        /// <summary>A robot picked up less than it was sent for - the shortfall stops being promised so a later pass can earmark it again.</summary>
        public void ReleaseCommitment(string itemId, int amount)
        {
            if (amount <= 0 || !_committed.TryGetValue(itemId, out int committed)) return;
            _committed[itemId] = System.Math.Max(0, committed - amount);
        }
    }
}

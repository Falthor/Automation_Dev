using System.Collections.Generic;
using Game.Data;
using Game.Gameplay.Buildings;
using Newtonsoft.Json.Linq;

namespace Game.Gameplay.Sites
{
    /// <summary>
    /// A single earmarked promise: `amount` of `ItemId` sitting inside `Container` (a StorageRuntime
    /// or a ProductionBuildingRuntime's output), reserved for one ConstructionSiteRuntime and no
    /// longer available to anything else - the "couples contenant-quantité" TASK_05_ROBOT_CONSTRUCTEUR.md
    /// §1 requires instead of a single total. Physically still sitting in Container until a robot
    /// actually picks it up.
    /// </summary>
    public struct Reservation
    {
        public object Container;
        public string ItemId;
        public int Amount;
    }

    /// <summary>
    /// A construction site (TASK_05_ROBOT_CONSTRUCTEUR.md §3/§7): one or more already-constructed
    /// but not-yet-registered BuildingRuntime segments (one for a normal building, several in
    /// placement order for a dragged conveyor/splitter run), plus the bill of materials still owed
    /// and the reservations already earmarked toward it. A segment's BuildingRuntime is real from
    /// the moment it is placed (already occupying its grid cell, already correctly configured) - it
    /// simply is not yet registered with TransportSystem and has no spawned view, so it neither
    /// ticks, transports, nor produces anything (PROJECT_ARCHITECTURE.md §12/§13) until this site
    /// materializes it.
    /// </summary>
    public sealed class ConstructionSiteRuntime
    {
        public int Id { get; }

        readonly List<BuildingRuntime> _segments = new List<BuildingRuntime>();
        readonly Dictionary<string, int> _totalCost = new Dictionary<string, int>();
        readonly Dictionary<string, int> _delivered = new Dictionary<string, int>();

        /// <summary>Reserved-in-container-but-not-yet-picked-up, OR picked-up-but-not-yet-delivered (in a robot's cargo) - see RemainingNeeded. Distinct from _delivered, which only grows once a robot actually drops items off here.</summary>
        readonly Dictionary<string, int> _committed = new Dictionary<string, int>();

        /// <summary>
        /// Running total of what the already-materialized segments consumed, maintained as they
        /// materialize instead of being re-summed on demand. SegmentProgress is read once per
        /// segment per frame by the construction view, and re-walking the preceding segments on
        /// every call would be quadratic in the length of a conveyor drag - harmless on three
        /// segments, not on fifty.
        /// </summary>
        readonly Dictionary<string, int> _consumedByMaterialized = new Dictionary<string, int>();

        readonly List<Reservation> _reservations = new List<Reservation>();

        public IReadOnlyList<BuildingRuntime> Segments => _segments;
        public IReadOnlyList<Reservation> Reservations => _reservations;
        public int MaterializedCount { get; private set; }
        public bool IsComplete => MaterializedCount >= _segments.Count;

        /// <summary>The building/segment kind this site is building, for UI/messaging - the first segment's definition (every segment of a conveyor/splitter run shares the same BuildingDefinition category, if not always the exact same definition instance for corner-vs-straight).</summary>
        public BuildingDefinition PrimaryDefinition => _segments.Count > 0 ? _segments[0].Definition : null;

        public ConstructionSiteRuntime(int id, BuildingRuntime firstSegment)
        {
            Id = id;
            AddSegment(firstSegment);
        }

        public void AddSegment(BuildingRuntime segment)
        {
            _segments.Add(segment);
            foreach (RecipeIngredient ingredient in segment.Definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;
                _totalCost[ingredient.Item.Id] = (_totalCost.TryGetValue(ingredient.Item.Id, out int existing) ? existing : 0) + ingredient.Amount;
            }
        }

        public IReadOnlyDictionary<string, int> TotalCost => _totalCost;
        public IReadOnlyDictionary<string, int> Delivered => _delivered;

        /// <summary>Still owed for this item: total cost, minus what has physically landed here, minus what is already promised (reserved in a container, or riding in a robot's cargo).</summary>
        public int RemainingNeeded(string itemId)
        {
            int cost = _totalCost.TryGetValue(itemId, out int c) ? c : 0;
            int delivered = _delivered.TryGetValue(itemId, out int d) ? d : 0;
            int committed = _committed.TryGetValue(itemId, out int p) ? p : 0;
            return cost - delivered - committed;
        }

        /// <summary>Every item this site still needs more of right now (cost not yet delivered or promised) - used to name missing materials for the échec/notification state.</summary>
        public IReadOnlyDictionary<string, int> GetStillNeeded()
        {
            var result = new Dictionary<string, int>();
            foreach (var kvp in _totalCost)
            {
                int remaining = RemainingNeeded(kvp.Key);
                if (remaining > 0) result[kvp.Key] = remaining;
            }
            return result;
        }

        /// <summary>
        /// Records a new earmark against a container - does not move anything physically yet.
        /// Merged into an existing earmark for the same container and item rather than appended:
        /// a conveyor drag reserves one plate per segment as it grows, and 55 separate one-unit
        /// entries would have a robot fetch a single plate per trip instead of filling its
        /// 4-unit capacity - fifty-five trips where the design calls for seven waves.
        /// </summary>
        public void AddReservation(object container, string itemId, int amount)
        {
            if (amount <= 0) return;

            for (int i = 0; i < _reservations.Count; i++)
            {
                Reservation existingReservation = _reservations[i];
                if (!ReferenceEquals(existingReservation.Container, container) || existingReservation.ItemId != itemId) continue;

                existingReservation.Amount += amount;
                _reservations[i] = existingReservation;
                _committed[itemId] = (_committed.TryGetValue(itemId, out int committed) ? committed : 0) + amount;
                return;
            }

            _reservations.Add(new Reservation { Container = container, ItemId = itemId, Amount = amount });
            _committed[itemId] = (_committed.TryGetValue(itemId, out int existing) ? existing : 0) + amount;
        }

        /// <summary>How much of one item this site has earmarked in one specific container - what a robot can pick up there in a single trip, up to its own capacity.</summary>
        public int ReservedIn(object container, string itemId)
        {
            int total = 0;
            foreach (Reservation reservation in _reservations)
            {
                if (ReferenceEquals(reservation.Container, container) && reservation.ItemId == itemId) total += reservation.Amount;
            }
            return total;
        }

        /// <summary>
        /// Consumes up to `amount` of `itemId` from this site's own reservations still sitting in
        /// `container` (a robot about to pick them up) - reduces/removes the matching Reservation
        /// entries so nothing else can claim the same earmark twice, but leaves _committed
        /// untouched (the promise just moved from "in the container" to "in the robot's cargo").
        /// Returns how much was actually released (may be less than requested).
        /// </summary>
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

        /// <summary>A robot just delivered cargo here - moves it from "committed" into "delivered" and checks whether any leading segment can now materialize.</summary>
        public void RegisterDelivery(string itemId, int amount)
        {
            if (amount <= 0) return;
            _delivered[itemId] = (_delivered.TryGetValue(itemId, out int existing) ? existing : 0) + amount;
            if (_committed.TryGetValue(itemId, out int committed))
            {
                _committed[itemId] = System.Math.Max(0, committed - amount);
            }
        }

        /// <summary>A robot carrying cargo for this site is dropping it elsewhere instead (cancellation) - releases the commitment without ever counting as delivered.</summary>
        public void ReleaseCommitment(string itemId, int amount)
        {
            if (amount <= 0 || !_committed.TryGetValue(itemId, out int committed)) return;
            _committed[itemId] = System.Math.Max(0, committed - amount);
        }

        /// <summary>Releases every reservation this site still holds in containers (not yet picked up) - used on cancellation.</summary>
        public void ReleaseAllContainerReservations()
        {
            foreach (Reservation reservation in _reservations)
            {
                if (_committed.TryGetValue(reservation.ItemId, out int committed))
                {
                    _committed[reservation.ItemId] = System.Math.Max(0, committed - reservation.Amount);
                }
            }
            _reservations.Clear();
        }

        /// <summary>
        /// Whether the next not-yet-materialized segment's own cost has been fully delivered -
        /// i.e. it can become a real, registered building right now. Segments materialize strictly
        /// in placement order (TASK_05_ROBOT_CONSTRUCTEUR.md §3: "les segments se matérialisent au
        /// fur et à mesure le long du tracé").
        /// </summary>
        public bool CanMaterializeNextSegment()
        {
            if (MaterializedCount >= _segments.Count) return false;

            BuildingRuntime segment = _segments[MaterializedCount];
            foreach (RecipeIngredient ingredient in segment.Definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;
                int delivered = _delivered.TryGetValue(ingredient.Item.Id, out int d) ? d : 0;
                int consumedBySegmentsBefore = ConsumedByMaterializedSegments(ingredient.Item.Id);
                if (delivered - consumedBySegmentsBefore < ingredient.Amount) return false;
            }
            return true;
        }

        int ConsumedByMaterializedSegments(string itemId)
        {
            return _consumedByMaterialized.TryGetValue(itemId, out int total) ? total : 0;
        }

        /// <summary>
        /// How far along segment `index` is, 0 to 1 - what a view needs to draw it materializing,
        /// and the only form in which this is exposed: the rule that decides which delivery feeds
        /// which segment stays here rather than being re-derived by whoever draws it.
        ///
        /// Segments materialize strictly in placement order and consume the delivered pile in that
        /// same order, so a segment past the current one has necessarily received nothing yet: the
        /// answer is 1 before the front, 0 after it, and a real ratio only for the segment being
        /// built. Costs nothing but the active segment's own ingredient list.
        ///
        /// Within a segment this weighs every item by its unit count, matching what the whole-site
        /// ratio does today.
        /// </summary>
        public float SegmentProgress(int index)
        {
            if (index < 0 || index >= _segments.Count) return 0f;
            if (index < MaterializedCount) return 1f;
            if (index > MaterializedCount) return 0f;

            int cost = 0;
            int available = 0;
            foreach (RecipeIngredient ingredient in _segments[index].Definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;

                cost += ingredient.Amount;
                int delivered = _delivered.TryGetValue(ingredient.Item.Id, out int d) ? d : 0;
                int usable = delivered - ConsumedByMaterializedSegments(ingredient.Item.Id);
                available += System.Math.Max(0, System.Math.Min(usable, ingredient.Amount));
            }

            return cost <= 0 ? 1f : (float)available / cost;
        }

        /// <summary>Marks the next segment as materialized (caller has already registered/spawned it) and returns it.</summary>
        public BuildingRuntime MaterializeNextSegment()
        {
            BuildingRuntime segment = _segments[MaterializedCount];
            AccumulateConsumption(segment);
            MaterializedCount++;
            return segment;
        }

        void AccumulateConsumption(BuildingRuntime segment)
        {
            foreach (RecipeIngredient ingredient in segment.Definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;
                _consumedByMaterialized[ingredient.Item.Id] =
                    (_consumedByMaterialized.TryGetValue(ingredient.Item.Id, out int existing) ? existing : 0) + ingredient.Amount;
            }
        }

        /// <summary>
        /// Envelope for save/restore (CONTRACTS.md §14 convention): each segment is captured as
        /// definitionId+cell+rotation, not a live reference - ConstructionSiteSystem.RestoreState
        /// reconstructs the actual BuildingRuntime instances (via the same factory TryPlace uses,
        /// with no cost/placement check, exactly like ConstructionService.CreateForRestore) before
        /// building a new ConstructionSiteRuntime around them. Reservations are captured by their
        /// container's definitionId+cell so they can be re-resolved against the already-restored
        /// grid on load, since a container has no other stable identity across a save/load cycle.
        /// </summary>
        public JObject CaptureState()
        {
            var segments = new JArray();
            foreach (BuildingRuntime segment in _segments)
            {
                segments.Add(new JObject
                {
                    ["definitionId"] = segment.Definition.Id,
                    ["cellX"] = segment.Cell.X,
                    ["cellY"] = segment.Cell.Y,
                    ["rotation"] = (int)segment.FacingRotation
                });
            }

            var reservations = new JArray();
            foreach (Reservation reservation in _reservations)
            {
                if (!(reservation.Container is BuildingRuntime containerBuilding)) continue;
                reservations.Add(new JObject
                {
                    ["containerDefinitionId"] = containerBuilding.Definition.Id,
                    ["containerCellX"] = containerBuilding.Cell.X,
                    ["containerCellY"] = containerBuilding.Cell.Y,
                    ["itemId"] = reservation.ItemId,
                    ["amount"] = reservation.Amount
                });
            }

            return new JObject
            {
                ["id"] = Id,
                ["segments"] = segments,
                ["materializedCount"] = MaterializedCount,
                ["delivered"] = JObject.FromObject(_delivered),
                ["committed"] = JObject.FromObject(_committed),
                ["reservations"] = reservations
            };
        }

        /// <summary>Restores materializedCount/delivered/committed onto this already-reconstructed instance (segments and reservations are rebuilt by ConstructionSiteSystem.RestoreState before this is called, since both need external context - a factory delegate and Game.Grid respectively - this instance does not have).</summary>
        public void RestoreCounters(JObject state)
        {
            MaterializedCount = state.Value<int?>("materializedCount") ?? 0;

            // Rebuilt rather than serialized: it is a pure function of the segments already
            // materialized, and those are reconstructed before this runs. One pass on load instead
            // of a saved field that could disagree with the segment list it summarizes.
            _consumedByMaterialized.Clear();
            for (int i = 0; i < MaterializedCount && i < _segments.Count; i++)
            {
                AccumulateConsumption(_segments[i]);
            }

            _delivered.Clear();
            if (state["delivered"] is JObject delivered)
            {
                foreach (var property in delivered.Properties())
                {
                    _delivered[property.Name] = property.Value.Value<int>();
                }
            }

            _committed.Clear();
            if (state["committed"] is JObject committed)
            {
                foreach (var property in committed.Properties())
                {
                    _committed[property.Name] = property.Value.Value<int>();
                }
            }
        }
    }
}

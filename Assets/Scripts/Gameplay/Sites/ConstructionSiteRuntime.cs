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

        /// <summary>
        /// How far the front segment has physically assembled, 0 to 1. Chases what has been
        /// delivered for it (SegmentProgress) at SegmentAssembly's rate, never overtaking it, so a
        /// lot of ten components takes longer to become a building than a lot of two instead of both
        /// snapping into place - and a segment is not operational until this reaches 1.
        ///
        /// Only the front segment has one, because the delivered pile is consumed strictly in
        /// placement order: nothing behind the front has received anything yet.
        /// </summary>
        float _frontAssembly;

        /// <summary>How many separate robot deliveries have landed here. A counter rather than a timestamp: its only reader is the materialisation effect, which flashes its rim on each arrival and needs to know that one happened, not when.</summary>
        public int DeliveryCount { get; private set; }

        /// <summary>
        /// The bill's item ids in the order they were first costed, so a reader gets a stable row
        /// order. _totalCost is a Dictionary and owes nobody an enumeration order; a panel refreshed
        /// every frame off it could reshuffle its rows under the player's cursor.
        /// </summary>
        readonly List<string> _costOrder = new List<string>();

        public IReadOnlyList<BuildingRuntime> Segments => _segments;
        public IReadOnlyList<Reservation> Reservations => _reservations;
        public int MaterializedCount { get; private set; }

        /// <summary>
        /// Every segment this site placed got built. The empty-list guard is what separates that from
        /// abandonment: a site whose segments were all cancelled satisfies "materialized >= count"
        /// arithmetically while having built nothing at all, and the site panel keys on exactly this
        /// to decide whether to hand over to the finished building's panel or simply close.
        /// </summary>
        public bool IsComplete => _segments.Count > 0 && MaterializedCount >= _segments.Count;

        /// <summary>Nothing more will ever be built here, for either reason: it finished, or it lost every segment it had. What decides that a site leaves the queue.</summary>
        public bool HasNothingLeftToBuild => _segments.Count == 0 || IsComplete;

        /// <summary>The building/segment kind this site is building, for UI/messaging - the first segment's definition (every segment of a conveyor/splitter run shares the same BuildingDefinition category, if not always the exact same definition instance for corner-vs-straight).</summary>
        public BuildingDefinition PrimaryDefinition => _segments.Count > 0 ? _segments[0].Definition : null;

        public ConstructionSiteRuntime(int id, BuildingRuntime firstSegment)
        {
            Id = id;
            AddSegment(firstSegment);
        }

        public void AddSegment(BuildingRuntime segment)
        {
            // From here until it materializes it holds ground and nothing else: no tick, and nothing
            // may be handed to it or taken from it. See BuildingRuntime.IsUnderConstruction.
            segment.IsUnderConstruction = true;
            _segments.Add(segment);
            foreach (RecipeIngredient ingredient in segment.Definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;

                if (!_totalCost.ContainsKey(ingredient.Item.Id)) _costOrder.Add(ingredient.Item.Id);
                _totalCost[ingredient.Item.Id] = (_totalCost.TryGetValue(ingredient.Item.Id, out int existing) ? existing : 0) + ingredient.Amount;
            }
        }

        /// <summary>
        /// Drops one not-yet-materialized segment: the cell it was holding has just been taken over
        /// by another placement, so its cost comes off the bill and the earmarks it alone justified
        /// are released. Every other segment keeps its own ground and its own share - a run does not
        /// stop being a run because one of its cells was reused.
        ///
        /// Anything already delivered stays with the site and feeds the segments that remain: a
        /// partly-supplied front segment being taken over hands its material to the one behind it,
        /// which is exactly what the delivered-minus-consumed arithmetic already does.
        ///
        /// False for a segment this site never held, and for one already built - that is a real
        /// building, and it leaves through demolition.
        /// </summary>
        public bool TryRemovePendingSegment(BuildingRuntime segment)
        {
            int index = -1;
            for (int i = MaterializedCount; i < _segments.Count; i++)
            {
                if (!ReferenceEquals(_segments[i], segment)) continue;
                index = i;
                break;
            }

            if (index < 0) return false;

            _segments.RemoveAt(index);

            foreach (RecipeIngredient ingredient in segment.Definition.Cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;
                if (!_totalCost.TryGetValue(ingredient.Item.Id, out int total)) continue;

                int remaining = total - ingredient.Amount;
                if (remaining > 0)
                {
                    _totalCost[ingredient.Item.Id] = remaining;
                }
                else
                {
                    _totalCost.Remove(ingredient.Item.Id);
                    _costOrder.Remove(ingredient.Item.Id);
                }
            }

            ReleaseOverCommitment();
            return true;
        }

        /// <summary>
        /// Gives back whatever the bill no longer justifies after it shrank. Container earmarks go
        /// first, newest first, so the stock is immediately claimable by another site again -
        /// leaving one standing is how a chest ends up holding material nothing may ever take.
        ///
        /// What is already riding in a robot's cargo stays committed: it is in flight and cannot be
        /// recalled mid-trip. It simply arrives, and GetSupply clamps it away.
        /// </summary>
        void ReleaseOverCommitment()
        {
            var items = new List<string>(_committed.Keys);
            foreach (string itemId in items)
            {
                int excess = -RemainingNeeded(itemId);
                if (excess <= 0) continue;

                for (int i = _reservations.Count - 1; i >= 0 && excess > 0; i--)
                {
                    Reservation reservation = _reservations[i];
                    if (reservation.ItemId != itemId) continue;

                    int take = System.Math.Min(reservation.Amount, excess);
                    excess -= take;

                    reservation.Amount -= take;
                    if (reservation.Amount <= 0) _reservations.RemoveAt(i);
                    else _reservations[i] = reservation;

                    _committed[itemId] = System.Math.Max(0, _committed[itemId] - take);
                }
            }
        }

        /// <summary>
        /// One ingredient of the bill, in the three states a player actually asks about: what has
        /// physically landed here, what is promised and coming, and what nothing anywhere has been
        /// found for.
        ///
        /// <b>Reserved is the whole point of this shape.</b> Without it the only honest thing to show
        /// is "delivered 10 of 15", which cannot tell a site the system is busy serving from one that
        /// has been forgotten for want of production - the two look identical until one of them
        /// silently never finishes.
        ///
        /// It covers both halves of a promise: earmarked in a container and not yet collected, and
        /// already riding in a robot's cargo. Both are the same statement about <b>stock</b> - this
        /// material is spoken for and nothing else may take it - and neither says anything about
        /// movement. Whether a robot is actually on its way to this site right now is a different
        /// question with a different answer, and it is not answered here: it is a property of the
        /// robots, not of the bill.
        /// </summary>
        public readonly struct SupplyLine
        {
            public readonly string ItemId;
            public readonly int Total;
            public readonly int Delivered;

            /// <summary>Committed to this site but not yet delivered - earmarked in a container, or already in a robot's cargo.</summary>
            public readonly int Reserved;

            public readonly int Missing;

            public SupplyLine(string itemId, int total, int delivered, int reserved, int missing)
            {
                ItemId = itemId;
                Total = total;
                Delivered = delivered;
                Reserved = reserved;
                Missing = missing;
            }

            /// <summary>Nothing is coming and something is still owed - the site is stalled on this ingredient.</summary>
            public bool IsStalled => Missing > 0 && Reserved == 0;
        }

        /// <summary>
        /// The bill of materials in its three states, in a stable order. Fills a caller-owned list so
        /// a panel refreshing every frame allocates nothing.
        ///
        /// Assembled here rather than left to the reader: the three numbers are one statement about
        /// one ingredient and they have to add up to Total. A caller subtracting its own "en route"
        /// from a delivered count and a cost would be re-deriving that rule outside the only object
        /// that maintains it.
        /// </summary>
        public void GetSupply(List<SupplyLine> into)
        {
            into.Clear();

            foreach (string itemId in _costOrder)
            {
                int total = _totalCost.TryGetValue(itemId, out int c) ? c : 0;
                int delivered = _delivered.TryGetValue(itemId, out int d) ? d : 0;
                int reserved = _committed.TryGetValue(itemId, out int p) ? p : 0;

                // Delivered can exceed a segment's own share while later segments still owe theirs,
                // so the clamp is on the total, not per line.
                delivered = System.Math.Min(delivered, total);
                reserved = System.Math.Min(reserved, total - delivered);

                into.Add(new SupplyLine(itemId, total, delivered, reserved, System.Math.Max(0, total - delivered - reserved)));
            }
        }

        /// <summary>Whether every ingredient of the bill has been delivered or is on its way - false the moment one of them has nothing coming.</summary>
        public bool IsFullySupplied
        {
            get
            {
                foreach (string itemId in _costOrder)
                {
                    if (RemainingNeeded(itemId) > 0) return false;
                }
                return true;
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
            DeliveryCount++;
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

            // Delivered is not built. The segment still has to physically assemble, and it does not
            // tick, transport, produce or accept anything until it has - which is the whole reason
            // the assembly clock lives here and not with the effect that draws it.
            return _frontAssembly >= 1f;
        }

        /// <summary>
        /// Advances the front segment's assembly by one tick. Driven from ConstructionSiteSystem's
        /// central tick on scaled time, so it stops with the game like everything else the
        /// simulation owns.
        ///
        /// <b>It runs to 1 at its own pace, whatever has been delivered.</b> It used to be clamped
        /// to the delivered fraction, so a site waiting on its last plate sat visibly half-built -
        /// two facts drawn as one, and the animation was the one the player read. It is an
        /// animation: it says "something is being assembled here", not "here is how much material
        /// arrived". What material arrived is on the site's own panel, in numbers.
        ///
        /// Being drawn as finished is not being built: <see cref="CanMaterializeNextSegment"/> still
        /// requires the segment's whole cost, so a fully-drawn site with a plate outstanding keeps
        /// its panel, keeps its robots coming, and does not tick, transport or produce anything.
        /// </summary>
        public void AdvanceAssembly(float deltaTime)
        {
            if (MaterializedCount >= _segments.Count) return;

            // The footprint's bounding area, matching what the effect was tuned on.
            UnityEngine.Vector2Int footprint = _segments[MaterializedCount].Definition.FootprintSize;

            float rate = SegmentAssembly.RateFor(footprint.x * footprint.y);
            _frontAssembly = System.Math.Min(1f, _frontAssembly + rate * deltaTime);
        }

        /// <summary>
        /// How far along segment `index` has physically assembled, 0 to 1 - what the materialisation
        /// effect draws. 1 behind the front, 0 ahead of it, and the running assembly on the front
        /// itself, for the same reason SegmentProgress has that shape: segments are built strictly in
        /// placement order.
        ///
        /// Distinct from SegmentProgress, which is how much material has <b>arrived</b>. This one is
        /// how much of it has been turned into a building, and it is the one that decides when the
        /// building starts working.
        /// </summary>
        public float AssemblyProgressFor(int index)
        {
            if (index < 0 || index >= _segments.Count) return 0f;
            if (index < MaterializedCount) return 1f;
            if (index > MaterializedCount) return 0f;
            return _frontAssembly;
        }

        int ConsumedByMaterializedSegments(string itemId)
        {
            return _consumedByMaterialized.TryGetValue(itemId, out int total) ? total : 0;
        }

        /// <summary>
        /// How much of segment `index`'s own cost has <b>arrived</b>, 0 to 1 - the target its assembly
        /// chases, and the only form in which per-segment supply leaves this object: the rule that
        /// decides which delivery feeds which segment stays here rather than being re-derived
        /// elsewhere. What is drawn, and what decides when the building works, is
        /// AssemblyProgressFor.
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

            // Paid for in full: it becomes a working building here, in the same breath as the caller
            // registering it with TransportSystem. The two must not drift apart - a segment that is
            // registered but still flagged would be ticked while every neighbour refused to deal
            // with it.
            segment.IsUnderConstruction = false;

            MaterializedCount++;

            // The next segment starts from nothing: a run assembles piece by piece along its path,
            // never all at once.
            _frontAssembly = 0f;
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
                ["frontAssembly"] = _frontAssembly,
                ["deliveryCount"] = DeliveryCount,
                ["delivered"] = JObject.FromObject(_delivered),
                ["committed"] = JObject.FromObject(_committed),
                ["reservations"] = reservations
            };
        }

        /// <summary>Restores materializedCount/delivered/committed onto this already-reconstructed instance (segments and reservations are rebuilt by ConstructionSiteSystem.RestoreState before this is called, since both need external context - a factory delegate and Game.Grid respectively - this instance does not have).</summary>
        public void RestoreCounters(JObject state)
        {
            MaterializedCount = state.Value<int?>("materializedCount") ?? 0;

            // A save taken mid-assembly resumes mid-assembly rather than restarting the front
            // segment from nothing. Absent in an older blob, which simply means "not started".
            _frontAssembly = state.Value<float?>("frontAssembly") ?? 0f;
            DeliveryCount = state.Value<int?>("deliveryCount") ?? 0;

            // Rebuilt rather than serialized: it is a pure function of the segments already
            // materialized, and those are reconstructed before this runs. One pass on load instead
            // of a saved field that could disagree with the segment list it summarizes.
            _consumedByMaterialized.Clear();
            for (int i = 0; i < MaterializedCount && i < _segments.Count; i++)
            {
                AccumulateConsumption(_segments[i]);

                // AddSegment flagged every reconstructed segment as pending, which is right for the
                // ones still owed material and wrong for the ones this save had already built.
                _segments[i].IsUnderConstruction = false;
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

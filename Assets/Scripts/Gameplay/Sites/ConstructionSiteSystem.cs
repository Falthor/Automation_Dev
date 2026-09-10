using System;
using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;
using Game.Gameplay.Items;
using Game.Gameplay.Notifications;
using Game.Gameplay.Transport;
using Game.Grid;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Game.Gameplay.Sites
{
    /// <summary>
    /// Owns every construction site and both builder robots (TASK_05_ROBOT_CONSTRUCTEUR.md), and
    /// is the sole authority on GlobalStock's new meaning: a read-only aggregate over the Core
    /// chest, every placed Storage, and every production building's output, minus whatever is
    /// currently reserved - "ce que GlobalStock affiche est exactement ce dans quoi un robot peut
    /// aller puiser" (§1). Ticked once per frame from GameRuntime's central tick, exactly like
    /// TransportSystem - never from an individual building/robot Update().
    ///
    /// Reservation is localized (couples contenant-quantité, §1): every tick, every open site -
    /// oldest first - tries to earmark whatever it still needs from the collection order (Core
    /// chest, then every Storage, then every production building's output). An older site always
    /// wins a newly available unit over a younger one.
    ///
    /// Dispatch follows that same priority without ever idling on it: each free robot takes the
    /// oldest site that still has an earmark nobody has been sent to collect, so robots pile onto
    /// one chantier while it has work to hand out and spill onto the next once it has none left.
    /// A site with nothing to collect right now is stepped over, never waited on, and reclaims the
    /// next free robot as soon as an earmark lands on it again.
    /// </summary>
    public sealed class ConstructionSiteSystem
    {
        /// <summary>Matches ConstructionService.CoreStorageDefinitionId - the Core chest is identified by definition id (not a tracked instance reference) so collection order still finds it correctly after a save/load, exactly like ConstructionService.IsProtectedFromDemolition already does.</summary>
        public const string CoreStorageDefinitionId = "core_storage";

        readonly TransportSystem _transport;
        readonly GridRuntime _grid;
        readonly NotificationSystem _notifications;

        readonly List<ConstructionSiteRuntime> _queue = new List<ConstructionSiteRuntime>();
        readonly List<BuilderRobotRuntime> _robots = new List<BuilderRobotRuntime>();
        readonly List<RepatriationJob> _repatriationJobs = new List<RepatriationJob>();

        int _nextSiteId;
        int? _stuckSiteId;
        int? _stuckNotificationId;

        /// <summary>The Core delivery in flight, or null. One at a time: a directive is validated, carried and finished before the next is offered.</summary>
        CoreHaulJob _coreHaul;

        /// <summary>Fired when a Core delivery has fully landed - what turns a validated directive into its reward.</summary>
        public event Action CoreHaulCompleted;

        public CoreHaulJob CoreHaul => _coreHaul;

        /// <summary>Fired the instant a segment (a whole building for a single-building site, one belt piece for a conveyor/splitter run) has received its full cost and becomes a real, registered building - the caller (Presentation) spawns its view and registers it with Transport/ItemVisuals, exactly like an immediate TryPlace used to.</summary>
        public event Action<BuildingRuntime> SegmentMaterialized;

        public IReadOnlyList<ConstructionSiteRuntime> Sites => _queue;
        public IReadOnlyList<BuilderRobotRuntime> Robots => _robots;

        /// <summary>
        /// The game runs two robots (robotCount's default, and the layout below reproduces exactly
        /// the two park spots they have always had). The count is a parameter only because dispatch
        /// has to hold at any fleet size, and that is not a claim one can make about a system whose
        /// fleet cannot be built larger than two: the parameter is what lets a test put two hundred
        /// and fifty robots against a queue and check they spread over it.
        /// </summary>
        public ConstructionSiteSystem(TransportSystem transport, GridRuntime grid, NotificationSystem notifications, Vector2 robotParkOrigin, int robotCount = 2)
        {
            _transport = transport;
            _grid = grid;
            _notifications = notifications;

            const float spacing = 1.2f;
            int perRow = Mathf.Max(1, Mathf.Min(robotCount, 4));
            for (int i = 0; i < robotCount; i++)
            {
                float x = (i % perRow - (perRow - 1) / 2f) * spacing;
                float y = -0.3f - i / perRow * spacing;
                _robots.Add(new BuilderRobotRuntime(i, robotParkOrigin + new Vector2(x, y)));
            }
        }

        /// <summary>
        /// How many pending (not yet complete) single-building sites count against
        /// ConstructionService.BuildingCap right now - a site occupies its slot from the moment
        /// it's placed, not only once complete, or the cap would lose all meaning while several
        /// sites wait on materials. Conveyor/splitter/crossroad sites are exempt, exactly like the
        /// completed buildings they become (ConstructionService.OccupiedBuildingSlots).
        /// </summary>
        public int OccupiedSiteSlots
        {
            get
            {
                int count = 0;
                foreach (ConstructionSiteRuntime site in _queue)
                {
                    if (!site.PrimaryDefinition.CountsAgainstBuildingCap) continue;
                    count++;
                }
                return count;
            }
        }

        /// <summary>Starts a brand-new site (a single building, or the first segment of a conveyor/splitter/crossroad drag). firstSegment is already a real, correctly configured BuildingRuntime occupying its grid cell - see ConstructionService.TryPlace.</summary>
        public ConstructionSiteRuntime CreateSite(BuildingRuntime firstSegment)
        {
            var site = new ConstructionSiteRuntime(_nextSiteId++, firstSegment);
            _queue.Add(site);
            RunReservationPass();

            // Nothing materializes here, not even a cost-free segment: everything has to assemble
            // first, and AdvanceAssemblies is the only place that happens.
            return site;
        }

        /// <summary>Appends one more segment to an in-progress conveyor/splitter/crossroad drag's site - still one chantier for the whole gesture (TASK_05_ROBOT_CONSTRUCTEUR.md §3).</summary>
        public void AppendSegment(ConstructionSiteRuntime site, BuildingRuntime segment)
        {
            site.AddSegment(segment);
            if (!_queue.Contains(site)) _queue.Add(site);
            RunReservationPass();
        }

        /// <summary>
        /// One unbuilt segment leaves its chantier: its cost comes off the bill, the earmarks it
        /// alone justified go back to their containers, and every sibling keeps its own cell and its
        /// own share. A site left with no segment at all is over and leaves the queue, which
        /// releases any robot still working for it (cargo already picked up is dropped off, never
        /// lost - TASK_05_ROBOT_CONSTRUCTEUR.md §4).
        ///
        /// The ground is deliberately <b>not</b> freed here: this is the overtaking path, where a
        /// new placement has already claimed that cell and is about to put its own segment on it.
        /// The player cancelling a segment goes through CancelPendingSegment below, which is this
        /// plus giving the ground back.
        /// </summary>
        public bool RemovePendingSegment(BuildingRuntime segment)
        {
            if (!TryGetSiteContaining(segment, out ConstructionSiteRuntime site)) return false;
            if (!site.TryRemovePendingSegment(segment)) return false;

            if (site.HasNothingLeftToBuild) CloseSite(site);
            return true;
        }

        /// <summary>
        /// The player cancelled one unbuilt segment: as above, and its ground goes back to being
        /// empty. So a drag of twenty belts can lose the three that went the wrong way without being
        /// laid again from scratch, and a single-building chantier - one segment - is cancelled
        /// outright, which is what right-clicking a lone blue silhouette has always done.
        ///
        /// One line apart from RemovePendingSegment, and the line is who owns the ground afterwards.
        /// The site bookkeeping stays written once: it was two cancellation paths restating the same
        /// rules that let the Extractor deposit rule hold in one of them and not the other.
        /// </summary>
        public bool CancelPendingSegment(BuildingRuntime segment)
        {
            if (!RemovePendingSegment(segment)) return false;

            // Not a bare clear: a cancelled Extractor segment has to give its deposit back, or the
            // ore is gone from the grid and nothing can ever be built on it again.
            BuildingRuntime.ReleaseFootprint(_grid, segment);
            return true;
        }

        /// <summary>
        /// Takes a site out of the queue and releases every robot still working for it. Cargo
        /// already picked up is dropped off rather than lost; a robot merely on its way to fetch
        /// also drops the claim it was carrying on that container, since what is pending counts
        /// against TotalReserved and a claim left standing would keep that stock unclaimable by
        /// anything, forever - the robot has nowhere to bring it and no reservation pass can see
        /// past it.
        /// </summary>
        void CloseSite(ConstructionSiteRuntime site)
        {
            _queue.Remove(site);

            foreach (BuilderRobotRuntime robot in _robots)
            {
                if (!ReferenceEquals(robot.TargetSite, site)) continue;

                robot.TargetSite = null;
                robot.SourceContainer = null;
                robot.ClearPending();

                if (robot.CargoTotal > 0 && robot.State != BuilderRobotState.Repatriating)
                {
                    BeginDropOffCarriedCargo(robot);
                }
                else if (robot.State == BuilderRobotState.MovingToSource || robot.State == BuilderRobotState.Idle)
                {
                    robot.State = BuilderRobotState.Idle;
                    robot.MoveTarget = robot.ParkPosition;
                }
            }

            if (_stuckSiteId == site.Id) ClearStuckNotification();
        }

        /// <summary>Whether `runtime` is still a pending segment of some site (not yet materialized/registered) - used by the demolition input path to route a click at that cell to CancelPendingSegment instead of ConstructionService.TryDemolish, which must never see an unpaid, unregistered building.</summary>
        public bool TryGetSiteContaining(BuildingRuntime runtime, out ConstructionSiteRuntime site)
        {
            foreach (ConstructionSiteRuntime candidate in _queue)
            {
                for (int i = candidate.MaterializedCount; i < candidate.Segments.Count; i++)
                {
                    if (ReferenceEquals(candidate.Segments[i], runtime))
                    {
                        site = candidate;
                        return true;
                    }
                }
            }
            site = null;
            return false;
        }

        /// <summary>A building was just demolished (grid already cleared by ConstructionService.TryDemolish) - its construction cost must be physically hauled back by a robot rather than refunded instantly. originCell is only used to place the job's icon/notification context; the robot itself needs no travel to "pick up" the cargo, since it left no physical trace at that cell.</summary>
        public void EnqueueRepatriation(GridCoord originCell, IReadOnlyList<RecipeIngredient> cost)
        {
            var remaining = new Dictionary<string, int>();
            foreach (RecipeIngredient ingredient in cost)
            {
                if (ingredient.Item == null || ingredient.Amount <= 0) continue;
                remaining[ingredient.Item.Id] = (remaining.TryGetValue(ingredient.Item.Id, out int existing) ? existing : 0) + ingredient.Amount;
            }
            if (remaining.Count == 0) return;

            _repatriationJobs.Add(new RepatriationJob { Remaining = remaining });
        }

        /// <summary>
        /// Starts hauling a Core directive's materials to `destination`. One at a time - a second
        /// call while one is in flight is refused rather than queued, because the Core only ever
        /// asks for one thing at a time.
        /// </summary>
        public bool BeginCoreHaul(BuildingRuntime destination, IReadOnlyDictionary<string, int> items)
        {
            if (_coreHaul != null || destination == null || items == null) return false;

            _coreHaul = new CoreHaulJob(destination, items);
            if (_coreHaul.IsComplete)
            {
                // Asked for nothing: finished before it started.
                CompleteCoreHaul();
                return true;
            }

            RunReservationPass();
            return true;
        }

        void CompleteCoreHaul()
        {
            _coreHaul = null;
            CoreHaulCompleted?.Invoke();
        }

        public void Tick(float deltaTime)
        {
            RunReservationPass();
            AdvanceAssemblies(deltaTime);
            UpdateStuckNotification();

            foreach (BuilderRobotRuntime robot in _robots)
            {
                TickRobot(robot, deltaTime);
            }
        }

        /// <summary>
        /// Every open site's front segment physically assembles, and becomes a real building the
        /// moment it is whole - never merely when its last item landed. Walked backwards because
        /// MaterializeReadySegments takes a finished site out of the queue underneath us.
        /// </summary>
        void AdvanceAssemblies(float deltaTime)
        {
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                ConstructionSiteRuntime site = _queue[i];
                site.AdvanceAssembly(deltaTime);
                MaterializeReadySegments(site);
            }
        }

        // ---- Reservation ----

        void RunReservationPass()
        {
            foreach (ConstructionSiteRuntime site in _queue)
            {
                if (site.IsComplete) continue;

                var needed = new List<string>(site.TotalCost.Keys);
                foreach (string itemId in needed)
                {
                    int remaining = site.RemainingNeeded(itemId);
                    if (remaining <= 0) continue;

                    foreach (StorageRuntime storage in StoragesInCollectionOrder())
                    {
                        if (remaining <= 0) break;
                        int available = storage.GetInputAmount(itemId) - TotalReserved(storage, itemId);
                        if (available <= 0) continue;
                        int reserve = Mathf.Min(available, remaining);
                        site.AddReservation(storage, itemId, reserve);
                        remaining -= reserve;
                    }

                    if (remaining <= 0) continue;

                    foreach (ProductionBuildingRuntime production in ProductionOutputsInOrder())
                    {
                        if (remaining <= 0) break;
                        int outputAmount = production.GetOutputContents().TryGetValue(itemId, out int amount) ? amount : 0;
                        int available = outputAmount - TotalReserved(production, itemId);
                        if (available <= 0) continue;
                        int reserve = Mathf.Min(available, remaining);
                        site.AddReservation(production, itemId, reserve);
                        remaining -= reserve;
                    }
                }
            }

            ReserveForCoreHaul();
        }

        /// <summary>
        /// The Core delivery earmarks last, out of whatever the sites left - it is a request the
        /// player took on, and must not starve a building already waiting on its material. Same
        /// collection order and same claim accounting; only the owner of the earmark differs.
        /// </summary>
        void ReserveForCoreHaul()
        {
            if (_coreHaul == null) return;

            foreach (string itemId in new List<string>(_coreHaul.Needed.Keys))
            {
                int remaining = _coreHaul.RemainingToReserve(itemId);
                if (remaining <= 0) continue;

                foreach (StorageRuntime storage in StoragesForCoreHaul())
                {
                    if (remaining <= 0) break;
                    int available = storage.GetInputAmount(itemId) - TotalReserved(storage, itemId);
                    if (available <= 0) continue;
                    int reserve = Mathf.Min(available, remaining);
                    _coreHaul.AddReservation(storage, itemId, reserve);
                    remaining -= reserve;
                }

                if (remaining <= 0) continue;

                foreach (ProductionBuildingRuntime production in ProductionOutputsInOrder())
                {
                    if (remaining <= 0) break;
                    int outputAmount = production.GetOutputContents().TryGetValue(itemId, out int amount) ? amount : 0;
                    int available = outputAmount - TotalReserved(production, itemId);
                    if (available <= 0) continue;
                    int reserve = Mathf.Min(available, remaining);
                    _coreHaul.AddReservation(production, itemId, reserve);
                    remaining -= reserve;
                }
            }
        }

        /// <summary>
        /// Everything already spoken for inside one container: what sites hold as reservations, plus
        /// what robots are already on their way to collect.
        ///
        /// The second half is not redundant. A dispatched robot releases its site's reservation the
        /// moment it is assigned, but only takes the items when it arrives - so for the whole length
        /// of that trip the units sit in the container claimed by nobody. Counting the reservations
        /// alone would offer them to the next site's reservation pass, and one stack would be
        /// promised twice: the first robot empties it, and the second site keeps showing material on
        /// its way that no longer exists anywhere.
        /// </summary>
        int TotalReserved(object container, string itemId)
        {
            int total = 0;

            // The Core delivery claims stock on exactly the same footing as a site: leaving it out
            // would offer the same stack to both.
            if (_coreHaul != null) total += _coreHaul.ReservedIn(container, itemId);
            foreach (ConstructionSiteRuntime site in _queue)
            {
                foreach (Reservation reservation in site.Reservations)
                {
                    if (ReferenceEquals(reservation.Container, container) && reservation.ItemId == itemId) total += reservation.Amount;
                }
            }

            foreach (BuilderRobotRuntime robot in _robots)
            {
                // Zero the instant the cargo is actually taken, which is also when the container's
                // own contents drop - so the claim is continuous and never double-counted.
                if (!ReferenceEquals(robot.SourceContainer, container)) continue;

                total += robot.PendingOf(itemId);
            }

            return total;
        }

        IEnumerable<StorageRuntime> StoragesInCollectionOrder()
        {
            if (_transport == null) yield break;

            StorageRuntime coreChest = null;
            foreach (StorageRuntime storage in _transport.Storages)
            {
                if (storage.Definition.Id == CoreStorageDefinitionId)
                {
                    coreChest = storage;
                    break;
                }
            }

            if (coreChest != null) yield return coreChest;

            foreach (StorageRuntime storage in _transport.Storages)
            {
                if (!ReferenceEquals(storage, coreChest)) yield return storage;
            }
        }

        /// <summary>Collection order with the Core's own reserve left out - see <see cref="GetAvailableForCoreHaul"/> for why a Core delivery may not draw on it.</summary>
        IEnumerable<StorageRuntime> StoragesForCoreHaul()
        {
            foreach (StorageRuntime storage in StoragesInCollectionOrder())
            {
                if (storage.Definition.Id == CoreStorageDefinitionId) continue;
                yield return storage;
            }
        }

        IEnumerable<ProductionBuildingRuntime> ProductionOutputsInOrder()
        {
            if (_transport == null) yield break;

            foreach (BuildingRuntime building in _transport.GetAllBuildings())
            {
                if (building is ProductionBuildingRuntime production) yield return production;
            }
        }

        /// <summary>
        /// The read-only aggregate GlobalStock now is (TASK_05_ROBOT_CONSTRUCTEUR.md §1): Core
        /// chest + every Storage + every production building's output, minus whatever any site has
        /// already reserved - exactly what a robot could still go claim right now. Never includes
        /// items in transit on a conveyor or in a robot's cargo, by design (§1's invariant).
        /// </summary>
        public IReadOnlyDictionary<string, int> GetAvailableAggregate() => Aggregate(StoragesInCollectionOrder());

        /// <summary>
        /// What a Core delivery may draw on: the same aggregate <b>minus the Core's own reserve</b>.
        ///
        /// A directive asks the player to bring the Core something. Material already sitting in the
        /// hatch under it has not been brought anywhere - it started there - so counting it would
        /// let the opening directive be satisfied by the starting stock without a single machine
        /// being built. The reserve still funds construction: <see cref="GetAvailableAggregate"/> is
        /// unchanged, and that is what a building's bill reads.
        ///
        /// Paired with <see cref="StoragesForCoreHaul"/>, which excludes the same container from the
        /// reservation pass. The two must exclude identically, or the Validate button would grey out
        /// on stock the haul would then happily claim.
        /// </summary>
        public IReadOnlyDictionary<string, int> GetAvailableForCoreHaul() => Aggregate(StoragesForCoreHaul());

        IReadOnlyDictionary<string, int> Aggregate(IEnumerable<StorageRuntime> storages)
        {
            var totals = new Dictionary<string, int>();

            foreach (StorageRuntime storage in storages)
            {
                var seen = new HashSet<string>();
                foreach (InventorySlot slot in storage.Slots)
                {
                    if (slot.IsEmpty || !seen.Add(slot.ItemId)) continue;
                    int amount = storage.GetInputAmount(slot.ItemId) - TotalReserved(storage, slot.ItemId);
                    if (amount <= 0) continue;
                    totals[slot.ItemId] = (totals.TryGetValue(slot.ItemId, out int existing) ? existing : 0) + amount;
                }
            }

            foreach (ProductionBuildingRuntime production in ProductionOutputsInOrder())
            {
                foreach (var kvp in production.GetOutputContents())
                {
                    int amount = kvp.Value - TotalReserved(production, kvp.Key);
                    if (amount <= 0) continue;
                    totals[kvp.Key] = (totals.TryGetValue(kvp.Key, out int existing) ? existing : 0) + amount;
                }
            }

            return totals;
        }

        // ---- Robot dispatch/state machine ----

        /// <summary>
        /// The site this robot should serve: the oldest one with something still earmarked in a
        /// container and nobody yet sent to collect it.
        ///
        /// Asked once per idle robot, independently, which is what makes the answer scale. A
        /// dispatch consumes the earmark it takes (ReleaseReservationForPickup), so the next robot
        /// asking - this tick or a later one - sees only what is genuinely still waiting for a
        /// carrier. Two robots, or two hundred and fifty, spread across the queue by that alone:
        /// each fills up on the oldest site that still has work, and the overflow rolls onto the
        /// next.
        ///
        /// A site whose whole remaining bill is already riding in someone's cargo is therefore
        /// stepped over, not waited on. It used to be returned anyway - "the robots serve one
        /// chantier at a time" read as a single active site - and every other robot then found a
        /// site with nothing left to hand out and simply stood still, while finished-but-unserved
        /// chantiers waited behind it.
        ///
        /// Oldest-first survives intact, because each robot rescans from the head of the queue: a
        /// site that regains an earmark takes the next robot to come free, ahead of any younger one.
        /// </summary>
        ConstructionSiteRuntime FindSiteAwaitingCollection()
        {
            foreach (ConstructionSiteRuntime site in _queue)
            {
                if (site.IsComplete) continue;
                if (site.Reservations.Count > 0) return site;
            }
            return null;
        }

        void TickRobot(BuilderRobotRuntime robot, float deltaTime)
        {
            switch (robot.State)
            {
                case BuilderRobotState.Idle:
                    TryAssignTask(robot);
                    if (robot.State == BuilderRobotState.Idle && robot.Position != robot.ParkPosition)
                    {
                        robot.MoveTarget = robot.ParkPosition;
                        robot.AdvanceTowardTarget(deltaTime);
                    }
                    break;

                case BuilderRobotState.MovingToSource:
                    if (robot.AdvanceTowardTarget(deltaTime)) PerformPickup(robot);
                    break;

                case BuilderRobotState.MovingToSite:
                    if (robot.AdvanceTowardTarget(deltaTime)) PerformDelivery(robot);
                    break;

                case BuilderRobotState.Repatriating:
                    if (robot.AdvanceTowardTarget(deltaTime)) PerformRepatriationDropoff(robot);
                    break;

                case BuilderRobotState.Blocked:
                    TickBlocked(robot, deltaTime);
                    break;
            }
        }

        void TryAssignTask(BuilderRobotRuntime robot)
        {
            ConstructionSiteRuntime site = FindSiteAwaitingCollection();
            if (site != null)
            {
                // <b>One source container per round trip, and everything it holds for this site.</b>
                // Uncapped and multi-item: a robot is meant to fetch a whole building's bill in one
                // trip, which it does whenever the materials sit in one chest - and the player's do,
                // being in the Core's own reserve. Spread across two chests it is two trips, because
                // a trip still visits one source; that is the multi-stop tour this deliberately
                // does not do.
                Reservation chosen = site.Reservations[0];
                LoadPendingFromContainer(robot, chosen.Container, site.Reservations,
                    (item, amount) => site.ReleaseReservationForPickup(chosen.Container, item, amount));

                robot.TargetSite = site;
                robot.SourceContainer = chosen.Container;
                robot.MoveTarget = ContainerPosition(chosen.Container);
                robot.State = BuilderRobotState.MovingToSource;
                return;
            }

            // A Core delivery comes after every construction site: a directive is a request the
            // player chose to take on, and it must not starve the buildings already waiting.
            if (_coreHaul != null && _coreHaul.Reservations.Count > 0)
            {
                // <b>Still capped, and this is the one place the cap survives.</b> A directive is a
                // hand-over the player chose to take on rather than a building waiting on its
                // materials, and how many waves it takes is part of what it asks.
                Reservation chosen = _coreHaul.Reservations[0];
                int available = _coreHaul.ReservedIn(chosen.Container, chosen.ItemId);
                int amount = Mathf.Min(BuilderRobotRuntime.DirectiveCargoCapacity, available);
                _coreHaul.ReleaseReservationForPickup(chosen.Container, chosen.ItemId, amount);

                robot.TargetHaul = _coreHaul;
                robot.SourceContainer = chosen.Container;
                robot.ClearPending();
                robot.AddPending(chosen.ItemId, amount);
                robot.MoveTarget = ContainerPosition(chosen.Container);
                robot.State = BuilderRobotState.MovingToSource;
                return;
            }

            if (_repatriationJobs.Count > 0)
            {
                AssignRepatriation(robot);
            }
        }

        /// <summary>
        /// Fills a robot's pending set with everything <paramref name="reservations"/> earmarks in one
        /// container, and hands each amount to <paramref name="release"/> so the job's own books move
        /// with it.
        ///
        /// Uncapped on purpose - see BuilderRobotRuntime.DirectiveCargoCapacity for the one job kind
        /// that is not. The list is copied first because releasing mutates it.
        /// </summary>
        static void LoadPendingFromContainer(BuilderRobotRuntime robot, object container,
            IReadOnlyList<Reservation> reservations, System.Action<string, int> release)
        {
            robot.ClearPending();

            var forThisContainer = new List<Reservation>();
            for (int i = 0; i < reservations.Count; i++)
            {
                if (ReferenceEquals(reservations[i].Container, container)) forThisContainer.Add(reservations[i]);
            }

            foreach (Reservation reservation in forThisContainer)
            {
                if (reservation.Amount <= 0) continue;

                robot.AddPending(reservation.ItemId, reservation.Amount);
                release(reservation.ItemId, reservation.Amount);
            }
        }

        void PerformPickup(BuilderRobotRuntime robot)
        {
            robot.State = BuilderRobotState.Loading;

            // Copied out before taking anything: the pending set is cleared below, and a shortfall
            // has to be released against what was promised rather than against what is left.
            foreach (var kvp in new List<KeyValuePair<string, int>>(robot.Pending))
            {
                int taken = ContainerTake(robot.SourceContainer, kvp.Key, kvp.Value);
                robot.AddCargo(kvp.Key, taken);

                int shortfall = kvp.Value - taken;
                if (shortfall <= 0) continue;

                robot.TargetSite?.ReleaseCommitment(kvp.Key, shortfall);
                robot.TargetHaul?.ReleaseCommitment(kvp.Key, shortfall);
            }

            robot.SourceContainer = null;
            robot.ClearPending();
            robot.MoveTarget = robot.TargetHaul != null
                ? ContainerPosition(robot.TargetHaul.Destination)
                : SitePosition(robot.TargetSite);
            robot.State = BuilderRobotState.MovingToSite;
        }

        void PerformDelivery(BuilderRobotRuntime robot)
        {
            robot.State = BuilderRobotState.Delivering;

            ConstructionSiteRuntime site = robot.TargetSite;
            if (site != null)
            {
                foreach (var kvp in new List<KeyValuePair<string, int>>(robot.Cargo))
                {
                    site.RegisterDelivery(kvp.Key, kvp.Value);
                }

                // No materialization here on purpose: what just landed is material, and material is
                // not a building until it has assembled. AdvanceAssemblies takes it from here.
            }

            CoreHaulJob haul = robot.TargetHaul;
            if (haul != null)
            {
                foreach (var kvp in new List<KeyValuePair<string, int>>(robot.Cargo))
                {
                    haul.RegisterDelivery(kvp.Key, kvp.Value);
                }

                // The Core consumes what it asked for rather than storing it: a directive is a
                // hand-over, not a deposit, and the Core takes no delivery of its own accord
                // (CoreRuntime.CanAcceptInput is false for everything).
                if (haul.IsComplete) CompleteCoreHaul();
            }

            robot.ClearCargo();
            robot.TargetSite = null;
            robot.TargetHaul = null;
            robot.State = BuilderRobotState.Idle;
            robot.MoveTarget = robot.ParkPosition;
        }

        void MaterializeReadySegments(ConstructionSiteRuntime site)
        {
            while (site.CanMaterializeNextSegment())
            {
                BuildingRuntime segment = site.MaterializeNextSegment();
                _transport?.Register(segment);
                SegmentMaterialized?.Invoke(segment);
            }

            if (site.IsComplete) _queue.Remove(site);
        }

        void AssignRepatriation(BuilderRobotRuntime robot)
        {
            RepatriationJob job = _repatriationJobs[0];

            // Uncapped, like a construction pickup and for the same reason read backwards: a
            // demolished building's materials are the bill that built it, and a robot that can carry
            // one can carry the other.
            var cargo = new Dictionary<string, int>();
            foreach (var kvp in new List<KeyValuePair<string, int>>(job.Remaining))
            {
                cargo[kvp.Key] = kvp.Value;
                job.Remaining.Remove(kvp.Key);
            }

            if (job.Remaining.Count == 0) _repatriationJobs.Remove(job);
            if (cargo.Count == 0) return;

            object destination = FindRepatriationDestination(cargo);
            if (destination == null)
            {
                // Nothing anywhere can take this cargo - anti-deadlock (TASK_05_ROBOT_CONSTRUCTEUR.md
                // §5): the robot keeps it and a 20s countdown starts, right here, without moving.
                foreach (var kvp in cargo) robot.AddCargo(kvp.Key, kvp.Value);
                EnterBlocked(robot);
                return;
            }

            foreach (var kvp in cargo) robot.AddCargo(kvp.Key, kvp.Value);
            robot.DestinationContainer = destination;
            robot.MoveTarget = ContainerPosition(destination);
            robot.State = BuilderRobotState.Repatriating;
        }

        /// <summary>Core chest first, then every Storage in registration order - the first that can accept every item in the cargo at once wins (TASK_05_ROBOT_CONSTRUCTEUR.md §5). No partial split across containers.</summary>
        object FindRepatriationDestination(IReadOnlyDictionary<string, int> cargo)
        {
            foreach (StorageRuntime storage in StoragesInCollectionOrder())
            {
                bool fits = true;
                foreach (var kvp in cargo)
                {
                    if (!storage.CanAcceptFromRobot(kvp.Key, kvp.Value)) { fits = false; break; }
                }
                if (fits) return storage;
            }
            return null;
        }

        void PerformRepatriationDropoff(BuilderRobotRuntime robot)
        {
            if (robot.DestinationContainer is StorageRuntime storage)
            {
                bool fits = true;
                foreach (var kvp in robot.Cargo)
                {
                    if (!storage.CanAcceptFromRobot(kvp.Key, kvp.Value)) { fits = false; break; }
                }

                if (fits)
                {
                    foreach (var kvp in robot.Cargo) storage.AddFromRobot(kvp.Key, kvp.Value);
                    robot.ClearCargo();
                    robot.DestinationContainer = null;
                    robot.State = BuilderRobotState.Idle;
                    robot.MoveTarget = robot.ParkPosition;
                    return;
                }
            }

            // Destination filled up while the robot was travelling (e.g. another robot delivered
            // there first) - try again with the wider search, or go Blocked.
            object destination = FindRepatriationDestination(robot.Cargo);
            if (destination == null)
            {
                EnterBlocked(robot);
                return;
            }

            robot.DestinationContainer = destination;
            robot.MoveTarget = ContainerPosition(destination);
        }

        /// <summary>A cancelled site's already-committed cargo is dropped off exactly like a repatriation, not lost (TASK_05_ROBOT_CONSTRUCTEUR.md §4).</summary>
        void BeginDropOffCarriedCargo(BuilderRobotRuntime robot)
        {
            object destination = FindRepatriationDestination(robot.Cargo);
            if (destination == null)
            {
                EnterBlocked(robot);
                return;
            }

            robot.DestinationContainer = destination;
            robot.MoveTarget = ContainerPosition(destination);
            robot.State = BuilderRobotState.Repatriating;
        }

        void EnterBlocked(BuilderRobotRuntime robot)
        {
            robot.State = BuilderRobotState.Blocked;
            robot.BlockedCountdownRemaining = BuilderRobotRuntime.BlockedDestructionSeconds;
            robot.BlockedNotificationId = _notifications?.Post(
                NotificationSeverity.Warning,
                "Un robot ne peut plus se vider : plus aucun stockage disponible. Construisez un coffre pour le liberer.",
                BuilderRobotRuntime.BlockedDestructionSeconds,
                BuilderRobotRuntime.BlockedDestructionSeconds);
        }

        void TickBlocked(BuilderRobotRuntime robot, float deltaTime)
        {
            float remaining = (robot.BlockedCountdownRemaining ?? 0f) - deltaTime;
            robot.BlockedCountdownRemaining = remaining;
            if (robot.BlockedNotificationId.HasValue) _notifications?.UpdateCountdown(robot.BlockedNotificationId.Value, Mathf.Max(0f, remaining));

            if (remaining > 0f) return;

            robot.ClearCargo();
            robot.DestinationContainer = null;
            robot.BlockedCountdownRemaining = null;
            robot.BlockedNotificationId = null;
            robot.State = BuilderRobotState.Idle;
            robot.MoveTarget = robot.ParkPosition;
        }

        // ---- Échec notification (a site stuck on a genuinely unreservable ingredient) ----

        void UpdateStuckNotification()
        {
            ConstructionSiteRuntime stuck = null;
            string missingItemId = null;

            foreach (ConstructionSiteRuntime site in _queue)
            {
                if (site.IsComplete) continue;
                if (IsBlockedOnSomething(site, out string blockedItem))
                {
                    stuck = site;
                    missingItemId = blockedItem;
                    break;
                }
            }

            if (stuck == null)
            {
                if (_stuckSiteId.HasValue) ClearStuckNotification();
                return;
            }

            if (_stuckSiteId == stuck.Id) return;

            ClearStuckNotification();
            _stuckSiteId = stuck.Id;
            _stuckNotificationId = _notifications?.Post(
                NotificationSeverity.Warning,
                $"Chantier en attente : materiau manquant ({missingItemId}).",
                6f);
        }

        void ClearStuckNotification()
        {
            if (_stuckNotificationId.HasValue) _notifications?.Dismiss(_stuckNotificationId.Value);
            _stuckSiteId = null;
            _stuckNotificationId = null;
        }

        static bool IsBlockedOnSomething(ConstructionSiteRuntime site, out string blockedItemId)
        {
            foreach (var kvp in site.GetStillNeeded())
            {
                int reservedForItem = 0;
                foreach (Reservation reservation in site.Reservations)
                {
                    if (reservation.ItemId == kvp.Key) reservedForItem += reservation.Amount;
                }
                if (reservedForItem <= 0)
                {
                    blockedItemId = kvp.Key;
                    return true;
                }
            }
            blockedItemId = null;
            return false;
        }

        // ---- Container access helpers ----

        static Vector2 ContainerPosition(object container)
        {
            if (!(container is BuildingRuntime building)) return Vector2.zero;
            Vector2Int size = building.Definition.FootprintSize;
            return new Vector2(building.Cell.X + size.x / 2f, building.Cell.Y + size.y / 2f);
        }

        static Vector2 SitePosition(ConstructionSiteRuntime site)
        {
            int index = Mathf.Clamp(site.MaterializedCount, 0, site.Segments.Count - 1);
            BuildingRuntime segment = site.Segments[index];
            Vector2Int size = segment.Definition.FootprintSize;
            return new Vector2(segment.Cell.X + size.x / 2f, segment.Cell.Y + size.y / 2f);
        }

        static int ContainerTake(object container, string itemId, int amount)
        {
            if (container is StorageRuntime storage) return storage.TakeInput(itemId, amount);
            if (container is ProductionBuildingRuntime production) return production.TakeOutput(itemId, amount);
            return 0;
        }

        sealed class RepatriationJob
        {
            public Dictionary<string, int> Remaining;
        }

        // ---- Save/Restore (CONTRACTS.md §14 convention) ----

        /// <summary>
        /// segmentFactory mirrors ConstructionService.CreateForRestore exactly (no cost/placement
        /// check, just instantiate + occupy the grid) - passed in rather than called directly to
        /// keep Game.Gameplay free of a dependency on Game.Construction (PROJECT_ARCHITECTURE.md
        /// §4). Segments are reconstructed but deliberately NOT registered with TransportSystem
        /// here - a restored pending site is exactly as unfinished as it was when saved.
        /// </summary>
        public JObject CaptureState()
        {
            var sites = new JArray();
            foreach (ConstructionSiteRuntime site in _queue) sites.Add(site.CaptureState());

            var robots = new JArray();
            foreach (BuilderRobotRuntime robot in _robots) robots.Add(CaptureRobot(robot));

            var repatriations = new JArray();
            foreach (RepatriationJob job in _repatriationJobs)
            {
                repatriations.Add(new JObject { ["remaining"] = JObject.FromObject(job.Remaining) });
            }

            return new JObject
            {
                ["nextSiteId"] = _nextSiteId,
                ["sites"] = sites,
                ["robots"] = robots,
                ["repatriations"] = repatriations
            };
        }

        JObject CaptureRobot(BuilderRobotRuntime robot)
        {
            var json = new JObject
            {
                ["index"] = robot.Index,
                ["positionX"] = robot.Position.x,
                ["positionY"] = robot.Position.y,
                ["moveTargetX"] = robot.MoveTarget.x,
                ["moveTargetY"] = robot.MoveTarget.y,
                ["state"] = robot.State.ToString(),
                ["cargo"] = JObject.FromObject(robot.Cargo),
                ["pending"] = JObject.FromObject(robot.Pending),
                ["targetSiteId"] = robot.TargetSite?.Id,
                ["blockedCountdownRemaining"] = robot.BlockedCountdownRemaining
            };

            if (robot.SourceContainer is BuildingRuntime source)
            {
                json["sourceContainerCellX"] = source.Cell.X;
                json["sourceContainerCellY"] = source.Cell.Y;
            }
            if (robot.DestinationContainer is BuildingRuntime destination)
            {
                json["destinationContainerCellX"] = destination.Cell.X;
                json["destinationContainerCellY"] = destination.Cell.Y;
            }

            return json;
        }

        /// <summary>
        /// Rebuilds every site/robot/repatriation job from a save. Must run after every real
        /// building (including a restored Core chest/Storage) is already registered with
        /// TransportSystem and placed in Game.Grid, since containers are re-resolved by cell.
        /// Tolerates a blob missing these keys entirely (falls back to two idle robots parked, no
        /// site, no repatriation - TASK_05_ROBOT_CONSTRUCTEUR.md §8) without throwing.
        /// </summary>
        public void RestoreState(JObject state, Func<BuildingDefinition, GridCoord, Direction, BuildingRuntime> segmentFactory, Func<string, BuildingDefinition> resolveDefinition)
        {
            _queue.Clear();
            _repatriationJobs.Clear();
            if (state == null) return;

            _nextSiteId = state.Value<int?>("nextSiteId") ?? 0;

            if (state["sites"] is JArray sites)
            {
                foreach (JToken siteToken in sites)
                {
                    ConstructionSiteRuntime site = RestoreSite((JObject)siteToken, segmentFactory, resolveDefinition);
                    if (site != null) _queue.Add(site);
                }
            }

            if (state["repatriations"] is JArray repatriations)
            {
                foreach (JToken jobToken in repatriations)
                {
                    var remaining = new Dictionary<string, int>();
                    if (jobToken["remaining"] is JObject remainingJson)
                    {
                        foreach (var property in remainingJson.Properties())
                        {
                            remaining[property.Name] = property.Value.Value<int>();
                        }
                    }
                    if (remaining.Count > 0) _repatriationJobs.Add(new RepatriationJob { Remaining = remaining });
                }
            }

            if (state["robots"] is JArray robots)
            {
                foreach (JToken robotToken in robots)
                {
                    RestoreRobot((JObject)robotToken);
                }
            }
        }

        ConstructionSiteRuntime RestoreSite(JObject siteJson, Func<BuildingDefinition, GridCoord, Direction, BuildingRuntime> segmentFactory, Func<string, BuildingDefinition> resolveDefinition)
        {
            if (!(siteJson["segments"] is JArray segmentsJson) || segmentsJson.Count == 0) return null;

            ConstructionSiteRuntime site = null;
            foreach (JToken segmentToken in segmentsJson)
            {
                BuildingDefinition definition = resolveDefinition(segmentToken.Value<string>("definitionId"));
                if (definition == null) return null;

                var cell = new GridCoord(segmentToken.Value<int>("cellX"), segmentToken.Value<int>("cellY"));
                var rotation = (Direction)segmentToken.Value<int>("rotation");
                BuildingRuntime segment = segmentFactory(definition, cell, rotation);
                if (segment == null) return null;

                if (site == null) site = new ConstructionSiteRuntime(siteJson.Value<int?>("id") ?? _nextSiteId, segment);
                else site.AddSegment(segment);
            }

            site?.RestoreCounters(siteJson);

            if (site != null && siteJson["reservations"] is JArray reservationsJson)
            {
                foreach (JToken reservationToken in reservationsJson)
                {
                    var containerCell = new GridCoord(reservationToken.Value<int>("containerCellX"), reservationToken.Value<int>("containerCellY"));
                    if (!(_grid.GetOccupant(containerCell) is object container)) continue;
                    site.AddReservation(container, reservationToken.Value<string>("itemId"), reservationToken.Value<int>("amount"));
                }
            }

            return site;
        }

        void RestoreRobot(JObject robotJson)
        {
            int index = robotJson.Value<int?>("index") ?? 0;
            BuilderRobotRuntime robot = index >= 0 && index < _robots.Count ? _robots[index] : null;
            if (robot == null) return;

            robot.Position = new Vector2(robotJson.Value<float?>("positionX") ?? robot.ParkPosition.x, robotJson.Value<float?>("positionY") ?? robot.ParkPosition.y);
            robot.MoveTarget = new Vector2(robotJson.Value<float?>("moveTargetX") ?? robot.Position.x, robotJson.Value<float?>("moveTargetY") ?? robot.Position.y);

            string stateName = robotJson.Value<string>("state");
            robot.State = Enum.TryParse(stateName, out BuilderRobotState parsedState) ? parsedState : BuilderRobotState.Idle;

            if (robotJson["cargo"] is JObject cargoJson)
            {
                foreach (var property in cargoJson.Properties())
                {
                    robot.AddCargo(property.Name, property.Value.Value<int>());
                }
            }

            robot.ClearPending();
            if (robotJson["pending"] is JObject pending)
            {
                foreach (JProperty property in pending.Properties())
                {
                    robot.AddPending(property.Name, property.Value.Value<int>());
                }
            }

            int? targetSiteId = robotJson.Value<int?>("targetSiteId");
            if (targetSiteId.HasValue)
            {
                foreach (ConstructionSiteRuntime site in _queue)
                {
                    if (site.Id == targetSiteId.Value) { robot.TargetSite = site; break; }
                }
            }

            int? sourceCellX = robotJson.Value<int?>("sourceContainerCellX");
            int? sourceCellY = robotJson.Value<int?>("sourceContainerCellY");
            if (sourceCellX.HasValue && sourceCellY.HasValue)
            {
                robot.SourceContainer = _grid.GetOccupant(new GridCoord(sourceCellX.Value, sourceCellY.Value));
            }

            int? destCellX = robotJson.Value<int?>("destinationContainerCellX");
            int? destCellY = robotJson.Value<int?>("destinationContainerCellY");
            if (destCellX.HasValue && destCellY.HasValue)
            {
                robot.DestinationContainer = _grid.GetOccupant(new GridCoord(destCellX.Value, destCellY.Value));
            }

            float? blockedCountdown = robotJson.Value<float?>("blockedCountdownRemaining");
            robot.BlockedCountdownRemaining = blockedCountdown;
            if (robot.State == BuilderRobotState.Blocked && blockedCountdown.HasValue)
            {
                robot.BlockedNotificationId = _notifications?.Post(
                    NotificationSeverity.Warning,
                    "Un robot ne peut plus se vider : plus aucun stockage disponible. Construisez un coffre pour le liberer.",
                    blockedCountdown.Value,
                    blockedCountdown.Value);
            }
        }
    }
}

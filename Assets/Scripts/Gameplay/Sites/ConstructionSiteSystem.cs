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
    /// wins a newly available unit over a younger one. A site with nothing earmarked right now is
    /// simply skipped by robot dispatch (never blocking); the robots always serve the oldest site
    /// that currently has something reserved-and-not-yet-delivered to bring - "un seul chantier à
    /// la fois" is about simultaneous execution, not queue order.
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

        /// <summary>Fired the instant a segment (a whole building for a single-building site, one belt piece for a conveyor/splitter run) has received its full cost and becomes a real, registered building - the caller (Presentation) spawns its view and registers it with Transport/ItemVisuals, exactly like an immediate TryPlace used to.</summary>
        public event Action<BuildingRuntime> SegmentMaterialized;

        public IReadOnlyList<ConstructionSiteRuntime> Sites => _queue;
        public IReadOnlyList<BuilderRobotRuntime> Robots => _robots;

        public ConstructionSiteSystem(TransportSystem transport, GridRuntime grid, NotificationSystem notifications, Vector2 robotParkOrigin)
        {
            _transport = transport;
            _grid = grid;
            _notifications = notifications;

            _robots.Add(new BuilderRobotRuntime(0, robotParkOrigin + new Vector2(-0.6f, -0.3f)));
            _robots.Add(new BuilderRobotRuntime(1, robotParkOrigin + new Vector2(0.6f, -0.3f)));
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
                    BuildingDefinition definition = site.PrimaryDefinition;
                    if (definition is ConveyorDefinition || definition is SplitterDefinition || definition is CrossroadDefinition) continue;
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

            // A cost-free segment owes nothing and must not wait for a delivery that will never
            // come - it materializes on the spot.
            MaterializeReadySegments(site);
            return site;
        }

        /// <summary>Appends one more segment to an in-progress conveyor/splitter/crossroad drag's site - still one chantier for the whole gesture (TASK_05_ROBOT_CONSTRUCTEUR.md §3).</summary>
        public void AppendSegment(ConstructionSiteRuntime site, BuildingRuntime segment)
        {
            site.AddSegment(segment);
            if (!_queue.Contains(site)) _queue.Add(site);
            RunReservationPass();
            MaterializeReadySegments(site);
        }

        /// <summary>
        /// Cancels a still-pending site: releases every reservation it held back to their
        /// containers (nothing physically moves for those), clears the grid cells its
        /// not-yet-materialized segments occupied, and - if a robot is already carrying cargo
        /// committed to this site - lets that robot keep its cargo and drop it off during its next
        /// return to idle instead of losing it (TASK_05_ROBOT_CONSTRUCTEUR.md §4).
        /// </summary>
        public bool CancelSite(ConstructionSiteRuntime site)
        {
            if (!_queue.Remove(site)) return false;

            site.ReleaseAllContainerReservations();

            for (int i = site.MaterializedCount; i < site.Segments.Count; i++)
            {
                BuildingRuntime segment = site.Segments[i];
                _grid.ClearOccupantFootprint(segment.Cell, segment.Definition.FootprintCells);
            }

            foreach (BuilderRobotRuntime robot in _robots)
            {
                if (!ReferenceEquals(robot.TargetSite, site)) continue;
                robot.TargetSite = null;
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
            return true;
        }

        /// <summary>Whether `runtime` is still a pending segment of some site (not yet materialized/registered) - used by the demolition input path to route a click at that cell to CancelSite instead of ConstructionService.TryDemolish, which must never see an unpaid, unregistered building.</summary>
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

        public void Tick(float deltaTime)
        {
            RunReservationPass();
            UpdateStuckNotification();

            foreach (BuilderRobotRuntime robot in _robots)
            {
                TickRobot(robot, deltaTime);
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
        }

        int TotalReserved(object container, string itemId)
        {
            int total = 0;
            foreach (ConstructionSiteRuntime site in _queue)
            {
                foreach (Reservation reservation in site.Reservations)
                {
                    if (ReferenceEquals(reservation.Container, container) && reservation.ItemId == itemId) total += reservation.Amount;
                }
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
        public IReadOnlyDictionary<string, int> GetAvailableAggregate()
        {
            var totals = new Dictionary<string, int>();

            foreach (StorageRuntime storage in StoragesInCollectionOrder())
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
        /// The one site the robots are working on right now: the oldest that is not blocked -
        /// blocked meaning it has nothing reserved AND no robot already carrying for it, i.e.
        /// nothing can happen for it at this instant. Such a site is skipped rather than holding
        /// up everything behind it, and reclaims the robots as soon as a reservation lands on it
        /// again. Everything else waits: only one chantier is ever served at a time, both robots
        /// on the same one (TASK_05_ROBOT_CONSTRUCTEUR.md §2).
        /// </summary>
        ConstructionSiteRuntime FindActiveSite()
        {
            foreach (ConstructionSiteRuntime site in _queue)
            {
                if (site.IsComplete) continue;
                if (site.Reservations.Count > 0 || HasRobotServing(site)) return site;
            }
            return null;
        }

        bool HasRobotServing(ConstructionSiteRuntime site)
        {
            foreach (BuilderRobotRuntime robot in _robots)
            {
                if (ReferenceEquals(robot.TargetSite, site)) return true;
            }
            return false;
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
            ConstructionSiteRuntime site = FindActiveSite();
            if (site != null && site.Reservations.Count > 0)
            {
                // One source container per round trip, filled to the robot's capacity - a robot
                // never leaves with one unit when four of the same item are earmarked in the same
                // chest (that is the difference between seven waves and fifty-five for a belt run).
                Reservation chosen = site.Reservations[0];
                int available = site.ReservedIn(chosen.Container, chosen.ItemId);
                int amount = Mathf.Min(BuilderRobotRuntime.Capacity, available);
                site.ReleaseReservationForPickup(chosen.Container, chosen.ItemId, amount);

                robot.TargetSite = site;
                robot.SourceContainer = chosen.Container;
                robot.PendingItemId = chosen.ItemId;
                robot.PendingAmount = amount;
                robot.MoveTarget = ContainerPosition(chosen.Container);
                robot.State = BuilderRobotState.MovingToSource;
                return;
            }

            if (_repatriationJobs.Count > 0)
            {
                AssignRepatriation(robot);
            }
        }

        void PerformPickup(BuilderRobotRuntime robot)
        {
            robot.State = BuilderRobotState.Loading;

            int taken = ContainerTake(robot.SourceContainer, robot.PendingItemId, robot.PendingAmount);
            robot.AddCargo(robot.PendingItemId, taken);
            if (taken < robot.PendingAmount && robot.TargetSite != null)
            {
                robot.TargetSite.ReleaseCommitment(robot.PendingItemId, robot.PendingAmount - taken);
            }

            robot.SourceContainer = null;
            robot.PendingItemId = null;
            robot.PendingAmount = 0;
            robot.MoveTarget = SitePosition(robot.TargetSite);
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
                MaterializeReadySegments(site);
            }

            robot.ClearCargo();
            robot.TargetSite = null;
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

            var cargo = new Dictionary<string, int>();
            int remainingCapacity = BuilderRobotRuntime.Capacity;
            foreach (var kvp in new List<KeyValuePair<string, int>>(job.Remaining))
            {
                if (remainingCapacity <= 0) break;
                int take = Mathf.Min(kvp.Value, remainingCapacity);
                cargo[kvp.Key] = take;
                remainingCapacity -= take;

                int left = kvp.Value - take;
                if (left <= 0) job.Remaining.Remove(kvp.Key);
                else job.Remaining[kvp.Key] = left;
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
                ["pendingItemId"] = robot.PendingItemId,
                ["pendingAmount"] = robot.PendingAmount,
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

            robot.PendingItemId = robotJson.Value<string>("pendingItemId");
            robot.PendingAmount = robotJson.Value<int?>("pendingAmount") ?? 0;

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

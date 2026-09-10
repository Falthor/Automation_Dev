using System.Collections.Generic;
using UnityEngine;

namespace Game.Gameplay.Sites
{
    /// <summary>
    /// One builder robot (TASK_05_ROBOT_CONSTRUCTEUR.md §2/§7): free 8-directional movement (no
    /// pathfinding/obstacle avoidance), 4-unit cargo capacity, driven entirely by
    /// ConstructionSiteSystem's central tick. This type only holds state and knows how to move its
    /// own Position toward a target - every decision (what to fetch, where to deliver, when to
    /// park) is made by ConstructionSiteSystem, exactly like ConveyorRuntime holds belt state while
    /// TransportSystem drives it. The view only ever reads Position (PROJECT_ARCHITECTURE.md §3.1 -
    /// runtime is authoritative, presentation interpolates nothing of its own).
    /// </summary>
    public sealed class BuilderRobotRuntime
    {
        /// <summary>
        /// How much a robot may carry <b>for a Core directive</b>, and only for that.
        ///
        /// Construction work is deliberately uncapped: one robot is meant to be able to fetch a
        /// whole building's bill in one trip, so a Foundry is one wave rather than five. A directive
        /// is the opposite kind of job - a hand-over the player chose to take on, whose pacing is
        /// part of what it asks - so its cap stays where it was.
        /// </summary>
        public const int DirectiveCargoCapacity = 5;
        public const float SpeedCellsPerSecond = 5.5f;
        public const float BlockedDestructionSeconds = 20f;

        public int Index { get; }

        /// <summary>Continuous position in grid-space (same numeric space as GridCoord, i.e. multiply by GridRuntime.CellSize for world space) - the runtime's sole authoritative position.</summary>
        public Vector2 Position { get; set; }

        public Vector2 ParkPosition { get; set; }

        public BuilderRobotState State { get; set; } = BuilderRobotState.Idle;

        readonly Dictionary<string, int> _cargo = new Dictionary<string, int>();
        public IReadOnlyDictionary<string, int> Cargo => _cargo;
        public int CargoTotal
        {
            get
            {
                int total = 0;
                foreach (var kvp in _cargo) total += kvp.Value;
                return total;
            }
        }

        /// <summary>Move target for MovingToSource/MovingToSite/Repatriating - grid-space, matching Position.</summary>
        public Vector2 MoveTarget { get; set; }

        public ConstructionSiteRuntime TargetSite { get; set; }

        /// <summary>The Core delivery this robot is carrying for, when it is not serving a site. Exactly one of TargetSite/TargetHaul is ever set.</summary>
        public CoreHaulJob TargetHaul { get; set; }
        public object SourceContainer { get; set; }
        public object DestinationContainer { get; set; }

        /// <summary>
        /// What this robot is travelling to fetch (MovingToSource) - not yet in Cargo until
        /// PerformPickup runs.
        ///
        /// <b>Several items, from one container.</b> It was a single item/amount pair, which capped a
        /// trip at one ingredient however much of the bill sat in the same chest: a building needing
        /// plates and wire took two round trips even with both in the Core's own reserve. One source
        /// per trip is still the rule (no multi-stop tours), but a trip now takes everything that
        /// source holds for this job.
        /// </summary>
        readonly Dictionary<string, int> _pending = new Dictionary<string, int>();

        public IReadOnlyDictionary<string, int> Pending => _pending;

        public int PendingOf(string itemId) => _pending.TryGetValue(itemId, out int amount) ? amount : 0;

        public int PendingTotal
        {
            get
            {
                int total = 0;
                foreach (var kvp in _pending) total += kvp.Value;
                return total;
            }
        }

        public void AddPending(string itemId, int amount)
        {
            if (amount <= 0) return;
            _pending[itemId] = PendingOf(itemId) + amount;
        }

        public void ClearPending() => _pending.Clear();

        /// <summary>Seconds remaining before a Blocked robot's cargo is destroyed (TASK_05_ROBOT_CONSTRUCTEUR.md §5's anti-deadlock). Null unless State == Blocked.</summary>
        public float? BlockedCountdownRemaining { get; set; }

        /// <summary>Id of the NotificationSystem entry currently showing this robot's blocked countdown, if any - so it can be dismissed/updated as the countdown progresses.</summary>
        public int? BlockedNotificationId { get; set; }

        public BuilderRobotRuntime(int index, Vector2 parkPosition)
        {
            Index = index;
            ParkPosition = parkPosition;
            Position = parkPosition;
            MoveTarget = parkPosition;
        }

        public void AddCargo(string itemId, int amount)
        {
            if (amount <= 0) return;
            _cargo[itemId] = (_cargo.TryGetValue(itemId, out int existing) ? existing : 0) + amount;
        }

        public void ClearCargo() => _cargo.Clear();

        /// <summary>Advances Position toward MoveTarget at SpeedCellsPerSecond. Returns true the tick it arrives (snaps exactly onto MoveTarget).</summary>
        public bool AdvanceTowardTarget(float deltaTime)
        {
            Vector2 toTarget = MoveTarget - Position;
            float distance = toTarget.magnitude;
            float step = SpeedCellsPerSecond * deltaTime;

            if (distance <= step)
            {
                Position = MoveTarget;
                return true;
            }

            Position += toTarget / distance * step;
            return false;
        }
    }
}

using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// Everything the expedition system is tuned by: when robots arrive, how long they last, how long
    /// a mission takes and what it pays.
    ///
    /// <b>Nothing here is derived.</b> The CU threshold that makes robots appear is a fraction, never
    /// a number of CU - see <see cref="RobotThresholdFractionOfCap"/>. The threshold in CU is computed
    /// from it and the reserve's own cap, and is therefore not a field anyone can set.
    /// </summary>
    [CreateAssetMenu(fileName = "MissionSettings", menuName = "Game/World/Mission Settings")]
    public sealed class MissionSettings : ScriptableObject
    {
        [Header("Robots explorateurs")]

        [SerializeField, Min(0)] int explorerRobotCount = 2;

        /// <summary>Missions, not minutes. Counted in missions is what makes the budget guaranteeable: a robot that runs out mid-introduction with the player also out of CU would leave no way forward at all.</summary>
        [SerializeField, Min(1)] int missionsPerRobot = 10;

        /// <summary>
        /// How far the CU reserve must fall, as a fraction of its own cap, before the robots arrive.
        ///
        /// <b>A fraction and never a number of CU.</b> The absolute figure has already stopped meaning
        /// what it meant twice: the cap went 25 000 → 60 000 → 70 000 while the threshold stayed put,
        /// turning an emergency trigger into an introduction trigger with nothing to signal it. As a
        /// fraction it survives the next move. 0.357143 of the shipped 70 000 cap is 25 000.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float robotThresholdFractionOfCap = 25000f / 70000f;

        [Header("Missions")]

        [SerializeField, Min(1)] int maxConcurrentMissions = 2;

        /// <summary>A prospection: near, and the shortest mission in the game.</summary>
        [SerializeField, Min(1f)] float prospectionSeconds = 180f;

        /// <summary>Far reconnaissance. Longer than a prospection before distance is even counted - it is the base, and distance adds to it.</summary>
        [SerializeField, Min(1f)] float explorationSeconds = 300f;

        [SerializeField, Min(1f)] float recoverySeconds = 360f;

        /// <summary>Extra seconds per cell of distance from the Core. What makes a far target actually feel far, and the only thing separating the two reconnaissances beyond their reward.</summary>
        [SerializeField, Min(0f)] float secondsPerDistanceCell = 0.5f;

        [Header("Récompenses de l'introduction")]

        /// <summary>
        /// <b>Introduction only.</b> After the Datacenter is bootstrapped, missions pay in map, sites
        /// and plans - never in currency. Two parallel economies would have the player arbitrating
        /// between building a factory and sending robots.
        /// </summary>
        [SerializeField, Min(0f)] float reconnaissanceReward = 500f;

        [SerializeField, Min(0f)] float recoveryReward = 1500f;

        /// <summary>
        /// How many reconnaissances pay. The map stays revealable afterwards; it simply stops paying.
        ///
        /// <b>A budget rather than a set of places.</b> The specification's finite gisement was written
        /// for a 300-cell map, where eight sectors were most of what a player could reach. The mining
        /// band of a 10 000-cell map holds about 291 - paying for each would be 145 000 CU, the
        /// currency fountain that the finite gisement exists to prevent. Counting payouts keeps the
        /// specification's totals exactly and does not move with the size of the world.
        /// </summary>
        [SerializeField, Min(0)] int paidReconnaissances = 8;

        [SerializeField, Min(0)] int paidRecoveries = 5;

        [Header("Le plancher à zéro CU")]

        /// <summary>
        /// The regenerating payout: small, slow, and always there.
        ///
        /// <b>The one thing that makes a run mathematically unloseable.</b> Launching costs no CU, so
        /// a player at zero with no production still has an action - but only if that action can pay.
        /// Once the budgets above are spent, this is what remains, and it is deliberately poor enough
        /// that nobody would farm it by choice.
        /// </summary>
        [SerializeField, Min(0f)] float regeneratingReward = 100f;

        /// <summary>How long before the regenerating payout is available again.</summary>
        [SerializeField, Min(1f)] float regeneratingCooldownSeconds = 600f;

        public int ExplorerRobotCount => explorerRobotCount;
        public int MissionsPerRobot => missionsPerRobot;
        public float RobotThresholdFractionOfCap => robotThresholdFractionOfCap;
        public int MaxConcurrentMissions => maxConcurrentMissions;
        public float ProspectionSeconds => prospectionSeconds;
        public float ExplorationSeconds => explorationSeconds;
        public float RecoverySeconds => recoverySeconds;
        public float SecondsPerDistanceCell => secondsPerDistanceCell;
        public float ReconnaissanceReward => reconnaissanceReward;
        public float RecoveryReward => recoveryReward;
        public int PaidReconnaissances => paidReconnaissances;
        public int PaidRecoveries => paidRecoveries;
        public float RegeneratingReward => regeneratingReward;
        public float RegeneratingCooldownSeconds => regeneratingCooldownSeconds;

        /// <summary>
        /// The reserve level at which the robots arrive, in CU.
        ///
        /// Derived, so it is a method rather than a field: taking the cap as an argument is what makes
        /// it impossible to store a threshold that has stopped agreeing with the reserve it describes.
        /// </summary>
        public float RobotThresholdCu(float reserveCap) => reserveCap * robotThresholdFractionOfCap;
    }
}

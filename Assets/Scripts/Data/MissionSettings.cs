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

        /// <summary>
        /// What an explorer robot looks like where it stands. Data rather than a scene reference so
        /// the fleet's look travels with the settings that say how many there are.
        /// </summary>
        [SerializeField] Sprite explorerRobotSprite;

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

        /// <summary>
        /// The field study - <c>MissionKind.EtudeDeTerrain</c>. Short, so it is the one a player launches
        /// rather than leave a robot idle.
        ///
        /// <b>"Field study", never "reconnaissance".</b> The design documents used that one word for two
        /// things - this kind, and the category covering Prospection and ExplorationLointaine that
        /// <see cref="ReconnaissanceReward"/> pays. The word now belongs to the category alone; the kind
        /// is <c>MissionKind.EtudeDeTerrain</c> and its settings are named for it.
        /// </summary>
        [SerializeField, Min(1f)] float fieldStudySeconds = 120f;

        /// <summary>
        /// What a field study pays. Its own figure rather than a draw on the introduction's finite
        /// reconnaissance budget: what bounds it is the number of field-study sites a zone holds, not a
        /// count of payouts.
        /// </summary>
        [SerializeField, Min(0f)] float fieldStudyReward = 250f;

        /// <summary>How often a field study turns up one of the zone's hidden sites. Drawn at launch and carried by the mission, so a reload cannot re-roll it.</summary>
        [SerializeField, Range(0f, 1f)] float fieldStudyHiddenSiteChance = 1f / 3f;

        /// <summary>Extra seconds per cell of distance from the Core. What makes a far target actually feel far, and the only thing separating the two reconnaissances beyond their reward.</summary>
        [SerializeField, Min(0f)] float secondsPerDistanceCell = 0.5f;

        [Header("Récompenses de l'introduction")]

        /// <summary>
        /// <b>Introduction only.</b> After the Datacenter is bootstrapped, missions pay in map, sites
        /// and plans - never in currency. Two parallel economies would have the player arbitrating
        /// between building a factory and sending robots.
        ///
        /// <b>"Reconnaissance" here means exactly one thing: the pair</b> - Prospection and
        /// ExplorationLointaine, which SPEC_EXPEDITIONS §4.9 calls "les deux Reconnaissances". It used to
        /// also name a mission kind, and every reader had to work out which was meant; that kind is
        /// <c>MissionKind.EtudeDeTerrain</c> now, and it pays <see cref="FieldStudyReward"/> instead of
        /// drawing on this budget.
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
        public Sprite ExplorerRobotSprite => explorerRobotSprite;
        public int MissionsPerRobot => missionsPerRobot;
        public float RobotThresholdFractionOfCap => robotThresholdFractionOfCap;
        public int MaxConcurrentMissions => maxConcurrentMissions;
        public float ProspectionSeconds => prospectionSeconds;
        public float ExplorationSeconds => explorationSeconds;
        public float RecoverySeconds => recoverySeconds;
        public float FieldStudySeconds => fieldStudySeconds;
        public float FieldStudyReward => fieldStudyReward;
        public float FieldStudyHiddenSiteChance => fieldStudyHiddenSiteChance;
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

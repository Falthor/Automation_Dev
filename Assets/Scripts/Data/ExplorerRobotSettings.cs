using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// How an explorer robot wanders. <b>A prototype's dials, all of them in one place</b> - the
    /// point of the exercise is watching the shape of the trace on screen and moving numbers until
    /// it looks right, so not one of them is a constant in the code.
    ///
    /// The three steering strengths are turn rates in degrees per second, and they are what the
    /// wander is made of: a continuous drift that makes the path meander, an attraction that leans
    /// towards whichever side is less explored, and a recall that bends the heading back inwards
    /// past <see cref="MaxRadiusCells"/>. They compose - nothing here overrides anything else.
    ///
    /// <b>A turn rate is a curve radius, read against the speed.</b> Worth knowing before touching
    /// them: at <c>v</c> cells per second and <c>w</c> degrees per second the robot turns on a circle
    /// of radius <c>v / (w x pi / 180)</c>. At the shipped 2 and 6 that is a 19-cell arc, which reads
    /// as a wide meander; at 30 degrees per second it would be 3.8 cells, which reads as a robot
    /// spinning on the spot. That is the whole reason the drift is small.
    /// </summary>
    [CreateAssetMenu(fileName = "ExplorerRobotSettings", menuName = "Game/World/Explorer Robot Settings")]
    public sealed class ExplorerRobotSettings : ScriptableObject
    {
        [Header("La flotte")]

        /// <summary>
        /// How many robots stand at the base. <b>Its own count, not the mission fleet's.</b> Free
        /// exploration runs beside the mission system rather than through it, so it does not read
        /// <c>MissionSettings.explorerRobotCount</c> - and a robot here spends no mission charge.
        /// </summary>
        [SerializeField, Min(0)] int robotCount = 2;

        /// <summary>What a robot looks like. Optional: null falls back to a plain coloured square, so a scene with no art still shows something moving.</summary>
        [SerializeField] Sprite robotSprite;

        [Header("Déplacement")]

        [SerializeField, Min(0.1f)] float speedCellsPerSecond = 2f;

        /// <summary>How far from the Core a robot may get before the recall below starts bending it back. Not a wall - see <see cref="BoundaryTurnDegreesPerSecond"/>.</summary>
        [SerializeField, Min(1f)] float maxRadiusCells = 330f;

        /// <summary>What a robot uncovers around itself as it goes.</summary>
        [SerializeField, Min(1f)] float revealRadiusCells = 6f;

        [Header("Errance")]

        /// <summary>
        /// Peak turn rate of the continuous drift. Small on purpose: this is what makes the trace
        /// serpentine, and anything much larger turns a meander into a spiral (see the class summary).
        /// </summary>
        [SerializeField, Min(0f)] float driftDegreesPerSecond = 6f;

        /// <summary>
        /// How fast the drift's noise itself evolves, in cycles per second. This is the length of one
        /// meander, and it is the reason the drift is a noise sampled over time rather than a fresh
        /// draw per frame: a new random number every frame averages to nothing and leaves the robot
        /// shivering in a straight line. At 0.08 one bend lasts about twelve seconds.
        /// </summary>
        [SerializeField, Min(0.001f)] float driftCyclesPerSecond = 0.08f;

        /// <summary>Turn rate of the pull towards the unknown, at full contrast - one probe entirely on discovered ground and the other entirely on unknown. Scaled linearly by the difference, so ordinary ground barely steers at all.</summary>
        [SerializeField, Min(0f)] float unknownTurnDegreesPerSecond = 20f;

        /// <summary>How far ahead the two probes sit, and the "quelques dizaines de cases" of the brief. Too short and the robot reacts to what it has just revealed itself; too long and it steers by ground it will never reach.</summary>
        [SerializeField, Min(1f)] float probeDistanceCells = 30f;

        /// <summary>Angle of each probe off the current heading. 45 degrees puts them squarely left and right of where the robot is going without either looking backwards.</summary>
        [SerializeField, Range(5f, 90f)] float probeAngleDegrees = 45f;

        [Header("Rappel à la limite")]

        /// <summary>How hard the heading is bent back inwards past the limit. Well above the drift, so leaving is never a tug-of-war the drift can win.</summary>
        [SerializeField, Min(0f)] float boundaryTurnDegreesPerSecond = 30f;

        /// <summary>Over how many cells past the limit the recall ramps from nothing to its full rate. A step change would snap the heading; a ramp makes it an arc.</summary>
        [SerializeField, Min(1f)] float boundaryRampCells = 40f;

        public int RobotCount => robotCount;
        public Sprite RobotSprite => robotSprite;
        public float SpeedCellsPerSecond => speedCellsPerSecond;
        public float MaxRadiusCells => maxRadiusCells;
        public float RevealRadiusCells => revealRadiusCells;
        public float DriftDegreesPerSecond => driftDegreesPerSecond;
        public float DriftCyclesPerSecond => driftCyclesPerSecond;
        public float UnknownTurnDegreesPerSecond => unknownTurnDegreesPerSecond;
        public float ProbeDistanceCells => probeDistanceCells;
        public float ProbeAngleDegrees => probeAngleDegrees;
        public float BoundaryTurnDegreesPerSecond => boundaryTurnDegreesPerSecond;
        public float BoundaryRampCells => boundaryRampCells;

        /// <summary>
        /// How wide a patch each probe measures, rather than the single cell under it.
        ///
        /// <b>Derived from the reveal radius, so it cannot disagree with it.</b> A probe answers
        /// "would going that way uncover anything", and the unit of uncovering is the disc the robot
        /// writes - so a probe reading one cell would flicker on the noise of its own trail's edge,
        /// while one reading a robot-sized patch reads the frontier.
        /// </summary>
        public float ProbeSpreadCells => revealRadiusCells;
    }
}

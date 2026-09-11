using UnityEngine;

namespace Game.Gameplay.Exploration
{
    /// <summary>
    /// One explorer robot: where it is, which way it is pointing, and what it is doing.
    ///
    /// <b>State only.</b> Every decision - which way to lean, when the boundary bites, what to
    /// uncover - is made by <see cref="ExplorerRobotSystem"/>, the same split
    /// <c>BuilderRobotRuntime</c> keeps with <c>ConstructionSiteSystem</c> and
    /// <c>ConveyorRuntime</c> with <c>TransportSystem</c>. A robot knows how to move itself one step
    /// along its own heading and nothing else.
    ///
    /// <b>The heading is simulation, not presentation</b> - unlike the builder drone, whose facing
    /// the view derives from its move target. Here it is the only thing that says where the robot
    /// will be next: there is no destination to point at, so the heading is the state, and it is
    /// saved.
    /// </summary>
    public sealed class ExplorerRobotRuntime
    {
        public int Index { get; }

        /// <summary>Where the robot rests and what <see cref="ExplorerRobotState.Returning"/> aims at.</summary>
        public Vector2 HomePosition { get; }

        /// <summary>Continuous position in grid space - the same numeric space as GridCoord, i.e. multiply by GridRuntime.CellSize for world space.</summary>
        public Vector2 Position { get; set; }

        /// <summary>Degrees counter-clockwise from +X, so <see cref="Forward"/> is plain cosine/sine. Not wrapped by this type; the system wraps it once per tick.</summary>
        public float HeadingDegrees { get; set; }

        public ExplorerRobotState State { get; set; } = ExplorerRobotState.Idle;

        /// <summary>
        /// Where the drift's noise has got to, in cycles. Saved with everything else: it is what
        /// makes a reloaded robot carry on the bend it was in the middle of rather than snapping
        /// onto a fresh one.
        /// </summary>
        public float DriftPhase { get; set; }

        /// <summary>
        /// How many times this robot has set out. What spaces two sorties apart - see
        /// <see cref="ExplorerRobotSystem.DepartureBearing"/> - so it is state rather than a counter
        /// for display, and it travels in the save.
        /// </summary>
        public int SortieCount { get; set; }

        /// <summary>Where the last reveal disc was written, so the system can space them a cell apart instead of writing one per frame. Presentation-free bookkeeping; not saved, because a reloaded robot stands on ground it has already uncovered.</summary>
        public Vector2 LastRevealPosition { get; set; }

        /// <summary>Datacards carried, capped by <c>ExplorerRobotSettings.MaxCards</c>. Spent the instant the robot docks - never an item, never in a container - and saved, so a reloaded robot keeps what it was carrying.</summary>
        public int Cards { get; set; }

        /// <summary>
        /// Newly discovered ground credited towards the next card, in cells.
        ///
        /// A float because a card's threshold is jittered and lands off a whole number. It carries
        /// over rather than being cleared at each card, so no fraction of explored ground is ever
        /// lost to rounding.
        /// </summary>
        public float NewCellsSinceLastCard { get; set; }

        /// <summary>
        /// How many cards this robot has ever earned. <b>The index the next card's jittered threshold
        /// is drawn from</b>, which is why it is state and travels in the save: without it a reload
        /// would re-roll the threshold the robot is already part-way towards.
        /// </summary>
        public int CardsDrawnEver { get; set; }

        /// <summary>Whether the "stock full" alert has already been raised for the load this robot is carrying. Cleared when it unloads, so the alert is once per filling rather than once per run.</summary>
        public bool StockAlertRaised { get; set; }

        /// <summary>
        /// The sector this robot last materialised the ground around, or -1. What makes the
        /// materialisation fire once per sector entered rather than once per reveal - not saved,
        /// because re-materialising a block is idempotent and costs one pass.
        /// </summary>
        public int LastMaterialisedSector { get; set; } = -1;

        /// <summary>
        /// Consecutive reveals that turned up no new ground at all. What lets the panel say the robot
        /// is going back over what it already knows rather than earning - not saved, since it is
        /// re-established within a cell or two of travel.
        /// </summary>
        public int RevealsWithoutNewGround { get; set; }

        public ExplorerRobotRuntime(int index, Vector2 homePosition)
        {
            Index = index;
            HomePosition = homePosition;
            Position = homePosition;
            LastRevealPosition = homePosition;
        }

        /// <summary>The unit vector the heading points along.</summary>
        public Vector2 Forward
        {
            get
            {
                float radians = HeadingDegrees * Mathf.Deg2Rad;
                return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            }
        }

        /// <summary>Slides the robot along its own heading. The only thing that moves it while exploring - there is no target to converge on.</summary>
        public void StepForward(float distanceCells) => Position += Forward * distanceCells;

        /// <summary>Slides the robot towards a point, and answers whether it got there this step. What the return leg uses, and the one movement that has a destination.</summary>
        public bool StepTowards(Vector2 target, float distanceCells)
        {
            Vector2 toTarget = target - Position;
            float distance = toTarget.magnitude;

            if (distance <= distanceCells || distance <= 0.0001f)
            {
                Position = target;
                return true;
            }

            Position += toTarget / distance * distanceCells;
            HeadingDegrees = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            return false;
        }
    }
}

using UnityEngine;

namespace Game.Data
{
    /// <summary>
    /// The one settings asset for the pole network (ENERGIE.md) - range and cable sag, alongside the
    /// rest of the project's other tunables (WorldGenerationSettings, ExplorerRobotSettings, ...)
    /// rather than fields on PoleDefinition, since every pole in the game shares one range and one
    /// sag curve.
    /// </summary>
    [CreateAssetMenu(fileName = "PoleNetworkSettings", menuName = "Game/World/Pole Network Settings")]
    public sealed class PoleNetworkSettings : ScriptableObject
    {
        /// <summary>
        /// Half-width of a pole's power field, in cells - 2 gives the spec's 5x5 square (2 cells
        /// either side of the pole's own cell). A Chebyshev (square) distance, not a circle: "rayon
        /// d'action 5x5" is a square footprint, not a disc. What a pole powers a building through,
        /// and what it reads a Core/Powerplant's proximity through to decide whether it is fed -
        /// symmetric, the same "who is next to me" test either way.
        /// </summary>
        [SerializeField, Min(1)] int powerRangeCells = 2;

        /// <summary>
        /// How far apart two poles may stand and still connect - deliberately a second, larger
        /// figure than PowerRangeCells: a pole's wire reaches further than its power field, the same
        /// way it does in the genre this is drawn from. Also a Chebyshev distance.
        /// </summary>
        [SerializeField, Min(1)] int connectionRangeCells = 8;

        /// <summary>World units a cable sags per cell of straight-line distance between its two poles - a short cable is nearly straight, a long one visibly hangs.</summary>
        [SerializeField, Min(0f)] float sagPerCellDistance = 0.035f;

        /// <summary>
        /// Height, in cells above the pole's own footprint centre, a cable attaches at - near the
        /// top of the pole's art (a crossarm, not the ground it stands on), not the footprint
        /// centre PositionCable would otherwise use. A pure presentation number with no gameplay
        /// reach (PoleNetworkSystem's own distance checks are all footprint-to-footprint,
        /// ground-level) - tune it against the shipped sprite's real proportions.
        /// </summary>
        [SerializeField] float cableAttachmentHeightCells = 1.3f;

        public int PowerRangeCells => powerRangeCells;
        public int ConnectionRangeCells => connectionRangeCells;
        public float SagPerCellDistance => sagPerCellDistance;
        public float CableAttachmentHeightCells => cableAttachmentHeightCells;
    }
}

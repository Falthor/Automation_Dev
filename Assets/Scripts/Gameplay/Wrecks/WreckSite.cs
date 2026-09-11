using Game.Core;
using UnityEngine;

namespace Game.Gameplay.Wrecks
{
    /// <summary>
    /// One wreck: where it is, which of the three it is, and whether the player has found it.
    ///
    /// <b>Everything but the last is derived</b>, from the world seed and the pair (ring, rank) -
    /// see <see cref="WreckField"/>. Nothing here is stored except <see cref="Discovered"/>, which
    /// is the usual boundary: what comes from the seed is recomputed, what comes from the player is
    /// saved.
    /// </summary>
    public sealed class WreckSite
    {
        /// <summary>A wreck is three cells across. Fixed rather than per-asset: the three sprites are interchangeable and the site is the same size whichever one it draws.</summary>
        public const int FootprintCells = 3;

        /// <summary>Its position in the field, flat across the rings. What the save records.</summary>
        public readonly int Index;

        /// <summary>Which ring, and which rank within it. The pair everything else is derived from.</summary>
        public readonly int Ring;

        public readonly int RankInRing;

        /// <summary>Which of the three assets. Drawn freely - the same wreck may appear more than once.</summary>
        public readonly int TypeIndex;

        /// <summary>The middle of the 3x3 footprint, in cell space. What distance is measured to.</summary>
        public readonly Vector2 CentreCells;

        /// <summary>The footprint's lowest-left cell - what a view is positioned from.</summary>
        public readonly GridCoord Origin;

        /// <summary>How far from the Core it sits. Kept rather than recomputed: the log reports it, and it is fixed for the life of the world.</summary>
        public readonly float DistanceFromCoreCells;

        /// <summary>Whether the player has found it. The one thing about a wreck that is not derived, and therefore the one thing that is saved.</summary>
        public bool Discovered { get; internal set; }

        public WreckSite(int index, int ring, int rankInRing, int typeIndex, Vector2 centreCells, float distanceFromCoreCells)
        {
            Index = index;
            Ring = ring;
            RankInRing = rankInRing;
            TypeIndex = typeIndex;
            CentreCells = centreCells;
            DistanceFromCoreCells = distanceFromCoreCells;

            // The footprint is odd, so it has a middle cell: the origin is one cell down and left of
            // it. Floored rather than rounded, so the cell a wreck sits on is the cell its centre
            // falls in.
            Origin = new GridCoord(
                Mathf.FloorToInt(centreCells.x) - FootprintCells / 2,
                Mathf.FloorToInt(centreCells.y) - FootprintCells / 2);
        }
    }
}

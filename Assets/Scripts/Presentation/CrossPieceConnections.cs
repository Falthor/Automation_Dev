using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;

namespace Game.Presentation
{
    /// <summary>
    /// Which sides a Splitter or a Crossroad takes items in on and hands them out on, asked of a
    /// facing rather than of a piece.
    ///
    /// <b>The placement ghost is the whole reason this exists.</b> The built view reads the runtime's
    /// own <c>EntryA</c>/<c>ExitA</c>/<c>EntrySide</c> - the very properties transport routes by, so
    /// what is drawn is what happens. A piece still being aimed at a cell has no runtime: it has a
    /// rotation and nothing else.
    ///
    /// <b>So the rule is not restated here.</b> Every line below calls the same static the runtime's
    /// own property calls, which is what makes it impossible for the preview to promise a different
    /// facing than the piece that gets built - and a test compares the two at all four rotations.
    /// </summary>
    public static class CrossPieceConnections
    {
        /// <summary>
        /// Fills <paramref name="into"/> with this piece's sides and returns true; returns false and
        /// empties it for anything that is not a cross piece, which is how the caller tells the two
        /// families apart without naming either type itself.
        ///
        /// A side is given with the direction pointing <b>away</b> from the piece and whether items
        /// travel inward along it - the same pair <see cref="BuildingSpawner"/> draws an arrow from.
        /// </summary>
        public static bool Describe(BuildingDefinition definition, Direction facing, List<(Direction side, bool inward)> into)
        {
            into.Clear();

            if (definition is CrossroadDefinition)
            {
                into.Add((CrossroadRuntime.EntryAAt(facing), true));
                into.Add((CrossroadRuntime.EntryBAt(facing), true));
                into.Add((CrossroadRuntime.ExitAAt(facing), false));
                into.Add((CrossroadRuntime.ExitBAt(facing), false));
                return true;
            }

            if (definition is SplitterDefinition)
            {
                into.Add((SplitterRuntime.EntrySideAt(facing), true));

                foreach (Direction exit in SplitterRuntime.CandidateExits(SplitterRuntime.EntrySideAt(facing)))
                {
                    into.Add((exit, false));
                }

                return true;
            }

            return false;
        }
    }
}

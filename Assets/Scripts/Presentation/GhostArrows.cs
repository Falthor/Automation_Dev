using System.Collections.Generic;
using Game.Core;
using Game.Data;
using Game.Gameplay.Buildings;

namespace Game.Presentation
{
    /// <summary>
    /// Which cells the placement ghost marks with an arrow, and which way each points, for the
    /// building currently being aimed.
    ///
    /// <b>Apart from the input adapter so it can be held to a test.</b> The adapter is a
    /// MonoBehaviour that needs a camera, a grid and a mouse before it will answer anything, and
    /// this rule needs none of the three: it is a function of the definition, the cell and the
    /// rotation. What is left in the adapter is turning each marked cell into a world position.
    ///
    /// Grid-free on purpose - it answers in cells, and the caller converts through
    /// <see cref="BuildingSpawner.ArrowPosition"/>, which is the same inset the built view uses.
    /// </summary>
    public static class GhostArrows
    {
        /// <summary>
        /// Fills <paramref name="into"/> with one entry per arrow: the cell it marks, the side it
        /// marks (pointing away from the building), and whether items travel inward along it.
        ///
        /// <paramref name="crossPieceScratch"/> is a caller-owned buffer, so a ghost refreshed every
        /// frame allocates nothing.
        /// </summary>
        public static void For(BuildingDefinition definition, GridCoord cell, Direction rotation, Direction inputSide,
            List<(Direction side, bool inward)> crossPieceScratch,
            List<(GridCoord cell, Direction side, bool inward)> into)
        {
            into.Clear();

            // A Splitter or a Crossroad first, because neither can be described by the two flags
            // below: one has a single entry and three exits, the other two of each, while the flags
            // say "one exit" and "every side but the exit".
            if (CrossPieceConnections.Describe(definition, rotation, crossPieceScratch))
            {
                foreach ((Direction side, bool inward) in crossPieceScratch)
                {
                    into.Add((CrossFootprint.NeighborCell(cell, side), side, inward));
                }

                return;
            }

            // Output and entry arrows are independent: a building can take deliveries without
            // producing anything physical (DataCenter), so each side is answered on its own.
            if (definition.HasOutputArrow)
            {
                // The building's own rule for which cell of its output edge carries the arrow, not
                // the first one: they differ on every even-width edge, so a 2x2 Foundry previewed
                // its arrow one cell away from where it grew it.
                into.Add((BuildingRuntime.ComputeOutputCell(cell, definition.FootprintSize, rotation), rotation, false));
            }

            // One arrow for a single-input building, on the side T has landed on - the ghost shows
            // the one face the building will actually take from, rather than three it refuses two of.
            if (definition.HasSingleInputArrow)
            {
                (GridCoord inputCell, Direction side) = BuildingRuntime.ComputeSingleInputCell(cell, definition.FootprintSize, inputSide);
                into.Add((inputCell, side, true));
            }
            else if (definition.HasInputArrows)
            {
                foreach ((GridCoord edgeCell, Direction fromMySide) in BuildingRuntime.ComputeInputCells(cell, definition.FootprintSize, rotation))
                {
                    into.Add((edgeCell, fromMySide, true));
                }
            }
        }
    }
}

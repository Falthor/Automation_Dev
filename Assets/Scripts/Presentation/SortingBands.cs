using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The one draw-order ladder for the whole world view. Every sortingOrder in Game.Presentation
    /// comes from here, and nowhere else writes a literal.
    ///
    /// <b>The rule.</b> An element lower on the grid draws in front of one above it, because its art
    /// may overhang the cell above. Four bands, in this order:
    ///
    /// <list type="number">
    /// <item><b>Ground</b> - fixed orders. Nothing here has height and nothing overhangs, so nothing
    /// needs depth: terrain, nano coverage, flat decor and vegetation, concrete, deposits, the grid,
    /// the action radius, and the transport family with the items riding it.</item>
    /// <item><b>Sorted</b> - a single band ordered by depth: every building, the Core, construction
    /// silhouettes, their shadows and arrows, the raised rocks. Anything whose art rises above its
    /// base and still stands on it.</item>
    /// <item><b>Flying</b> - fixed, above everything sorted: the builder drones, and later anything
    /// else genuinely airborne. They were tried in the sorted band and it put a drone behind the very
    /// site it was delivering to, swallowed by the nano assembly - a thing in the air does not queue
    /// for a place on the ground. Its shadow keeps the ground's logic and falls on whatever is
    /// underneath it.</item>
    /// <item><b>Information</b> - fixed, above the world: placement previews, their arrows and the
    /// hover outline. Not "flying": what flies is in the world and has a depth, while an overlay
    /// annotates it. A preview hidden behind a building would be a preview that failed at its job.
    /// Permanent marks stay in the sorted band with the thing they belong to - a building's own
    /// input/output arrows are world decoration, not an answer to a gesture.</item>
    /// </list>
    ///
    /// Fog sits above all four.
    /// </summary>
    public static class SortingBands
    {
        // ---- Ground band ----

        public const int TerrainBase = 0;
        public const int TerrainTop = 1;
        public const int GroundCoverage = 2;

        /// <summary>Painted-on ground marks (mud, sand) - under everything laid on top of them.</summary>
        public const int FlatDecor = 3;

        /// <summary>Flowers, bushes, dead wood, pebbles: vegetation whose art has no rising silhouette. The raised rocks are in the sorted band instead - see WildDecorationGenerator.</summary>
        public const int FlatVegetation = 4;

        /// <summary>Above the vegetation on purpose: concrete poured over a flower has to cover it.</summary>
        public const int GroundSlab = 5;

        public const int DepositGlow = 6;

        /// <summary>Ore deposits are a scatter of small chunks lying on the ground, not a mound - flat, so an Extractor's construction silhouette is never hidden behind the deposit it stands on.</summary>
        public const int Deposit = 7;

        public const int GridLines = 8;
        public const int ActionRadius = 9;

        public const int Conveyor = 10;

        /// <summary>Added on odd cells so two overscanned belts never share an order at their seam - see ConveyorView.</summary>
        public const int ConveyorSeamParity = 1;

        /// <summary>Above the belts carrying them, below every building: an item passing behind a factory is hidden by it.</summary>
        public const int TransportedItem = 12;

        /// <summary>
        /// A Splitter/Crossroad's RenderOverscan deliberately makes its arms overlap the neighbouring
        /// belt's sprite bounds at the seam, to close the visual gap. On an equal order Unity breaks
        /// the tie by instantiation order - unstable across placements, so the overlapping edge would
        /// randomly land in front of or behind the belt. A strictly higher order makes the cross
        /// always win there, which is what the overscan was for.
        ///
        /// Above TransportedItem for the same reason: an item riding right up to the shared edge sits
        /// inside that same overlap, and an equal order made it flicker in and out as the tie-break
        /// flipped. Cross always winning covers it cleanly instead.
        /// </summary>
        public const int CrossPiece = 13;

        // ---- Sorted band ----

        /// <summary>
        /// Quantisation of the depth key, per world unit. At the scene's cell size of 1 that is four
        /// sub-steps per cell, which separates anything the player can place while keeping the band
        /// small enough to sit inside a short several times over.
        /// </summary>
        public const int StepsPerWorldUnit = 4;

        /// <summary>World rows the band can address. The world is 60 cells today (TerrainGenerationSettings.size); this leaves room for one an order of magnitude larger.</summary>
        public const int AddressableRows = 512;

        const int Steps = AddressableRows * StepsPerWorldUnit;

        public const int SubLayers = 4;

        public const int SubSilhouette = 0;
        public const int SubShadow = 1;
        public const int SubSprite = 2;

        /// <summary>A building's own input/output arrows - drawn over the building they belong to, never under it.</summary>
        public const int SubOverlay = 3;

        public const int SortedFirst = 100;
        public const int SortedLast = SortedFirst + Steps * SubLayers - 1;

        // ---- Flying band ----

        /// <summary>
        /// A flier's shadow. Above everything standing on the ground on purpose: a shadow lands on
        /// whatever is underneath it, roofs included, which is exactly what says the thing casting it
        /// is in the air. One slot below the flier itself, which DropShadow relies on - it takes its
        /// caster's order minus the gap between SubSprite and SubShadow.
        /// </summary>
        public const int FlyingShadow = SortedLast + 1;

        /// <summary>The builder drones, and later anything else genuinely airborne. Fixed, above every depth-sorted thing: a drone flies over the base rather than queueing for a place in it.</summary>
        public const int FlyingFirst = FlyingShadow + 1;

        // ---- Information band ----

        public const int PlacementPreview = FlyingFirst + 100;
        public const int PlacementPreviewArrow = PlacementPreview + 1;
        public const int HoverOutline = PlacementPreview + 2;

        public const int Fog = PlacementPreview + 100;

        /// <summary>
        /// The depth rank of something standing at <paramref name="worldBottomY"/> - the world Y of
        /// the <b>lowest</b> point it stands on, never the centre of its art. For a building that is
        /// the bottom edge of its footprint (GridRuntime.CellToWorld, which returns the corner); for
        /// a scattered rock, the bottom edge of its sprite; for a robot, its own position.
        ///
        /// Lower on the grid gives a higher order, so it draws in front. Stated as a world
        /// coordinate rather than a row index so grid-aligned buildings and free-standing decor go
        /// through the same function - the one thing that keeps a baked scene and the runtime from
        /// drifting apart.
        /// </summary>
        public static int Sorted(float worldBottomY, int subLayer)
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(worldBottomY * StepsPerWorldUnit), 0, Steps - 1);
            int sub = Mathf.Clamp(subLayer, 0, SubLayers - 1);
            return SortedFirst + (Steps - 1 - step) * SubLayers + sub;
        }

        /// <summary>The rank of a renderer's own art, measured off the bottom of what it actually draws - what free-standing decor uses, since it owns no footprint.</summary>
        public static int SortedFromBounds(Renderer renderer, int subLayer)
            => Sorted(renderer.bounds.min.y, subLayer);
    }
}

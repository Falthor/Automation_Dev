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
    ///
    /// <b>The sorted band is measured against the camera, not the world.</b> Its ranks are handed
    /// out by <see cref="DepthSortLadder"/> against a window that follows the view; the constants
    /// here are the ladder's geometry, not a map of the world. A consequence worth stating once: a
    /// sorted-band rank can never be written into a scene, because it is only true for the window it
    /// was computed against. Baked decor that rises above its base has to be ranked at runtime.
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
        /// sub-steps per cell, which separates anything the player can place.
        /// </summary>
        public const int StepsPerWorldUnit = 4;

        /// <summary>
        /// World rows the band addresses - <b>of the view window, not of the world</b>.
        ///
        /// This used to be the world's own height, and that is what stops working: sortingOrder is a
        /// short, and a 10 000-cell map at 4 steps and 4 sub-layers would need 160 000 values. It
        /// would not fail loudly either - <see cref="SortedFromDepth"/> clamps, so everything past
        /// the last addressable row collapses onto one order and quietly stops sorting by depth.
        ///
        /// The way out is that <b>only what is on screen at the same time has to be ordered</b>, and
        /// the zoom-out cap bounds that. The ladder is therefore anchored to a window that follows
        /// the camera (<see cref="DepthSortLadder"/>), and its size no longer has anything to do
        /// with the size of the map: a 300-cell world and a 10 000-cell one cost the same 4 096
        /// orders.
        ///
        /// 256 rows against a 60-row view leaves ~98 world units of slack on each side, which is how
        /// far the camera pans between two re-anchorings. Raising it buys rarer re-anchorings and
        /// costs orders; the whole ladder must stay inside a short, which a test asserts.
        /// </summary>
        public const int AddressableRows = 256;

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
        /// The depth rank of something standing <paramref name="depthBelowWindowTop"/> world units
        /// below the top of the current view window - see <see cref="DepthSortLadder"/>, which owns
        /// that window and is the only thing that should be calling this.
        ///
        /// The measurement is taken from the <b>lowest</b> point the thing stands on, never the
        /// centre of its art: the bottom edge of a building's footprint, the bottom of a scattered
        /// rock's sprite. Deeper means further down the screen, means drawn in front.
        ///
        /// <b>Relative, not absolute.</b> The rank of one object is meaningless on its own now; only
        /// the comparison between two objects measured against the same window means anything. That
        /// is also why a rank can no longer be baked into a scene - it would be a number frozen
        /// against a window that has since moved. Nothing outside the window is ordered correctly
        /// either, and nothing outside it is visible.
        /// </summary>
        public static int SortedFromDepth(float depthBelowWindowTop, int subLayer)
        {
            int step = Mathf.Clamp(Mathf.FloorToInt(depthBelowWindowTop * StepsPerWorldUnit), 0, Steps - 1);
            int sub = Mathf.Clamp(subLayer, 0, SubLayers - 1);
            return SortedFirst + step * SubLayers + sub;
        }
    }
}

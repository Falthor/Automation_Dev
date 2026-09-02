using UnityEngine;

namespace Game.Data
{
    /// <summary>Static, immutable definition of a buildable building. Not runtime state.</summary>
    public abstract class BuildingDefinition : ScriptableObject
    {
        [SerializeField] string id;
        [SerializeField] string displayName;
        [SerializeField] Vector2Int footprintSize = new Vector2Int(1, 1);
        [SerializeField] Color placeholderColor = Color.white;
        [SerializeField] ResearchDefinition unlockResearch;
        [SerializeField] Sprite sprite;
        [SerializeField] Sprite[] animationFrames = System.Array.Empty<Sprite>();
        [SerializeField] float animationFps = 8f;
        [SerializeField] RecipeIngredient[] cost = System.Array.Empty<RecipeIngredient>();

        public string Id => id;
        public string DisplayName => displayName;
        public Vector2Int FootprintSize => footprintSize;
        public Color PlaceholderColor => placeholderColor;

        /// <summary>Real art for this building, or null to fall back to a procedural placeholder colored by PlaceholderColor.</summary>
        public Sprite Sprite => sprite;

        /// <summary>
        /// Optional flipbook frames (e.g. a pulsing core) played in a loop over Sprite while
        /// placed. Empty/single-element means the building is static and just shows Sprite.
        /// </summary>
        public Sprite[] AnimationFrames => animationFrames;

        /// <summary>Playback speed for AnimationFrames, in frames per second.</summary>
        public float AnimationFps => animationFps;

        /// <summary>
        /// Uniform overscan applied on top of the footprint-fitted size, e.g. so a rotatable
        /// cross-shaped building's arms visually reach into a neighboring conveyor instead of
        /// leaving a hairline gap at the seam (matches ConveyorView's own LengthStretchFactor).
        /// 1 by default (exact footprint fit, no overscan).
        /// </summary>
        public virtual float RenderOverscan => 1f;

        /// <summary>Research required before this building type may be placed at all. Null means buildable from the start (CONTRACTS.md §11).</summary>
        public ResearchDefinition UnlockResearch => unlockResearch;

        /// <summary>Items required (from Core + every Storage) to place one of this building. Empty means free.</summary>
        public RecipeIngredient[] Cost => cost;

        /// <summary>
        /// Configured Power draw (kW), for the Building menu's consumption preview. 0 by default;
        /// overridden by every building type with a fixed continuous draw. DataCenter has none -
        /// its demand depends entirely on installed components, not a base value - so it keeps 0.
        /// </summary>
        public virtual float PowerDemandKw => 0f;

        /// <summary>Configured Compute draw (CU/s), for the Building menu's consumption preview. 0 by default.</summary>
        public virtual float CuDemand => 0f;

        /// <summary>
        /// Every cell (relative to the placement origin) this building actually occupies. A full
        /// FootprintSize rectangle by default - only a non-rectangular building (Splitter's "+"
        /// shape, whose 3x3 bounding box has 4 free corners) overrides this. Grid occupancy,
        /// demolition and the action-radius check all go through this rather than FootprintSize
        /// directly, so they automatically support a masked shape with no per-caller special case.
        /// </summary>
        public virtual Vector2Int[] FootprintCells => RectangleCells(FootprintSize);

        protected static Vector2Int[] RectangleCells(Vector2Int size)
        {
            var cells = new Vector2Int[size.x * size.y];
            int i = 0;
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    cells[i++] = new Vector2Int(x, y);
                }
            }
            return cells;
        }

        /// <summary>
        /// Center + one arm per cardinal side inside a 3x3 bounding box, corners free - the
        /// footprint shared by Splitter and Crossroad. See CrossFootprint (Game.Gameplay) for the
        /// matching absolute-cell math used by their runtimes.
        /// </summary>
        protected static readonly Vector2Int[] CrossShapeCells =
        {
            new Vector2Int(1, 0), // south arm
            new Vector2Int(0, 1), // west arm
            new Vector2Int(1, 1), // center
            new Vector2Int(2, 1), // east arm
            new Vector2Int(1, 2), // north arm
        };

        /// <summary>
        /// Whether this building has a single fixed output side (drawn as an arrow, both on the
        /// construction ghost and the built view). False by default - most buildings have no
        /// directional output (e.g. Storage accepts input from any side and has none).
        /// </summary>
        public virtual bool HasOutputArrow => false;

        /// <summary>
        /// Whether every non-output side should be drawn with an inward-pointing entry arrow
        /// (construction ghost and built view alike) - only meaningful alongside HasOutputArrow,
        /// since "every side but the output side" needs a fixed output side to be defined
        /// against. False by default; the recipe-based production buildings (Foundry/Factory/
        /// AdvancedFoundry/Assembler) override it - Extractor keeps the default since it accepts
        /// no input at all.
        /// </summary>
        public virtual bool HasInputArrows => false;
    }
}

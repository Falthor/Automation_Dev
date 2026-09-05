using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The single source of appearance for the nano-materialisation layers. Nothing here is
    /// per-building on purpose: the whole base must be retuned by editing one asset, so a building
    /// never carries its own copy of any of these values.
    ///
    /// A definition asset, not runtime state (DEVELOPMENT_RULES.md §1): authored in the inspector
    /// and only ever read, hence private fields with getters. That also keeps it safe under this
    /// project's disabled Domain Reload, where a runtime write to an asset would survive into the
    /// next Play session.
    ///
    /// Values come from a browser prototype rather than from Unity - see
    /// docs/materialisation-nano.md for where each one comes from and how to re-derive it.
    /// </summary>
    [CreateAssetMenu(fileName = "NanoConstructionSettings", menuName = "Game/Presentation/Nano Construction Settings")]
    public sealed class NanoConstructionSettings : ScriptableObject
    {
        [Header("Dissolve")]

        /// <summary>
        /// Noise periods per world unit - one number for every building, never a per-building
        /// value. Because the noise is sampled in world coordinates, a bigger building simply
        /// receives more periods across its width at an identical physical grain size, which is
        /// the intended behaviour.
        /// </summary>
        [SerializeField, Min(0f)] float noiseScale = 12f;

        /// <summary>How much the noise perturbs the reveal front. 0 is a clean sweep, 1 is pure noise.</summary>
        [SerializeField, Range(0f, 1f)] float noiseWeight = 0.045f;

        /// <summary>Width of the glowing edge trailing the reveal front, in progress units.</summary>
        [SerializeField, Range(0f, 1f)] float rimWidth = 0.059f;

        /// <summary>Colour of the building's own rim.</summary>
        [SerializeField] Color rimColor = new Color(0.2353f, 0.7255f, 0.9216f, 1f);

        /// <summary>0 = bottom to top, 1 = radial.</summary>
        [SerializeField, Range(0, 1)] int revealMode = 0;

        [Header("Site silhouette")]

        /// <summary>
        /// Opacity of the blue silhouette once construction has actually started. Constant for the
        /// whole build rather than fading with progress: a half-erased outline under a half-formed
        /// building reads as mush, and the clean cut at 1 is what makes completion legible.
        /// A value posed by reasoning, not yet judged on screen - expect to move it.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float sitePlaceholderAlpha = 0.35f;

        /// <summary>
        /// Sorting order of the silhouette. It must sit under both the shadow and the sprite it
        /// belongs to, which fixes the whole stack for a building under construction:
        /// ground slab 5, action radius 6, <b>silhouette 7</b>, drop shadow 8, building sprite 10.
        /// </summary>
        [SerializeField] int siteSilhouetteSortingOrder = 7;

        [Header("Displayed progress (see the spec's section 3)")]

        /// <summary>
        /// Footprint cells assembled per second - a speed, not a duration, so a big building takes
        /// proportionally longer than a small one. Progress per second is this divided by the
        /// building's own footprint area, which is why a 9-cell power plant and a 1-cell conveyor
        /// no longer take the same time.
        ///
        /// 1.8 reproduces the value tuned by eye on the gas power plant (0.2 progress/s over 9
        /// cells), so that building's behaviour is unchanged; a conveyor assembles in 0.56 s.
        /// </summary>
        [SerializeField, Min(0.0001f)] float assemblyRate = 1.8f;

        /// <summary>
        /// Floor on how fast any building may assemble, so a small one cannot pop into existence in
        /// a single frame. Caps the derived rate at 1/duration. Inert at the shipped assemblyRate -
        /// a 1-cell building already takes 0.56 s - and there as a guard for future retuning.
        /// </summary>
        [SerializeField, Min(0.0001f)] float minAssemblyDuration = 0.25f;

        /// <summary>Seconds the rim flash lasts after a delivery.</summary>
        [SerializeField, Min(0f)] float deliveryFlashDuration = 0.40f;

        /// <summary>Peak value pushed into the shader's _RimBoost when a delivery lands.</summary>
        [SerializeField, Min(0f)] float deliveryFlashIntensity = 0.28f;

        [Header("Ground coverage")]

        /// <summary>Rim colour of the ground layer, a duller variant of rimColor.</summary>
        [SerializeField] Color groundRimColor = new Color(0.1176f, 0.5490f, 0.7255f, 1f);

        /// <summary>Tint strength of converted ground, deliberately discreet.</summary>
        [SerializeField, Range(0f, 1f)] float groundIntensity = 0.15f;

        /// <summary>Deliberately decoupled from groundIntensity: the ground stays subtle while its lit boundary stays readable. Coupling them would make tuning one switch the other off.</summary>
        [SerializeField, Range(0f, 1f)] float groundRimIntensity = 0.6f;

        /// <summary>Seconds a cell with no site takes to fade back to zero coverage.</summary>
        [SerializeField, Min(0f)] float coverageFadeSeconds = 4f;

        /// <summary>
        /// How far the shader looks around each pixel to find the boundary, in cells. Widens the lit
        /// band; it does not move it. Measured in cells rather than world units so the band keeps
        /// the same physical width whatever the cell size.
        /// </summary>
        [SerializeField, Range(0.1f, 3f)] float groundRimWidth = 1f;

        /// <summary>
        /// Between the terrain (0 and 1) and the concrete slab (5). 2 and 4 are left free on either
        /// side deliberately - the whole ladder is contiguous integers with existing collisions, and
        /// renumbering it is a separate task, so this only takes a free value rather than making
        /// room.
        /// </summary>
        [SerializeField] int groundCoverageSortingOrder = 3;

        [Header("Shader")]

        /// <summary>
        /// Custom/BuildDissolve, referenced as an asset rather than resolved by Shader.Find. A
        /// shader only reached by name is stripped from a player build unless it is also listed in
        /// Always Included Shaders - see docs/BUILD.md. An asset reference cannot be stripped, so
        /// this dependency cannot silently break a build.
        /// </summary>
        [SerializeField] Shader dissolveShader;

        /// <summary>Custom/GroundCoverage, referenced as an asset for the same reason as dissolveShader.</summary>
        [SerializeField] Shader coverageShader;

        public float NoiseScale => noiseScale;
        public float NoiseWeight => noiseWeight;
        public float RimWidth => rimWidth;
        public Color RimColor => rimColor;
        public int RevealMode => revealMode;

        public float SitePlaceholderAlpha => sitePlaceholderAlpha;
        public int SiteSilhouetteSortingOrder => siteSilhouetteSortingOrder;

        public float AssemblyRate => assemblyRate;
        public float MinAssemblyDuration => minAssemblyDuration;

        /// <summary>
        /// Progress units per second for a building covering <paramref name="footprintCells"/> grid
        /// cells. The area is the building's <b>logical footprint</b>, never the visual AABB the
        /// shader's _BuildBounds carries: a sprite may deliberately overflow its footprint, and
        /// sizing the assembly speed off what is drawn would make an overhanging roof slow the
        /// building down.
        /// </summary>
        public float ProgressRateFor(int footprintCells)
        {
            float cells = Mathf.Max(1, footprintCells);
            return Mathf.Min(assemblyRate / cells, 1f / minAssemblyDuration);
        }
        public float DeliveryFlashDuration => deliveryFlashDuration;
        public float DeliveryFlashIntensity => deliveryFlashIntensity;

        public Color GroundRimColor => groundRimColor;
        public float GroundIntensity => groundIntensity;
        public float GroundRimIntensity => groundRimIntensity;
        public float CoverageFadeSeconds => coverageFadeSeconds;
        public float GroundRimWidth => groundRimWidth;
        public int GroundCoverageSortingOrder => groundCoverageSortingOrder;

        public Shader DissolveShader => dissolveShader;
        public Shader CoverageShader => coverageShader;
    }
}

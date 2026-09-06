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

        [Header("Displayed progress (see the spec's section 3)")]

        // How fast a building assembles is NOT here. It decides when that building starts working -
        // a segment is not operational until its assembly reaches 1 - so it belongs to the
        // simulation, in Game.Gameplay.Sites.SegmentAssembly, and this asset keeps only the look.

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

        /// <summary>Seconds a site with no chantier takes to walk its conversion front back to nothing.</summary>
        [SerializeField, Min(0f)] float coverageFadeSeconds = 4f;

        /// <summary>
        /// Fraction of a building's displayed progress at which its ground has finished converting.
        /// Below 1 the ground runs <b>ahead</b> of the building, which is the only way to see it: the
        /// sprite covers its own footprint, so a ground on the same clock is hidden for the whole
        /// build and only ever peeks out as a halo at the very end. At 0.5 the ground is done by the
        /// time the building is half materialised, and the second half rises on finished ground.
        /// </summary>
        [SerializeField, Range(0.05f, 1f)] float groundLeadShare = 0.5f;

        /// <summary>
        /// How far past the footprint's outline the conversion reaches once the ground's own phase
        /// is over, in cells. This is what stops the finished patch from being a square: the outer
        /// boundary is decided by the threshold and the noise inside this ring, not by the edge of
        /// the footprint. It also narrows the gap with a building whose art already overhangs its
        /// own cells. Measured from the footprint's <b>corner</b>, which is the last point of the
        /// footprint the front reaches.
        /// </summary>
        [SerializeField, Range(0.05f, 3f)] float groundOverflowCells = 0.45f;

        // The ground deliberately has no grain settings of its own. It is handed the dissolve's
        // noiseScale and noiseWeight above, because the two fronts are meant to be ragged the same
        // way rather than merely both being ragged - two settings here would be two things that
        // drift apart, and the layers would go back to reading as separate effects.

        /// <summary>
        /// Resolution of the coverage field, in texels per grid cell. It carries the <b>smooth</b>
        /// threshold only - the grain is added per fragment by the shader, so this no longer limits
        /// how fine the front's teeth can be, only how faithfully the underlying sweep and the spill
        /// boundary are sampled. One texel per cell makes that boundary follow the grid.
        /// Changing it reallocates every zone's texture, which is why it is a tuning knob and not
        /// something read per frame.
        /// </summary>
        [SerializeField, Range(1, 8)] int groundTexelsPerCell = 4;

        /// <summary>
        /// Width of the lit band behind the conversion front, in threshold units - the same unit and
        /// the same meaning as the dissolve's own rimWidth, so both layers light their boundary the
        /// same way. Widens the band; it does not move it.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float groundRimWidth = 0.08f;

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

        public float DeliveryFlashDuration => deliveryFlashDuration;
        public float DeliveryFlashIntensity => deliveryFlashIntensity;

        public Color GroundRimColor => groundRimColor;
        public float GroundIntensity => groundIntensity;
        public float GroundRimIntensity => groundRimIntensity;
        public float CoverageFadeSeconds => coverageFadeSeconds;
        public float GroundLeadShare => groundLeadShare;
        public float GroundOverflowCells => groundOverflowCells;

        /// <summary>
        /// Where the ground's own conversion stands when a building's dissolve stands at
        /// <paramref name="displayedProgress"/>. Reaches 1 - a completely converted footprint -
        /// at groundLeadShare of the build, and stays there for the rest of it.
        /// </summary>
        public float GroundProgressFor(float displayedProgress)
            => Mathf.Clamp01(displayedProgress / Mathf.Max(groundLeadShare, 0.0001f));
        public int GroundTexelsPerCell => groundTexelsPerCell;
        public float GroundRimWidth => groundRimWidth;

        public Shader DissolveShader => dissolveShader;
        public Shader CoverageShader => coverageShader;
    }
}

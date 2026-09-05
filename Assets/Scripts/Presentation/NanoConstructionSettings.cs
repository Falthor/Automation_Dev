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
        [SerializeField, Min(0f)] float noiseScale = 6.3f;

        /// <summary>How much the noise perturbs the reveal front. 0 is a clean sweep, 1 is pure noise.</summary>
        [SerializeField, Range(0f, 1f)] float noiseWeight = 0.30f;

        /// <summary>Width of the glowing edge trailing the reveal front, in progress units.</summary>
        [SerializeField, Range(0f, 1f)] float rimWidth = 0.09f;

        /// <summary>Colour of the building's own rim.</summary>
        [SerializeField] Color rimColor = new Color(0.2353f, 0.7255f, 0.9216f, 1f);

        /// <summary>0 = bottom to top, 1 = radial.</summary>
        [SerializeField, Range(0, 1)] int revealMode = 0;

        [Header("Displayed progress (see the spec's section 3)")]

        /// <summary>
        /// Progress units per second that the displayed value is allowed to gain on the real one.
        /// This is what turns a delivery of ten components into a longer assembly than a delivery
        /// of two, and what guarantees a minimum materialisation time even when everything lands
        /// at once.
        /// </summary>
        [SerializeField, Min(0.0001f)] float catchUpRate = 0.25f;

        /// <summary>Seconds the rim flash lasts after a delivery.</summary>
        [SerializeField, Min(0f)] float deliveryFlashDuration = 0.40f;

        /// <summary>Peak value pushed into the shader's _RimBoost when a delivery lands.</summary>
        [SerializeField, Min(0f)] float deliveryFlashIntensity = 0.28f;

        [Header("Ground coverage (step 2 - not read yet)")]

        /// <summary>Rim colour of the ground layer, a duller variant of rimColor.</summary>
        [SerializeField] Color groundRimColor = new Color(0.1176f, 0.5490f, 0.7255f, 1f);

        /// <summary>Tint strength of converted ground, deliberately discreet.</summary>
        [SerializeField, Range(0f, 1f)] float groundIntensity = 0.15f;

        /// <summary>Deliberately decoupled from groundIntensity: the ground stays subtle while its lit boundary stays readable. Coupling them would make tuning one switch the other off.</summary>
        [SerializeField, Range(0f, 1f)] float groundRimIntensity = 0.6f;

        /// <summary>Seconds a cell with no site takes to fade back to zero coverage.</summary>
        [SerializeField, Min(0f)] float coverageFadeSeconds = 4f;

        [Header("Shader")]

        /// <summary>
        /// Custom/BuildDissolve, referenced as an asset rather than resolved by Shader.Find. A
        /// shader only reached by name is stripped from a player build unless it is also listed in
        /// Always Included Shaders - see docs/BUILD.md. An asset reference cannot be stripped, so
        /// this dependency cannot silently break a build.
        /// </summary>
        [SerializeField] Shader dissolveShader;

        public float NoiseScale => noiseScale;
        public float NoiseWeight => noiseWeight;
        public float RimWidth => rimWidth;
        public Color RimColor => rimColor;
        public int RevealMode => revealMode;

        public float CatchUpRate => catchUpRate;
        public float DeliveryFlashDuration => deliveryFlashDuration;
        public float DeliveryFlashIntensity => deliveryFlashIntensity;

        public Color GroundRimColor => groundRimColor;
        public float GroundIntensity => groundIntensity;
        public float GroundRimIntensity => groundRimIntensity;
        public float CoverageFadeSeconds => coverageFadeSeconds;

        public Shader DissolveShader => dissolveShader;
    }
}

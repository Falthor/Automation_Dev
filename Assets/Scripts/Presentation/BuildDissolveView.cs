using Game.Gameplay.Sites;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Materialises a building's sprite as its construction site receives material: the sprite is
    /// clipped away below a noisy reveal front that advances with progress, with a lit rim trailing
    /// the front and a brief flash each time a delivery lands.
    ///
    /// Read-only over gameplay. It reads a ConstructionSiteRuntime's delivered/total counters and
    /// writes nothing back - the whole feature lives in Game.Presentation and construction is
    /// unaware of it.
    ///
    /// The displayed progress is deliberately NOT the site's progress. Material arrives in lots, so
    /// the real value jumps in steps (0 to 0.67 in one frame is normal); driving the shader with it
    /// would make the building snap into existence. DisplayedProgress chases the real value at a
    /// bounded rate instead, never overtaking it, which makes materialisation last in proportion to
    /// how much matter actually arrived and guarantees a minimum duration even when everything
    /// lands at once. The steps are not hidden but announced, by the delivery flash.
    ///
    /// That rate is a speed in cells per second, not in progress per second, so it is divided by
    /// the building's own footprint - a 9-cell power plant takes nine times as long to assemble as
    /// a 1-cell conveyor rather than exactly as long. See NanoConstructionSettings.ProgressRateFor.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class BuildDissolveView : MonoBehaviour
    {
        static readonly int ProgressId = Shader.PropertyToID("_Progress");
        static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        static readonly int NoiseWeightId = Shader.PropertyToID("_NoiseWeight");
        static readonly int RimWidthId = Shader.PropertyToID("_RimWidth");
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int RimBoostId = Shader.PropertyToID("_RimBoost");
        static readonly int RevealModeId = Shader.PropertyToID("_RevealMode");
        static readonly int BuildBoundsId = Shader.PropertyToID("_BuildBounds");

        [SerializeField] NanoConstructionSettings settings;

        /// <summary>
        /// Serialized rather than a plain auto-property so it shows up in the inspector: with no
        /// site bound, dragging this by hand during play is how the effect gets judged by eye
        /// (directive-materialisation-nano.md §11, step 1). A bound site overwrites it every tick.
        /// </summary>
        [SerializeField, Range(0f, 1f)] float targetProgress;

        /// <summary>
        /// The building's logical footprint area, in grid cells - what sets the assembly speed.
        /// Defaults to 9, the gas power plant this effect was tuned on, so a hand-driven test
        /// object behaves like that reference building. ConstructionSiteVisualSync overwrites it
        /// from BuildingDefinition.FootprintSize for a real segment.
        /// </summary>
        [SerializeField, Min(1)] int footprintCells = 9;

        SpriteRenderer _renderer;
        MaterialPropertyBlock _propertyBlock;
        Material _dissolveMaterial;
        Material _originalMaterial;
        SpriteFlipbook _flipbook;
        ConstructionSiteRuntime _site;
        float _lastTargetProgress;

        /// <summary>The site's real progress: discrete, jumping at each delivery. Set directly when driving the effect by hand (a test prefab); otherwise fed by the bound site.</summary>
        public float TargetProgress
        {
            get => targetProgress;
            set => targetProgress = Mathf.Clamp01(value);
        }

        /// <summary>What actually drives the shader. Chases TargetProgress at the configured rate and never exceeds it.</summary>
        public float DisplayedProgress { get; private set; }

        /// <summary>Seconds left on the current delivery flash, 0 when none is running.</summary>
        public float FlashRemaining { get; private set; }

        /// <summary>True once the sprite is fully materialised. The component removes itself on the same tick.</summary>
        public bool IsComplete => DisplayedProgress >= 1f;

        public NanoConstructionSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        /// <summary>The building's logical footprint area in cells, which is what its assembly speed is derived from. Clamped to at least 1.</summary>
        public int FootprintCells
        {
            get => footprintCells;
            set => footprintCells = Mathf.Max(1, value);
        }

        /// <summary>Progress units per second this particular building assembles at, given its footprint. 0 when no settings are bound.</summary>
        public float ProgressRate => settings != null ? settings.ProgressRateFor(footprintCells) : 0f;

        /// <summary>Reads TargetProgress from this site from now on. Read-only on the site; pass null to drive TargetProgress by hand instead.</summary>
        public void Bind(ConstructionSiteRuntime site) => _site = site;

        /// <summary>
        /// Fraction of a site's total cost already delivered. A site that costs nothing is complete
        /// on the spot rather than dividing by zero.
        /// </summary>
        public static float ProgressOf(ConstructionSiteRuntime site)
        {
            if (site == null) return 0f;

            int total = 0;
            foreach (var entry in site.TotalCost) total += entry.Value;
            if (total <= 0) return 1f;

            int delivered = 0;
            foreach (var entry in site.Delivered) delivered += entry.Value;

            return Mathf.Clamp01((float)delivered / total);
        }

        void Awake() => Initialize();

        /// <summary>
        /// Gated on the objects themselves rather than on a "done" flag. A script recompile
        /// reloads the domain, and Unity restores a MonoBehaviour's serializable private fields
        /// (a bool among them) while everything else - a MaterialPropertyBlock, a Material - comes
        /// back null. A flag would therefore say "initialised" over a null property block, which
        /// throws on the next tick. Checking each object means the component simply heals itself.
        /// </summary>
        void Initialize()
        {
            if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
            if (_flipbook == null) _flipbook = GetComponent<SpriteFlipbook>();

            // Frozen on its current frame while under construction; the machine only starts moving
            // once it is operational.
            if (_flipbook != null && _flipbook.enabled) _flipbook.enabled = false;

            if (settings == null || settings.DissolveShader == null) return;
            if (_dissolveMaterial != null) return;

            // Already wearing the dissolve material means a reload lost the reference rather than
            // the material never having been installed - taking the branch again would stack a
            // second instance and record the dissolve material as the one to restore.
            Material current = _renderer.sharedMaterial;
            if (current != null && current.shader == settings.DissolveShader) return;

            _originalMaterial = current;
            _dissolveMaterial = new Material(settings.DissolveShader) { name = "BuildDissolve (Instance)" };
            _renderer.sharedMaterial = _dissolveMaterial;
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>
        /// Advances the effect by deltaTime. Public and parameterised rather than reading Time
        /// itself, so the smoothing and the flash are testable without a frame loop.
        /// </summary>
        public void Tick(float deltaTime)
        {
            Initialize();
            if (settings == null) return;

            if (_site != null) TargetProgress = ProgressOf(_site);
            float target = Mathf.Clamp01(TargetProgress);

            // A rise means material just landed. Detected on the target, not on the displayed
            // value, so the flash marks the arrival rather than the catching up.
            if (target > _lastTargetProgress) FlashRemaining = settings.DeliveryFlashDuration;
            _lastTargetProgress = target;

            if (FlashRemaining > 0f) FlashRemaining = Mathf.Max(0f, FlashRemaining - deltaTime);

            DisplayedProgress = Mathf.Min(target, DisplayedProgress + ProgressRate * deltaTime);

            PushToRenderer();

            if (IsComplete) Complete();
        }

        void PushToRenderer()
        {
            if (_renderer == null) return;

            // A property block, never the material: two buildings of the same type under
            // construction at once would otherwise share one progress value - whichever wrote last.
            _renderer.GetPropertyBlock(_propertyBlock);

            _propertyBlock.SetFloat(ProgressId, DisplayedProgress);
            _propertyBlock.SetFloat(RimBoostId, CurrentFlashBoost());
            _propertyBlock.SetFloat(NoiseScaleId, settings.NoiseScale);
            _propertyBlock.SetFloat(NoiseWeightId, settings.NoiseWeight);
            _propertyBlock.SetFloat(RimWidthId, settings.RimWidth);
            _propertyBlock.SetColor(RimColorId, settings.RimColor);
            _propertyBlock.SetFloat(RevealModeId, settings.RevealMode);

            // The reveal gradient is normalised over the caster's world-space extent - see the
            // shader's _BuildBounds for why neither UVs nor object space would do.
            Bounds bounds = _renderer.bounds;
            _propertyBlock.SetVector(BuildBoundsId, new Vector4(bounds.min.x, bounds.min.y, bounds.size.x, bounds.size.y));

            _renderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>Peak intensity at the instant of delivery, falling linearly to zero over the flash duration.</summary>
        public float CurrentFlashBoost()
        {
            if (settings == null || FlashRemaining <= 0f || settings.DeliveryFlashDuration <= 0f) return 0f;
            return settings.DeliveryFlashIntensity * (FlashRemaining / settings.DeliveryFlashDuration);
        }

        /// <summary>
        /// Completion is DisplayedProgress reaching 1, not the materials being delivered - the
        /// building becomes whole when it finishes assembling, which is strictly later.
        /// </summary>
        void Complete()
        {
            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetFloat(ProgressId, 1f);
                _propertyBlock.SetFloat(RimBoostId, 0f);
                _renderer.SetPropertyBlock(_propertyBlock);

                if (_originalMaterial != null) _renderer.sharedMaterial = _originalMaterial;
            }

            if (_flipbook != null) _flipbook.enabled = true;

            DestroyOwned(_dissolveMaterial);
            _dissolveMaterial = null;

            DestroyOwned(this);
        }

        void OnDestroy()
        {
            DestroyOwned(_dissolveMaterial);
            _dissolveMaterial = null;
        }

        /// <summary>Destroy takes effect next frame in play mode and is an error outside it, so the two cases are split rather than leaving edit-mode callers with a live object.</summary>
        static void DestroyOwned(Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) Destroy(target);
            else DestroyImmediate(target);
        }
    }
}

using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Casts a drop shadow for the SpriteRenderer on this GameObject: a child renderer showing the
    /// exact same sprite in flat black, offset toward the sun. No new art asset is involved - the
    /// shadow IS the caster's silhouette, so it stays correct for free when the sprite is swapped
    /// or animated (SpriteFlipbook).
    ///
    /// Every value that shapes the shadow (opacity, sun offset, depth) comes from one shared
    /// BuildingShadowSettings asset, never from a per-building field: changing the sun there moves
    /// every shadow in the game. A null settings reference disables the shadow entirely rather than
    /// falling back to a hardcoded look - same convention as GroundSlabSettings' missing textures.
    ///
    /// The offset is applied in WORLD space even though the shadow is a child: a Splitter or a
    /// conveyor corner rotates its view, and a shadow that turned with it would read as the sun
    /// turning with the building.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class DropShadow : MonoBehaviour
    {
        const string ShadowChildName = "Shadow";

        [SerializeField] BuildingShadowSettings settings;

        /// <summary>
        /// How high off the ground this particular caster sits, as a multiple of the shared sun
        /// offset. 1 is something standing on the ground; a flying unit uses more, and its shadow
        /// falls that much further away - which is what reads as altitude in a top-down view.
        /// The sun direction itself stays global, so raising this never puts one object's shadow
        /// on a different side than everything else's.
        /// </summary>
        [SerializeField, Min(0f)] float heightMultiplier = 1f;

        SpriteRenderer _caster;
        SpriteRenderer _shadow;

        // Last values pushed to the shadow renderer. LateUpdate compares against these and writes
        // nothing while they hold, so a scene full of buildings costs a handful of comparisons per
        // frame rather than a transform write per building.
        Sprite _appliedSprite;
        Color _appliedColor;
        int _appliedSortingOrder;
        Vector2 _appliedOffset;
        float _appliedScale;
        Quaternion _appliedCasterRotation;
        Vector3 _appliedCasterScale;
        bool _hasApplied;

        /// <summary>The shared asset this shadow reads from. Assigned in the inspector; settable from code for a view built at runtime, which is how every building view in this project is created.</summary>
        public BuildingShadowSettings Settings
        {
            get => settings;
            set
            {
                settings = value;
                _hasApplied = false;
            }
        }

        /// <summary>The child renderer drawing the shadow. Null until Awake has run, and while no settings asset is assigned.</summary>
        public SpriteRenderer ShadowRenderer => _shadow;

        /// <summary>See the field: multiplies the shared sun offset for this caster only. Settable from code for a view built at runtime.</summary>
        public float HeightMultiplier
        {
            get => heightMultiplier;
            set
            {
                heightMultiplier = Mathf.Max(0f, value);
                _hasApplied = false;
            }
        }

        void Awake()
        {
            _caster = GetComponent<SpriteRenderer>();
            Apply();
        }

        void LateUpdate()
        {
            Apply();
        }

        /// <summary>
        /// Brings the shadow in line with the caster and the current settings, creating it on first
        /// use. Idempotent and cheap when nothing changed; call it directly to force a refresh
        /// outside the normal frame loop.
        /// </summary>
        public void Apply()
        {
            if (settings == null) return;
            if (_caster == null) _caster = GetComponent<SpriteRenderer>();
            if (_shadow == null) CreateShadowRenderer();

            if (_appliedSprite != _caster.sprite)
            {
                _shadow.sprite = _caster.sprite;
                _appliedSprite = _caster.sprite;
            }

            // The caster can be mirrored (ConveyorView flips a corner's chirality via negative
            // scale, but a renderer-level flip is just as legal) - a shadow that ignored it would
            // be the mirror image of the thing casting it.
            _shadow.flipX = _caster.flipX;
            _shadow.flipY = _caster.flipY;

            Color color = settings.ShadowColor;
            if (!_hasApplied || _appliedColor != color)
            {
                _shadow.color = color;
                _appliedColor = color;
            }

            if (!_hasApplied || _appliedSortingOrder != settings.SortingOrder)
            {
                _shadow.sortingOrder = settings.SortingOrder;
                _appliedSortingOrder = settings.SortingOrder;
            }

            // A local scale, so it multiplies whatever size the caster already is - including a
            // negative axis where the caster is mirrored, which must be preserved.
            if (!_hasApplied || _appliedScale != settings.Scale)
            {
                _shadow.transform.localScale = Vector3.one * settings.Scale;
                _appliedScale = settings.Scale;
            }

            // Only the caster's rotation and scale change what local offset produces the wanted
            // world offset; a caster that merely translates drags the child along for free.
            Vector2 offset = settings.Offset * heightMultiplier;
            if (!_hasApplied
                || _appliedOffset != offset
                || _appliedCasterRotation != transform.rotation
                || _appliedCasterScale != transform.lossyScale)
            {
                _shadow.transform.position = transform.position + new Vector3(offset.x, offset.y, 0f);
                _appliedOffset = offset;
                _appliedCasterRotation = transform.rotation;
                _appliedCasterScale = transform.lossyScale;
            }

            _hasApplied = true;
        }

        void CreateShadowRenderer()
        {
            var go = new GameObject(ShadowChildName);
            go.transform.SetParent(transform, false);
            _shadow = go.AddComponent<SpriteRenderer>();
            _shadow.sortingLayerID = _caster.sortingLayerID;
        }
    }
}

using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Real fog-of-war overlay: everything outside the Core's action radius is hidden under a
    /// dark, soft-edged fog, not just visually indicated by a ring (see ActionRadiusView, which
    /// stays as the thin boundary line). The Core is the only vision source that exists today, so
    /// this is a single static circular reveal, not a multi-source/explored-memory system.
    ///
    /// The fog quad itself always resizes/recenters to the main camera's current view every
    /// frame (camera panning/zooming is unbounded - see CameraPanController), so the fog covers
    /// whatever is on screen rather than a fixed area around the Core.
    /// </summary>
    public sealed class FogOfWarView : MonoBehaviour
    {
        const string ShaderName = "Custom/FogOfWar";
        const int SortingOrder = 50; // above every building/item/arrow sprite, still below UI Toolkit's screen-space overlay.
        const float ViewportMargin = 2f; // world units of slack so a resize/rotation never leaves a visible seam.

        [SerializeField] Color fogColor = new Color(0.02f, 0.03f, 0.05f, 0.96f);
        [SerializeField, Min(0f)] float edgeSoftness = 2f;

        SpriteRenderer _renderer;
        Material _material;
        Camera _camera;
        bool _initialized;

        public void Initialize(Vector3 centerWorld, float radiusWorld)
        {
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = CreateCenteredUnitSprite();
                _renderer.sortingOrder = SortingOrder;
                _material = new Material(Shader.Find(ShaderName)) { name = "FogOfWar (Instance)" };
                _renderer.sharedMaterial = _material;
            }

            _material.SetVector("_Center", centerWorld);
            _material.SetFloat("_Radius", radiusWorld);
            _material.SetFloat("_EdgeSoftness", edgeSoftness);
            _material.SetColor("_FogColor", fogColor);

            _initialized = true;
        }

        void LateUpdate()
        {
            if (!_initialized) return;

            if (_camera == null) _camera = Camera.main;
            if (_camera == null) return;

            float height = _camera.orthographicSize * 2f + ViewportMargin * 2f;
            float width = height * _camera.aspect;

            transform.position = new Vector3(_camera.transform.position.x, _camera.transform.position.y, transform.position.z);
            transform.localScale = new Vector3(width, height, 1f);
        }

        static Sprite CreateCenteredUnitSprite()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }
    }
}

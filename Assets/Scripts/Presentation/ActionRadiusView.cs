using UnityEngine;

namespace Game.Presentation
{
    /// <summary>Purely visual ring showing the Core's action radius - no gameplay effect.</summary>
    public sealed class ActionRadiusView : MonoBehaviour
    {
        const int SortingOrder = 6; // above terrain (0/1) and grid lines (5), below buildings (9/10/11).

        /// <summary>Custom/ActionRadiusOverlay. An asset reference, not a Shader.Find by name: a shader only reached by name is stripped from a player build unless it is also listed in Always Included Shaders, and that list is a protection somebody has to remember to maintain. See docs/BUILD.md.</summary>
        [SerializeField] Shader overlayShader;

        [SerializeField] Color lineColor = new Color(0.2f, 0.85f, 1f, 0.6f);
        [SerializeField, Min(0.001f)] float lineThickness = 0.2f;

        SpriteRenderer _renderer;
        Material _material;

        public void Initialize(Vector3 centerWorld, float radiusWorld)
        {
            if (_renderer == null)
            {
                _renderer = gameObject.AddComponent<SpriteRenderer>();
                _renderer.sprite = CreateCenteredUnitSprite();
                _renderer.sortingOrder = SortingOrder;
                _material = new Material(overlayShader) { name = "ActionRadius (Instance)" };
                _renderer.sharedMaterial = _material;
            }

            float quadSize = radiusWorld * 2f + lineThickness * 4f;
            transform.position = centerWorld;
            transform.localScale = new Vector3(quadSize, quadSize, 1f);

            _material.SetVector("_Center", centerWorld);
            _material.SetFloat("_Radius", radiusWorld);
            _material.SetFloat("_LineThickness", lineThickness);
            _material.SetColor("_LineColor", lineColor);
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

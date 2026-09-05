using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    /// <summary>Mouse-wheel zoom for an orthographic camera. Only touches orthographicSize - never repositions the camera, so it composes cleanly with CameraPanController.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraZoomController : MonoBehaviour
    {
        /// <summary>
        /// Optional; when set, scrolling with the cursor over a UI element scrolls that element
        /// instead of also zooming the world underneath it - the Research panel's list being the
        /// case that first needed it. Null is fine wherever no such gating is needed (e.g.
        /// EditMode/PlayMode tests), and simply means the wheel always zooms.
        ///
        /// This asks where the <b>cursor</b> is, not whether a panel is open. Gating on "a panel is
        /// open" is right for a click, which must not fall through onto the world behind the panel,
        /// and wrong for the wheel: it took zooming away over the whole screen whenever any panel
        /// was open or any building selected, placement included. See PointerOverUI.
        /// </summary>
        [SerializeField] UIDocument uiDocument;

        [SerializeField, Min(0.01f)] float zoomSpeed = 4f;
        [SerializeField, Min(0.01f)] float minOrthographicSize = 10f;
        [SerializeField, Min(0.01f)] float maxOrthographicSize = 40f;
        [SerializeField, Min(0.01f)] float smoothing = 12f;

        Camera _camera;
        float _targetSize;

        void Awake()
        {
            _camera = GetComponent<Camera>();
            _targetSize = _camera.orthographicSize;
        }

        void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            float scroll = PointerOverUI.At(uiDocument, mouse.position.ReadValue()) ? 0f : mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                _targetSize = Mathf.Clamp(_targetSize - scroll * zoomSpeed * 0.01f * _targetSize, minOrthographicSize, maxOrthographicSize);
            }

            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetSize, Time.deltaTime * smoothing);
        }
    }
}

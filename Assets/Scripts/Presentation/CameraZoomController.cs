using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>Mouse-wheel zoom for an orthographic camera. Only touches orthographicSize - never repositions the camera, so it composes cleanly with CameraPanController.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraZoomController : MonoBehaviour
    {
        /// <summary>
        /// Optional; when set, scrolling while a UI panel owns input (a global panel or a
        /// selected building's inspector) no longer also zooms the world underneath it -
        /// e.g. scrolling the Research panel's list used to zoom the camera at the same time.
        /// Null is fine wherever no such gating is needed (e.g. EditMode/PlayMode tests).
        /// </summary>
        [SerializeField] GameRuntime gameRuntime;

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

            float scroll = gameRuntime != null && gameRuntime.IsUIBlockingInput ? 0f : mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                _targetSize = Mathf.Clamp(_targetSize - scroll * zoomSpeed * 0.01f * _targetSize, minOrthographicSize, maxOrthographicSize);
            }

            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetSize, Time.deltaTime * smoothing);
        }
    }
}

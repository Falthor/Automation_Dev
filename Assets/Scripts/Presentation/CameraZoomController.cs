using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>Mouse-wheel zoom for an orthographic camera. Only touches orthographicSize - never repositions the camera, so it composes cleanly with CameraPanController.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraZoomController : MonoBehaviour
    {
        [SerializeField, Min(0.01f)] float zoomSpeed = 4f;
        [SerializeField, Min(0.01f)] float minOrthographicSize = 3f;
        [SerializeField, Min(0.01f)] float maxOrthographicSize = 60f;
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

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                _targetSize = Mathf.Clamp(_targetSize - scroll * zoomSpeed * 0.01f * _targetSize, minOrthographicSize, maxOrthographicSize);
            }

            _camera.orthographicSize = Mathf.Lerp(_camera.orthographicSize, _targetSize, Time.deltaTime * smoothing);
        }
    }
}

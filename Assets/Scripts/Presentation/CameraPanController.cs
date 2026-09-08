using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>
    /// AZERTY camera panning: Z=North, Q=West, S=South, D=East.
    ///
    /// Driven by <b>unscaled</b> time, like CameraZoomController: pause freezes the simulation by
    /// setting Time.timeScale to 0, and where the player is looking is not part of that simulation.
    /// Reading a frozen board is what pause is for, so the camera has to keep answering.
    /// </summary>
    public sealed class CameraPanController : MonoBehaviour
    {
        [SerializeField] float panSpeed = 10f;

        GameRuntime _gameRuntime;

        // Found rather than wired, like GameRuntime does for the zoom controller: there is one of
        // each in the scene, and a missing one only means nothing ever suppresses panning.
        void Start() => _gameRuntime = FindAnyObjectByType<GameRuntime>();

        void Update()
        {
            // A panel that navigates with the keyboard takes ZQSD for itself. Without this the map
            // panned and the world scrolled underneath it on the same keypress.
            if (_gameRuntime != null && _gameRuntime.KeyboardOwnedByPanel) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 move = Vector2.zero;
            if (keyboard.zKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.qKey.isPressed) move.x -= 1f;

            if (move.sqrMagnitude > 0f)
            {
                transform.position += (Vector3)(move.normalized * panSpeed * Time.unscaledDeltaTime);
            }
        }
    }
}

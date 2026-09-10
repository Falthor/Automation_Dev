using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.Presentation
{
    /// <summary>
    /// Camera panning, two ways: AZERTY keys (Z=North, Q=West, S=South, D=East) and dragging the
    /// world with the left mouse button held.
    ///
    /// Driven by <b>unscaled</b> time, like CameraZoomController: pause freezes the simulation by
    /// setting Time.timeScale to 0, and where the player is looking is not part of that simulation.
    /// Reading a frozen board is what pause is for, so the camera has to keep answering.
    ///
    /// <b>The drag grabs the world, not the camera.</b> The mouse moves the ground the same way the
    /// hand moves, which is the camera moving the opposite way - hence the subtraction in
    /// <see cref="Drag"/>. Scaled so the ground tracks the cursor one-to-one at any zoom: a pixel of
    /// mouse is a pixel of world, whatever the orthographic size happens to be.
    ///
    /// <b>The cursor moves with the hand and stays visible</b>, so the world point it grabbed stays
    /// under it for the whole gesture - which is the visible promise of a grab, and the reason the
    /// movement is read from the pointer's <i>position</i> rather than from the device's raw delta.
    /// The two are not the same number: pointer acceleration scales one and not the other, and at the
    /// edge of the screen the position stops changing while the device keeps reporting motion. Using
    /// the position is what keeps the ground glued to the cursor instead of sliding out from under it.
    ///
    /// Translation only, and it stays that way. The zoom controller touches `orthographicSize` and
    /// never the position; this touches the position and never the size. That is what lets the two
    /// run at once without either having to know about the other.
    /// </summary>
    public sealed class CameraPanController : MonoBehaviour
    {
        [SerializeField] float panSpeed = 10f;

        /// <summary>
        /// Optional, and the same role it plays on CameraZoomController: a drag starting on a UI
        /// element belongs to that element, not to the world behind it. Null means "no UI to be
        /// over", so a scene without one is fully draggable rather than fully inert.
        /// </summary>
        [SerializeField] UIDocument uiDocument;

        /// <summary>
        /// How far the mouse has to travel with the button down before this is a drag rather than a
        /// click. Without it every click would nudge the world by a pixel or two, and every click on
        /// a machine would be ambiguous between opening its panel and grabbing the ground.
        /// </summary>
        [SerializeField, Min(0f)] float dragSlopPixels = 3f;

        GameRuntime _gameRuntime;
        Camera _camera;

        /// <summary>Left button is down on the world and this may yet become a drag - it is not one until the slop is crossed.</summary>
        bool _pressed;

        bool _dragging;

        /// <summary>Where the pointer was last frame, so a frame's movement is a difference of positions rather than a device delta - see the class summary.</summary>
        Vector2 _lastPointerPosition;

        /// <summary>
        /// Pointer travel since the current press, for the whole gesture rather than only up to the
        /// slop. Reset at the next press and at nothing else, which is what makes
        /// <see cref="PressTravelPixels"/> readable on the release frame however the component order
        /// happens to fall.
        /// </summary>
        Vector2 _travelSincePress;

        /// <summary>
        /// How far the pointer has travelled since the left button went down, in pixels. Held until
        /// the next press, so it is still this gesture's answer on the frame the button comes up.
        ///
        /// <b>Published so the click router can ask one question of one accumulator.</b> A drag has to
        /// suppress the click it began with, and the alternative was a second press-tracker with a
        /// second copy of the threshold - two implementations of one rule, free to disagree the day
        /// either is touched.
        /// </summary>
        public float PressTravelPixels => _travelSincePress.magnitude;

        /// <summary>The one threshold that separates a click from a drag. Read by the click router rather than duplicated there.</summary>
        public float DragSlopPixels => dragSlopPixels;

        /// <summary>Whether the current gesture has already become a drag - true from the slop being crossed until the button is released.</summary>
        public bool IsDraggingTheWorld => _dragging;

        // Found rather than wired, like GameRuntime does for the zoom controller: there is one of
        // each in the scene, and a missing one only means nothing ever suppresses panning.
        void Start()
        {
            _gameRuntime = FindAnyObjectByType<GameRuntime>();
            _camera = GetComponent<Camera>();
        }

        void Update()
        {
            PanWithKeyboard();
            PanWithMouse();
        }

        void PanWithKeyboard()
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

        void PanWithMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 pointer = mouse.position.ReadValue();

            // Every press starts a fresh gesture, including one the camera is about to decline.
            // Reset inside the accepted-press branch instead and a refused press inherits the travel
            // of the last real drag - after which the click router, reading it, would swallow a click
            // that never moved a pixel.
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _travelSincePress = Vector2.zero;
                _lastPointerPosition = pointer;
            }

            Vector2 movement = pointer - _lastPointerPosition;
            _lastPointerPosition = pointer;

            if (_dragging)
            {
                // Kept accumulating past the slop, so the total is still this gesture's when the
                // click router reads it on the release frame.
                _travelSincePress += movement;

                // Deliberately not re-checking whether the drag would still be allowed to start.
                // A gesture that has begun runs to the button being released - including one whose
                // cursor has wandered over a panel on the way, which must not drop the ground
                // mid-throw.
                if (mouse.leftButton.isPressed) Drag(movement);
                else _dragging = false;
                return;
            }

            if (_pressed)
            {
                if (!mouse.leftButton.isPressed)
                {
                    // Released under the slop: that was a click, and it was somebody else's. Nothing
                    // to undo - the camera never moved.
                    _pressed = false;
                    return;
                }

                _travelSincePress += movement;
                if (_travelSincePress.sqrMagnitude < dragSlopPixels * dragSlopPixels) return;

                _pressed = false;
                _dragging = true;

                // The travel that proved this was a drag is spent rather than dropped, so the ground
                // ends up grabbed at the point the button actually went down instead of a few pixels
                // along from it.
                Drag(_travelSincePress);
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || !MayGrabTheWorld(pointer)) return;

            _pressed = true;
        }

        /// <summary>
        /// Whether a press here is the world's to take.
        ///
        /// Three things own the left button ahead of the camera. A UI element under the cursor owns
        /// its own clicks. An open global panel owns the click even outside itself, because clicking
        /// away is how it closes. And an armed construction tool owns left-drag outright - that
        /// gesture is how a run of conveyors is laid, and panning would take it away.
        ///
        /// A per-building panel being open is deliberately <b>not</b> in that list: it docks to the
        /// right and leaves most of the world in view, so dragging what is still visible is fair.
        /// </summary>
        bool MayGrabTheWorld(Vector2 pointer)
        {
            if (PointerOverUI.At(uiDocument, pointer)) return false;
            if (_gameRuntime == null) return true;

            return _gameRuntime.Selection?.ActiveGlobalPanel == null
                && _gameRuntime.Construction?.Selected == null;
        }

        /// <summary>
        /// Moves the ground with the hand. Subtracted because the camera goes the other way, and
        /// scaled by world-units-per-pixel so the grab holds at any zoom - the orthographic height
        /// is <c>2 x orthographicSize</c> world units across <c>Screen.height</c> pixels.
        ///
        /// No <c>deltaTime</c> anywhere: this is a displacement the player has already made with
        /// their hand, not a rate. Multiplying it by a frame time would make the same gesture move
        /// the world differently at a different framerate.
        /// </summary>
        void Drag(Vector2 movementPixels)
        {
            if (_camera == null || Screen.height <= 0) return;

            float worldPerPixel = _camera.orthographicSize * 2f / Screen.height;
            transform.position -= (Vector3)(movementPixels * worldPerPixel);
        }

        /// <summary>Drops the gesture if this component or the application goes away mid-drag, so the world does not lurch when focus comes back and the pointer has moved elsewhere meanwhile.</summary>
        void OnDisable()
        {
            _dragging = false;
            _pressed = false;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus) return;

            _dragging = false;
            _pressed = false;
        }
    }
}

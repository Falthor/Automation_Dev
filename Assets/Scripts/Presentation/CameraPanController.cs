using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

// UIElements has a Cursor of its own - a style value, not the pointer. Aliased rather than
// fully qualified at each use, since only one of the two is ever meant here.
using Cursor = UnityEngine.Cursor;

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
        /// click. Without it every click would pan the world by a pixel or two and hide the cursor
        /// for a frame.
        /// </summary>
        [SerializeField, Min(0f)] float dragSlopPixels = 3f;

        GameRuntime _gameRuntime;
        Camera _camera;

        /// <summary>Left button is down on the world and this may yet become a drag - it is not one until the slop is crossed.</summary>
        bool _pressed;

        /// <summary>
        /// Mouse travel since the current press, for the whole gesture rather than only up to the
        /// slop. Reset at the next press and at nothing else, which is what makes
        /// <see cref="PressTravelPixels"/> readable on the release frame however the component order
        /// happens to fall.
        /// </summary>
        Vector2 _travelSincePress;

        bool _dragging;

        /// <summary>
        /// How far the mouse has travelled since the left button went down, in pixels. Held until the
        /// next press, so it is still this gesture's answer on the frame the button comes up.
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

        /// <summary>
        /// Where the operating system's cursor stood when the button went down, in its own screen
        /// coordinates.
        ///
        /// <b>Recorded at the press, not at the moment the drag starts</b>, and put back verbatim on
        /// release - so the cursor reappears where the player pressed rather than a few pixels along,
        /// and no conversion between Unity's screen space and the desktop's is ever needed. Asking
        /// the OS both times is what makes that exact.
        /// </summary>
        OsPoint _cursorAnchor;

        bool _cursorAnchored;

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

            // Every press starts a fresh gesture, including one the camera is about to decline.
            // Reset inside the accepted-press branch instead and a refused press inherits the travel
            // of the last real drag - after which the click router, reading it, would swallow a click
            // that never moved a pixel.
            if (mouse.leftButton.wasPressedThisFrame) _travelSincePress = Vector2.zero;

            Vector2 delta = mouse.delta.ReadValue();

            if (_dragging)
            {
                // Kept accumulating past the slop, so the total is still this gesture's when the
                // click router reads it on the release frame.
                _travelSincePress += delta;

                // Deliberately not re-checking whether the drag would still be allowed to start.
                // A gesture that has begun runs to the button being released: losing the lock
                // half-way would strand the cursor hidden somewhere it never asked to be.
                if (mouse.leftButton.isPressed) Drag(delta);
                else EndDrag();
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

                _travelSincePress += delta;
                if (_travelSincePress.sqrMagnitude < dragSlopPixels * dragSlopPixels) return;

                BeginDrag();

                // The travel that proved this was a drag is spent rather than dropped, so the ground
                // starts moving from where it was actually grabbed.
                Drag(_travelSincePress);
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame || !MayGrabTheWorld(mouse)) return;

            _pressed = true;
            AnchorCursor();
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
        bool MayGrabTheWorld(Mouse mouse)
        {
            if (PointerOverUI.At(uiDocument, mouse.position.ReadValue())) return false;
            if (_gameRuntime == null) return true;

            return _gameRuntime.Selection?.ActiveGlobalPanel == null
                && _gameRuntime.Construction?.Selected == null;
        }

        /// <summary>
        /// Moves the ground with the hand. Subtracted because the camera goes the other way, and
        /// scaled by world-units-per-pixel so the grab holds at any zoom - the orthographic height
        /// is <c>2 x orthographicSize</c> world units across <c>Screen.height</c> pixels.
        ///
        /// No <c>deltaTime</c> anywhere: the Input System already reports the movement accumulated
        /// this frame, and a one-to-one drag must follow the mouse rather than a rate.
        /// </summary>
        void Drag(Vector2 deltaPixels)
        {
            if (_camera == null || Screen.height <= 0) return;

            float worldPerPixel = _camera.orthographicSize * 2f / Screen.height;
            transform.position -= (Vector3)(deltaPixels * worldPerPixel);
        }

        void BeginDrag()
        {
            _pressed = false;
            _dragging = true;

            // Unity's own pinning: the cursor stops moving and the mouse keeps reporting raw
            // movement, which is exactly the pair this needs. Hidden as well, because a frozen
            // arrow sitting in the middle of a moving world reads as a stuck game.
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        void EndDrag()
        {
            _dragging = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ReleaseCursorToAnchor();
        }

        /// <summary>Restores the cursor and the camera's claim on it if this component or the application goes away mid-gesture. Without it, quitting or disabling during a drag leaves the cursor hidden and locked.</summary>
        void OnDisable()
        {
            if (_dragging) EndDrag();
            _pressed = false;
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && _dragging) EndDrag();
        }

        // ---- Where the cursor was ----

        [StructLayout(LayoutKind.Sequential)]
        struct OsPoint
        {
            public int X;
            public int Y;
        }

        void AnchorCursor()
        {
            _cursorAnchored = TryGetOsCursor(out _cursorAnchor);
        }

        /// <summary>
        /// Puts the cursor back where the button went down.
        ///
        /// <b>Unlocking alone does not do this.</b> A locked cursor is parked at the centre of the
        /// window, and that is where it is handed back - so without this the pointer jumps to the
        /// middle of the screen at the end of every drag. Windows is asked directly because Unity has
        /// no cross-platform way to place a cursor; anywhere else this is a no-op and the pointer
        /// arrives wherever the platform left it.
        /// </summary>
        void ReleaseCursorToAnchor()
        {
            if (!_cursorAnchored) return;

            _cursorAnchored = false;
            TrySetOsCursor(_cursorAnchor);
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out OsPoint point);

        [DllImport("user32.dll")]
        static extern bool SetCursorPos(int x, int y);

        static bool TryGetOsCursor(out OsPoint point) => GetCursorPos(out point);

        static void TrySetOsCursor(OsPoint point) => SetCursorPos(point.X, point.Y);
#else
        static bool TryGetOsCursor(out OsPoint point)
        {
            point = default;
            return false;
        }

        static void TrySetOsCursor(OsPoint point) { }
#endif
    }
}

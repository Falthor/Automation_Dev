using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Presentation
{
    /// <summary>AZERTY camera panning: Z=North, Q=West, S=South, D=East.</summary>
    public sealed class CameraPanController : MonoBehaviour
    {
        [SerializeField] float panSpeed = 10f;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            Vector2 move = Vector2.zero;
            if (keyboard.zKey.isPressed) move.y += 1f;
            if (keyboard.sKey.isPressed) move.y -= 1f;
            if (keyboard.dKey.isPressed) move.x += 1f;
            if (keyboard.qKey.isPressed) move.x -= 1f;

            if (move.sqrMagnitude > 0f)
            {
                transform.position += (Vector3)(move.normalized * panSpeed * Time.deltaTime);
            }
        }
    }
}

using Game.Gameplay.Buildings;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The red Energy glyph a power-starved building wears.
    ///
    /// On the building, not only in a panel, for the same reason <see cref="PausedBadgeView"/> is:
    /// the player scanning a stalled base needs the answer from the map, not from opening one
    /// building at a time.
    ///
    /// Reads the runtime every frame rather than subscribing - one enabled-flag comparison per
    /// power-consuming building is far below anything worth an event for, matching
    /// <see cref="PausedBadgeView"/>'s own reasoning exactly.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PowerShortageBadgeView : MonoBehaviour
    {
        BuildingRuntime _runtime;
        SpriteRenderer _renderer;

        public void Bind(BuildingRuntime runtime)
        {
            _runtime = runtime;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.enabled = false;
        }

        void LateUpdate()
        {
            if (_runtime == null || _renderer == null) return;

            _renderer.enabled = _runtime.IsUnderpowered;
        }
    }
}

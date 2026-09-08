using Game.Gameplay.Buildings;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// The pause glyph a switched-off building wears.
    ///
    /// <b>On the building, not only in its panel.</b> Pausing used to be visible only from inside
    /// the panel of the building you had already selected, so a base with one machine stopped looked
    /// exactly like a base with none - and the way to find the stopped one was to open every panel
    /// in turn. A player scanning a stalled line needs the answer from the map.
    ///
    /// Reads the runtime every frame rather than subscribing: pause is a bool on the building, the
    /// building is the single source of truth for it (CONTRACTS.md §7), and one enabled-flag
    /// comparison per paused-capable building is far below anything worth an event for.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PausedBadgeView : MonoBehaviour
    {
        ProductionBuildingRuntime _runtime;
        SpriteRenderer _renderer;

        public void Bind(ProductionBuildingRuntime runtime)
        {
            _runtime = runtime;
            _renderer = GetComponent<SpriteRenderer>();
            _renderer.enabled = false;
        }

        void LateUpdate()
        {
            if (_runtime == null || _renderer == null) return;

            _renderer.enabled = _runtime.IsPaused;
        }
    }
}

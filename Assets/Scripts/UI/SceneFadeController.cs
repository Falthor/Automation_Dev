using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Reusable full-screen black fade overlay for scene-boundary transitions (Intro to Genesis,
    /// Genesis's own fade-in). Lazily creates a VisualElement on the owning UIDocument's root and
    /// keeps it topmost via BringToFront() on every fade start, since Awake/Start ordering between
    /// this component and the scene's own UI content is not guaranteed. One instance per scene -
    /// not a singleton, no DontDestroyOnLoad (DEVELOPMENT_RULES.md §1).
    /// </summary>
    public sealed class SceneFadeController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;

        VisualElement _overlay;
        float _from;
        float _to;
        float _duration;
        float _elapsed;
        bool _fading;
        Action _onComplete;

        VisualElement Overlay
        {
            get
            {
                if (_overlay == null)
                {
                    _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
                    _overlay.style.position = Position.Absolute;
                    _overlay.style.left = 0;
                    _overlay.style.right = 0;
                    _overlay.style.top = 0;
                    _overlay.style.bottom = 0;
                    _overlay.style.backgroundColor = Color.black;
                    uiDocument.rootVisualElement.Add(_overlay);
                }

                _overlay.BringToFront();
                return _overlay;
            }
        }

        /// <summary>Sets the overlay opacity instantly (1 = fully black, 0 = fully clear), without animating.</summary>
        public void SetOpacity(float value)
        {
            Overlay.style.opacity = Mathf.Clamp01(value);
        }

        /// <summary>Animates the overlay from black (1) to clear (0), revealing the scene's own content.</summary>
        public void FadeIn(float duration, Action onComplete = null) => StartFade(1f, 0f, duration, onComplete);

        /// <summary>Animates the overlay from clear (0) to black (1), hiding the scene's own content.</summary>
        public void FadeOut(float duration, Action onComplete = null) => StartFade(0f, 1f, duration, onComplete);

        void StartFade(float from, float to, float duration, Action onComplete)
        {
            _from = from;
            _to = to;
            _duration = Mathf.Max(0.0001f, duration);
            _elapsed = 0f;
            _onComplete = onComplete;
            SetOpacity(from);
            _fading = true;
        }

        void Update()
        {
            if (!_fading) return;

            _elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_elapsed / _duration);
            SetOpacity(Mathf.Lerp(_from, _to, t));

            if (t >= 1f)
            {
                _fading = false;
                _onComplete?.Invoke();
            }
        }
    }
}

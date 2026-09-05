using System;
using UnityEngine;

namespace Game.Presentation
{
    /// <summary>
    /// Renders the Custom/TvTuneIn noise/roll shader onto a dedicated RenderTexture, via this
    /// component's own camera+quad rig rather than Genesis's main camera: Genesis's UI runs
    /// Screen Space Overlay (GameUIPanelSettings.asset), which always composites on top of
    /// anything a camera renders, so a world-space quad in front of the main camera could never
    /// visually cover the boot text. GenesisController instead displays RenderTexture as a
    /// full-screen UI Toolkit background image and shows/hides it around calls to Play(), which
    /// keeps this component free of any UI Toolkit knowledge (DEVELOPMENT_RULES.md §6 - UI reads
    /// a public contract, does not get read from).
    /// </summary>
    public sealed class TvTuneInEffect : MonoBehaviour
    {
        [SerializeField] Camera renderCamera;
        [SerializeField] MeshRenderer quadRenderer;
        [SerializeField] RenderTexture renderTexture;

        Material _material;
        float _duration;
        float _elapsed;
        bool _playing;
        Action _onComplete;

        static readonly int ProgressId = Shader.PropertyToID("_Progress");

        /// <summary>The texture GenesisController binds to its full-screen overlay VisualElement.</summary>
        public RenderTexture RenderTexture => renderTexture;

        void Awake()
        {
            _material = quadRenderer.material; // per-instance copy, safe to animate without affecting a shared asset
            gameObject.SetActive(false);
        }

        /// <summary>Activates the render rig and plays the effect over <paramref name="duration"/> seconds, then calls <paramref name="onComplete"/>.</summary>
        public void Play(float duration, Action onComplete = null)
        {
            _duration = Mathf.Max(0.0001f, duration);
            _elapsed = 0f;
            _onComplete = onComplete;
            _playing = true;
            _material.SetFloat(ProgressId, 0f);
            gameObject.SetActive(true);
        }

        void Update()
        {
            if (!_playing) return;

            _elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(_elapsed / _duration);
            _material.SetFloat(ProgressId, progress);

            if (progress >= 1f)
            {
                _playing = false;
                gameObject.SetActive(false);
                _onComplete?.Invoke();
            }
        }
    }
}

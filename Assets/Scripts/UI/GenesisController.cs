using System.Collections;
using System.Collections.Generic;
using Game.Presentation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// Genesis.unity: plays once between Intro.unity and Bootstrap.unity (reached from Intro's
    /// skip or its natural end - never directly from MainMenu). Fades in from black, types out a
    /// fake boot log line by line, then plays the TvTuneInEffect "signal catching" transition
    /// (its RenderTexture bound to TvTuneInOverlay, since Screen Space Overlay UI always draws on
    /// top of anything a camera renders - see TvTuneInEffect's own comment) before loading
    /// Bootstrap. Any key or click at any point skips straight to Bootstrap.
    /// </summary>
    [RequireComponent(typeof(SceneFadeController))]
    public sealed class GenesisController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] string bootstrapSceneName = "Bootstrap";
        [SerializeField] float fadeInDuration = 0.6f;
        [SerializeField] float charsPerSecond = 40f;
        [SerializeField] float lineDelaySeconds = 0.25f;
        [SerializeField] TvTuneInEffect tuneInEffect;
        [SerializeField] float tuneInDuration = 1.3f;
        [SerializeField] string cursorCharacter = "█";
        [SerializeField] float cursorBlinkInterval = 0.35f;
        [SerializeField] int cursorBlinkCount = 3;

        /// <summary>
        /// No monospace font ships in the project, so this references an OS-installed one by name
        /// at runtime (Font.CreateDynamicFontFromOSFont) instead of importing a font file - falls
        /// back to the default UI font silently if the named font isn't installed on this machine.
        /// </summary>
        [SerializeField] string terminalFontName = "Consolas";
        [SerializeField] int terminalFontBaseSize = 32;

        SceneFadeController _fade;
        VisualElement _tuneInOverlay;
        Coroutine _bootSequence;
        bool _loaded;

        void Start()
        {
            _fade = GetComponent<SceneFadeController>();

            VisualElement content = visualTree.CloneTree();
            uiDocument.rootVisualElement.Add(content);
            content.StretchToParentSize();

            ApplyTerminalFont(content);

            _tuneInOverlay = content.Q<VisualElement>("TvTuneInOverlay");
            if (tuneInEffect != null && _tuneInOverlay != null)
            {
                _tuneInOverlay.style.backgroundImage = new StyleBackground(Background.FromRenderTexture(tuneInEffect.RenderTexture));
            }

            _fade.FadeIn(fadeInDuration);

            var bootLines = content.Query<Label>(className: "boot-line").ToList();
            _bootSequence = StartCoroutine(PlayBootSequence(bootLines));
        }

        void ApplyTerminalFont(VisualElement content)
        {
            Font osFont = Font.CreateDynamicFontFromOSFont(terminalFontName, terminalFontBaseSize);
            if (osFont == null) return; // terminalFontName not installed on this machine - keep the default UI font

            content.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(osFont));
        }

        IEnumerator PlayBootSequence(List<Label> lines)
        {
            // Clear every line up front - typing only the current line while leaving the rest at
            // their full UXML-authored text would show the whole boot log immediately and then
            // "retype" each line in turn, instead of building it up from nothing.
            var fullTexts = new string[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                fullTexts[i] = lines[i].text;
                lines[i].text = string.Empty;
            }

            if (lines.Count > 0)
            {
                yield return BlinkCursor(lines[0]);
            }

            float charInterval = 1f / charsPerSecond;

            for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                Label line = lines[lineIndex];
                string fullText = fullTexts[lineIndex];

                for (int i = 1; i <= fullText.Length; i++)
                {
                    line.text = fullText.Substring(0, i) + cursorCharacter;
                    yield return new WaitForSeconds(charInterval);
                }

                line.text = fullText; // drop the trailing cursor once the line is complete
                yield return new WaitForSeconds(lineDelaySeconds);
            }

            BeginTuneIn();
        }

        IEnumerator BlinkCursor(Label line)
        {
            for (int i = 0; i < cursorBlinkCount; i++)
            {
                line.text = cursorCharacter;
                yield return new WaitForSeconds(cursorBlinkInterval);
                line.text = string.Empty;
                yield return new WaitForSeconds(cursorBlinkInterval);
            }
        }

        void BeginTuneIn()
        {
            _bootSequence = null;

            if (tuneInEffect != null && _tuneInOverlay != null)
            {
                _tuneInOverlay.RemoveFromClassList("hidden");
                tuneInEffect.Play(tuneInDuration, LoadBootstrap);
            }
            else
            {
                LoadBootstrap();
            }
        }

        void Update()
        {
            if ((Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame) ||
                (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame))
            {
                LoadBootstrap();
            }
        }

        void LoadBootstrap()
        {
            if (_loaded) return;
            _loaded = true;

            if (_bootSequence != null)
            {
                StopCoroutine(_bootSequence);
                _bootSequence = null;
            }

            SceneManager.LoadScene(bootstrapSceneName);
        }
    }
}

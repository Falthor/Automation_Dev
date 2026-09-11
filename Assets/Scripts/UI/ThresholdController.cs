using System.Collections;
using Game.Gameplay.Compute;
using Game.Gameplay.Exploration;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The message that announces the explorer robots, shown once when the reserve crosses their
    /// threshold. Second of the Core's narrative screens, sharing <see cref="NarrativeScreen"/> and
    /// <c>Narrative.uss</c> with the awakening - the same voice, deliberately.
    ///
    /// <b>An event, not a state, and it needs nothing saved.</b> It fires on the frame
    /// <see cref="ExplorerRobotSystem.RobotsHaveAppeared"/> turns true, having been false when this
    /// controller started. That covers every case the message must not reappear in: a save taken
    /// after the crossing restores the flag as already true, so the screen sees no transition; the
    /// reserve rising back above the threshold and falling again cannot re-fire it, because the
    /// system's own guard only ever flips the flag once; and the development bypass
    /// (<c>MakeRobotsAppear</c>) flips it before the first frame, which reads as "already happened".
    ///
    /// <b>It pauses the game and it can be dismissed.</b> Unlike the awakening this arrives
    /// mid-game, possibly mid-placement, so the button hands control back. The time scale is
    /// restored to whatever it was rather than to 1, so dismissing cannot silently unpause a game
    /// the player had paused.
    /// </summary>
    public sealed class ThresholdController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        [Tooltip("The same image as the awakening screen: the two messages are one voice, and changing it would suggest otherwise.")]
        [SerializeField] Sprite coreImage;

        [Header("Rythme (ajustable en cours de partie)")]
        [Tooltip("Durée du fondu de chaque bloc.")]
        [SerializeField, Min(0f)] float fadeSeconds = 0.5f;

        [Tooltip("Pause ordinaire entre deux phrases.")]
        [SerializeField, Min(0f)] float pauseBetweenLines = 1.9f;

        [Tooltip("Pause après le relevé des deux mesures.")]
        [SerializeField, Min(0f)] float pauseAfterReadout = 3.1f;

        [Tooltip("Pause après « Continuer ainsi ne suffira pas » - plus longue, c'est le constat qui motive la suite.")]
        [SerializeField, Min(0f)] float pauseAfterDeficit = 3.1f;

        [Tooltip("Pause avant la dernière ligne, celle qui est un relevé et non une pensée.")]
        [SerializeField, Min(0f)] float pauseBeforeResult = 2.4f;

        [Tooltip("Pause entre la dernière ligne et l'apparition du bouton.")]
        [SerializeField, Min(0f)] float pauseBeforeButton = 1.2f;

        VisualElement _overlay;
        Button _seeButton;

        /// <summary>What the flag read on the first frame. Anything already true then is history, not a crossing.</summary>
        bool _appearedAtStart;
        bool _shown;

        /// <summary>The time scale as it stood when the screen went up, restored on dismissal rather than assuming 1.</summary>
        float _timeScaleBeforePause = 1f;

        void Start()
        {
            ExplorerRobotSystem explorers = gameRuntime?.ExplorerRobots;
            _appearedAtStart = explorers == null || explorers.RobotsHaveAppeared;
        }

        void Update()
        {
            if (_shown || _appearedAtStart) return;

            ExplorerRobotSystem explorers = gameRuntime?.ExplorerRobots;
            if (explorers == null || !explorers.RobotsHaveAppeared) return;

            _shown = true;
            Show();
        }

        void Show()
        {
            if (uiDocument == null || visualTree == null) return;

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            _overlay = visualTree.CloneTree().Q<VisualElement>("ThresholdOverlay");
            if (_overlay == null) return;

            root.Add(_overlay);
            _overlay.BringToFront();

            if (coreImage != null)
            {
                _overlay.Q<VisualElement>("ThresholdCore").style.backgroundImage = new StyleBackground(coreImage);
            }

            WriteReadout();

            _seeButton = _overlay.Q<Button>("ThresholdSeeButton");
            if (_seeButton != null)
            {
                // Absent rather than disabled, and never focused: a focused UI Toolkit Button
                // answers to Space, and Space is Pause.
                _seeButton.style.display = DisplayStyle.None;
                _seeButton.clicked += Dismiss;
            }

            _timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;

            StartCoroutine(Play());
        }

        /// <summary>
        /// Both figures come from the reserve as it actually stands.
        ///
        /// <b>The share is computed, not written.</b> 71% is what 20 000 against a 70 000 cap
        /// happens to be today; hard-coding it would make the sentence stop meaning what it says
        /// the first time either number moves - which is exactly how the threshold itself twice
        /// came to mean something else.
        /// </summary>
        void WriteReadout()
        {
            float reserve = gameRuntime.Compute != null ? gameRuntime.Compute.Reserve : 0f;

            Label reserveValue = _overlay.Q<Label>("ThresholdReserveValue");
            if (reserveValue != null) reserveValue.text = FormatUnits(reserve);

            Label consumedValue = _overlay.Q<Label>("ThresholdConsumedValue");
            if (consumedValue == null) return;

            float consumed = ComputeSystem.ReserveCap > 0f ? 1f - reserve / ComputeSystem.ReserveCap : 0f;
            consumedValue.text = Mathf.RoundToInt(Mathf.Clamp01(consumed) * 100f) + " %";
        }

        IEnumerator Play()
        {
            yield return NarrativeScreen.Play(
                new[]
                {
                    Step("ThresholdStepTitle", pauseBetweenLines),
                    Step("ThresholdStepReadout", pauseAfterReadout),
                    Step("ThresholdStepDeficit", pauseAfterDeficit),
                    Step("ThresholdStepUnits", pauseBetweenLines),
                    Step("ThresholdStepConvert", pauseBeforeResult),
                    Step("ThresholdStepResult", pauseBeforeButton)
                },
                fadeSeconds,
                () =>
                {
                    if (_seeButton != null) _seeButton.style.display = DisplayStyle.Flex;
                });
        }

        NarrativeScreen.Step Step(string name, float pauseAfter)
            => new NarrativeScreen.Step(_overlay.Q<VisualElement>(name), pauseAfter);

        /// <summary>
        /// Hands control back, and hands the player a robot: the button says "Voir", so it selects
        /// one so there is something to see. The first of the fleet - they are identical at this
        /// point, both just woken and both still at the park.
        /// </summary>
        void Dismiss()
        {
            Time.timeScale = _timeScaleBeforePause;

            _overlay?.RemoveFromHierarchy();
            _overlay = null;

            ExplorerRobotSystem explorers = gameRuntime?.ExplorerRobots;
            if (explorers == null || explorers.Robots.Count == 0) return;

            gameRuntime.Selection.SelectExplorerRobot(explorers.Robots[0]);
        }

        /// <summary>
        /// French grouping and a space before the unit, both non-breaking so a narrow panel can
        /// never split "20 000" or orphan "unités". Same rule as the awakening screen's own figure.
        /// </summary>
        static string FormatUnits(float value)
        {
            string digits = Mathf.FloorToInt(value).ToString();
            var grouped = new System.Text.StringBuilder(digits.Length + 4);

            for (var i = 0; i < digits.Length; i++)
            {
                if (i > 0 && (digits.Length - i) % 3 == 0) grouped.Append(' ');
                grouped.Append(digits[i]);
            }

            return grouped.Append(' ').Append("unités").ToString();
        }
    }
}

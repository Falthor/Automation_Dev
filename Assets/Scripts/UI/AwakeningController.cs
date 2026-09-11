using System.Collections;
using System.Collections.Generic;
using Game.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The Core's first seconds, shown once over the game as a new run begins.
    ///
    /// <b>Not an intro screen.</b> It is a message on arriving in Bootstrap, not a scene of its own
    /// like Intro/Genesis - so it needs no scene, no load, and no place in the build order. It is
    /// an overlay on the game that is already running underneath.
    ///
    /// <b>New game only.</b> Gated on <see cref="GameRuntime.StartedFromNewGame"/> rather than on
    /// the scene loading, which is the whole reason that property exists: showing it on Start would
    /// show it to a player reloading a two-hour run, and by the time any view runs
    /// <c>PendingGameStart</c> has been consumed and cleared.
    ///
    /// <b>It can be skipped, by one button and nothing else.</b> "Passer" sits in the overlay's
    /// corner from the first frame and dismisses the whole thing. It was unskippable, which is
    /// right for a first run and wrong for the twentieth: the message is worth reading once, and a
    /// player who starts a run to test a belt layout should not have to read it again.
    ///
    /// No <i>key</i> dismisses it, still. Escape has one arbiter (<see cref="EscapeArbiter"/>) and
    /// this overlay is not one of its tiers; and neither button is ever focused - a focused UI
    /// Toolkit Button answers to Space, and Space is Pause, so focusing one would hand the player a
    /// second skip key by accident. The skip button is declared non-focusable for that reason,
    /// which costs it nothing: a click still reaches it.
    ///
    /// "Commencer" still does not exist until the last line has landed - not disabled, absent.
    /// Skipping is a deliberate way out, not the same gesture as the end of the message.
    ///
    /// <b>Real time, not game time.</b> Every wait is unscaled, so pausing behind the overlay cannot
    /// stall the message half-way through.
    ///
    /// The reveal itself lives in <see cref="NarrativeScreen"/>, shared with the threshold message.
    /// What stays here is what only this screen knows: when it appears, its steps, and the figure
    /// that falls while it plays.
    /// </summary>
    public sealed class AwakeningController : MonoBehaviour
    {
        [SerializeField] UIDocument uiDocument;
        [SerializeField] VisualTreeAsset visualTree;
        [SerializeField] GameRuntime gameRuntime;

        [Tooltip("Shown small and centred above the title. Left empty, the image is simply absent.")]
        [SerializeField] Sprite coreImage;

        [Header("Rythme (ajustable en cours de partie)")]
        [Tooltip("Durée du fondu de chaque bloc.")]
        [SerializeField, Min(0f)] float fadeSeconds = 0.5f;

        [Tooltip("Pause ordinaire entre deux phrases.")]
        [SerializeField, Min(0f)] float pauseBetweenLines = 1.9f;

        [Tooltip("Pause après le relevé des trois mesures - plus longue, le Noyau vient de lire son état.")]
        [SerializeField, Min(0f)] float pauseAfterReadout = 3.1f;

        [Tooltip("Pause après « une seule tient » - plus longue, c'est la décision.")]
        [SerializeField, Min(0f)] float pauseAfterConclusion = 3.1f;

        [Tooltip("Pause entre la dernière phrase et l'apparition du bouton.")]
        [SerializeField, Min(0f)] float pauseBeforeButton = 1.2f;

        [Tooltip("Secondes entre deux unités perdues par la réserve affichée. Purement visuel.")]
        [SerializeField, Min(0.05f)] float reserveTickSeconds = 1f;

        VisualElement _overlay;
        Label _reserveValue;
        Button _beginButton;
        Button _skipButton;

        /// <summary>The displayed reserve, seeded from the real one and then drifting on its own - the first figure is true, the drift is theatre.</summary>
        float _displayedReserve;
        bool _reserveIsFalling;

        void Start()
        {
            if (gameRuntime == null || !gameRuntime.StartedFromNewGame) return;
            if (uiDocument == null || visualTree == null) return;

            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;

            _overlay = visualTree.CloneTree().Q<VisualElement>("AwakeningOverlay");
            if (_overlay == null) return;

            root.Add(_overlay);
            _overlay.BringToFront(); // over the whole HUD, whatever order the other controllers ran in

            if (coreImage != null)
            {
                _overlay.Q<VisualElement>("AwakeningCore").style.backgroundImage = new StyleBackground(coreImage);
            }

            _reserveValue = _overlay.Q<Label>("AwakeningReserveValue");
            _displayedReserve = gameRuntime.Compute != null ? gameRuntime.Compute.Reserve : 0f;
            if (_reserveValue != null) _reserveValue.text = FormatReserve(_displayedReserve);

            _beginButton = _overlay.Q<Button>("AwakeningBeginButton");
            if (_beginButton != null)
            {
                _beginButton.style.display = DisplayStyle.None;
                _beginButton.clicked += Dismiss;
            }

            _skipButton = _overlay.Q<Button>("AwakeningSkipButton");
            if (_skipButton != null)
            {
                // Belt and braces with the UXML attribute: whichever of the two is read, Space must
                // stay Pause.
                _skipButton.focusable = false;
                _skipButton.clicked += Dismiss;
            }

            StartCoroutine(Play());
        }

        /// <summary>
        /// The reveal order and its rhythm. The readout's own step also starts the figure falling,
        /// so that by the time the player reads "chaque opération me rapproche de la veille" the
        /// sentence has already been demonstrated in front of them.
        /// </summary>
        IEnumerator Play()
        {
            yield return NarrativeScreen.Play(
                new[]
                {
                    Step("AwakeningStepTitle", pauseBetweenLines),
                    Step("AwakeningStepReadout", pauseAfterReadout, () => _reserveIsFalling = true),
                    Step("AwakeningStepDecay", pauseBetweenLines),
                    Step("AwakeningStepConclusion", pauseAfterConclusion),
                    Step("AwakeningStepLearning", pauseBetweenLines),
                    Step("AwakeningStepGround", pauseBeforeButton)
                },
                fadeSeconds,
                () =>
                {
                    if (_beginButton != null) _beginButton.style.display = DisplayStyle.Flex;
                });
        }

        NarrativeScreen.Step Step(string name, float pauseAfter, System.Action onRevealed = null)
            => new NarrativeScreen.Step(_overlay.Q<VisualElement>(name), pauseAfter, onRevealed);

        void Update()
        {
            if (!_reserveIsFalling || _reserveValue == null) return;

            float before = Mathf.Floor(_displayedReserve);
            _displayedReserve -= Time.unscaledDeltaTime / reserveTickSeconds;
            if (_displayedReserve < 0f) _displayedReserve = 0f;

            // Rewritten only when the whole number changes, rather than every frame.
            if (!Mathf.Approximately(Mathf.Floor(_displayedReserve), before))
            {
                _reserveValue.text = FormatReserve(_displayedReserve);
            }
        }

        /// <summary>
        /// Ends the screen, from either button. The coroutine is stopped rather than left to finish
        /// against a detached hierarchy: skipping at the second line otherwise leaves it revealing
        /// blocks nobody can see for another ten seconds, and turning the button on at the end of
        /// it.
        /// </summary>
        void Dismiss()
        {
            StopAllCoroutines();
            _reserveIsFalling = false;
            _overlay?.RemoveFromHierarchy();
            _overlay = null;
        }

        /// <summary>
        /// French grouping and a space before the unit, both with non-breaking spaces so a narrow
        /// panel can never split "70 000" or orphan "unités". Built by hand rather than through a
        /// culture, which would depend on the machine's locale.
        /// </summary>
        static string FormatReserve(float value)
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

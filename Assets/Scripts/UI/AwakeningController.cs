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
    /// <b>It cannot be skipped.</b> No key dismisses it and the button does not exist until the last
    /// line has landed. The button is deliberately never focused: a focused UI Toolkit Button answers
    /// to Space, and Space is Pause - focusing it would hand the player a skip key by accident.
    ///
    /// <b>Real time, not game time.</b> Every wait is unscaled, so pausing behind the overlay cannot
    /// stall the message half-way through.
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

            StartCoroutine(Play());
        }

        /// <summary>
        /// The reveal order and its rhythm. Written as a sequence of (block, pause after it) so the
        /// two long pauses the message needs are two values rather than a special case in the loop.
        /// </summary>
        IEnumerator Play()
        {
            var steps = new List<(string Name, float PauseAfter)>
            {
                ("AwakeningStepTitle", pauseBetweenLines),
                ("AwakeningStepReadout", pauseAfterReadout),
                ("AwakeningStepDecay", pauseBetweenLines),
                ("AwakeningStepConclusion", pauseAfterConclusion),
                ("AwakeningStepLearning", pauseBetweenLines),
                ("AwakeningStepGround", pauseBeforeButton)
            };

            foreach ((string name, float pauseAfter) in steps)
            {
                Reveal(name);

                // The figure starts moving the moment its own line is on screen, so that by the time
                // the player reads "chaque opération me rapproche de la veille" the sentence has
                // already been demonstrated.
                if (name == "AwakeningStepReadout") _reserveIsFalling = true;

                yield return new WaitForSecondsRealtime(pauseAfter);
            }

            if (_beginButton != null) _beginButton.style.display = DisplayStyle.Flex;
        }

        void Reveal(string name)
        {
            VisualElement step = _overlay.Q<VisualElement>(name);
            if (step == null) return;

            step.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(fadeSeconds, TimeUnit.Second) });
            step.style.opacity = 1f;
        }

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

        void Dismiss()
        {
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

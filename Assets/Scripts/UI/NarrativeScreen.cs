using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The reveal mechanism the Core's narrative screens share: blocks appearing one at a time in a
    /// short fade, on a rhythm that follows the weight of what was just said, and an action that
    /// does not exist until the last line has landed.
    ///
    /// <b>Extracted at the second screen rather than the fourth.</b> The awakening screen had all of
    /// this inline, keyed to its own six element names, its own trigger and its own live figure -
    /// reusable in appearance and not in fact. What is shared is only the machinery: each screen
    /// still owns its steps, its pauses, when it appears and what its button does. This knows
    /// nothing about any of that.
    ///
    /// <b>Real time throughout.</b> Every wait is unscaled, so a screen that pauses the game behind
    /// itself - or a player pausing while one is up - cannot stall the message half-way.
    /// </summary>
    static class NarrativeScreen
    {
        /// <summary>
        /// One block, how long to hold before the next one, and optionally something to start the
        /// moment it appears.
        ///
        /// The pause belongs to the step it follows, so the two long silences a message needs are
        /// two values rather than a special case in the loop. <see cref="OnRevealed"/> exists for
        /// the same reason: the awakening's reserve begins falling with the line that reads it, and
        /// tying that to the step is more honest than a timer of its own that happens to agree.
        /// </summary>
        public readonly struct Step
        {
            public readonly VisualElement Element;
            public readonly float PauseAfter;
            public readonly Action OnRevealed;

            public Step(VisualElement element, float pauseAfter, Action onRevealed = null)
            {
                Element = element;
                PauseAfter = pauseAfter;
                OnRevealed = onRevealed;
            }
        }

        /// <summary>
        /// Fades one block in. The duration is applied to the element rather than written in the
        /// stylesheet so it stays an Inspector value the author can nudge while the game runs.
        /// </summary>
        public static void Reveal(VisualElement element, float fadeSeconds)
        {
            if (element == null) return;

            element.style.transitionDuration = new StyleList<TimeValue>(
                new List<TimeValue> { new TimeValue(fadeSeconds, TimeUnit.Second) });
            element.style.opacity = 1f;
        }

        /// <summary>
        /// Plays the steps in order, then calls <paramref name="onFinished"/> - which is where a
        /// screen puts its button up. A null step is skipped without consuming its pause, so a
        /// screen may hand over a block it did not build without special-casing it here.
        /// </summary>
        public static IEnumerator Play(IReadOnlyList<Step> steps, float fadeSeconds, Action onFinished)
        {
            if (steps != null)
            {
                for (var i = 0; i < steps.Count; i++)
                {
                    if (steps[i].Element == null) continue;

                    Reveal(steps[i].Element, fadeSeconds);
                    steps[i].OnRevealed?.Invoke();
                    yield return new WaitForSecondsRealtime(steps[i].PauseAfter);
                }
            }

            onFinished?.Invoke();
        }
    }
}

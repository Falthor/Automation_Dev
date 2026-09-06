using UnityEngine;
using UnityEngine.UIElements;

namespace Game.UI
{
    /// <summary>
    /// The "this is new, look here" pulse on a control the player has just been handed - today the
    /// Research menu, granted by the Core's first directive, on both the Top Bar card and the
    /// Bottom Nav icon at once.
    ///
    /// A blink rather than a permanent badge, because it has to be noticed on a screen the player
    /// is already reading; and it stops the moment they open what it points at, because a signal
    /// that never ends stops being one.
    ///
    /// Driven from Update rather than from USS: UI Toolkit has transitions but no keyframe
    /// animation, so there is nothing in a stylesheet that can blink on its own. On unscaled time,
    /// so it keeps pulsing while the game is paused - the player pausing to look around is exactly
    /// when a new control most needs to be pointed at.
    ///
    /// The tint itself never changes the border's width, only its colour, so a control does not
    /// twitch by a pixel every half second: each surface keeps whatever frame it already had.
    /// </summary>
    public static class NewUnlockPulse
    {
        /// <summary>One full on-off cycle. Slow enough to read as "look here" rather than as an alarm.</summary>
        const float PeriodSeconds = 1f;

        public const string ClassName = "newly-unlocked";

        /// <summary>Whether the pulse is on its lit half right now. Shared by every element pulsing at once, so they blink together instead of drifting apart.</summary>
        public static bool IsOn => IsLitAt(Time.unscaledTime);

        /// <summary>
        /// The lit half of the cycle at a given instant - the whole of the blink, as a function of
        /// time and nothing else.
        ///
        /// Split out from IsOn so the rhythm can be checked without a running player: read straight
        /// off Time, a single frame always answers the same thing, and a "test" of it proves only
        /// that the clock stood still.
        /// </summary>
        public static bool IsLitAt(float unscaledTime) => unscaledTime % PeriodSeconds < PeriodSeconds * 0.5f;

        /// <summary>Lights one element on the on-half of the cycle, and leaves it alone entirely when there is nothing new to announce.</summary>
        public static void Apply(VisualElement element, bool isNew)
        {
            element?.EnableInClassList(ClassName, isNew && IsOn);
        }
    }
}

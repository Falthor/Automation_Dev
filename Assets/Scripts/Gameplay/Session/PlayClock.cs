using UnityEngine;

namespace Game.Gameplay.Session
{
    /// <summary>
    /// How long this run has been played, in seconds of simulated time.
    ///
    /// <b>It knows nothing about pause, and must not.</b> Pause freezes the simulation by setting
    /// Time.timeScale to 0, which makes Time.deltaTime 0 - so a clock fed the same deltaTime as
    /// every other system stops with them and resumes exactly where it stopped, by construction
    /// rather than through a flag someone has to remember to check from a second place. That is the
    /// mirror image of CameraZoomController/CameraPanController, which read <b>unscaled</b> time
    /// precisely because where the player is looking is not part of the simulation being frozen.
    ///
    /// Advanced once per frame from GameRuntime's central tick, never from an Update of its own
    /// (PROJECT_ARCHITECTURE.md §17), so it counts the same time the factory does.
    /// </summary>
    public sealed class PlayClock
    {
        public float ElapsedSeconds { get; private set; }

        /// <summary>
        /// Negative or zero is ignored rather than subtracted. Zero is the ordinary paused frame,
        /// and a negative delta has no meaning for a run's duration - a clock that could be walked
        /// backwards would be a stopwatch nobody could trust.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (deltaTime <= 0f) return;
            ElapsedSeconds += deltaTime;
        }

        /// <summary>Restores a saved run's elapsed time. An absent value (a save from before the clock existed) starts the count at zero rather than refusing the save.</summary>
        public void Restore(float? elapsedSeconds) => ElapsedSeconds = Mathf.Max(0f, elapsedSeconds ?? 0f);

        /// <summary>
        /// mm:ss, growing to h:mm:ss past the first hour rather than letting the minutes run past 60
        /// - a run measured in "83:20" is a run nobody reads at a glance.
        ///
        /// Floored, never rounded: a chronometer that showed 00:01 half a second in would be
        /// claiming time that has not passed, and every reading would sit up to half a second ahead
        /// of the simulation it is timing.
        /// </summary>
        public static string Format(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int hours = total / 3600;
            int minutes = total / 60 % 60;
            int secs = total % 60;

            return hours > 0 ? $"{hours}:{minutes:00}:{secs:00}" : $"{minutes:00}:{secs:00}";
        }
    }
}

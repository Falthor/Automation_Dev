using System.Collections.Generic;

namespace Game.Gameplay.Notifications
{
    /// <summary>
    /// Generic notification queue (TASK_05_ROBOT_CONSTRUCTEUR.md §6): a bandeau on the left edge
    /// of the screen informs the player of an event they did not directly cause. Deliberately
    /// generic from the start - a robot unable to unload its cargo is the first caller, not the
    /// reason this exists; the building cap, a construction site missing materials, and later an
    /// expedition return or a nest discovery all post through the same Post(...) call. A
    /// notification never blocks interaction - it is display-only state, never consulted by any
    /// gameplay decision.
    /// </summary>
    public sealed class NotificationSystem
    {
        readonly List<(int id, NotificationSeverity severity, string message, float remaining, float durationSeconds, float? countdownRemaining)> _active =
            new List<(int, NotificationSeverity, string, float, float, float?)>();

        int _nextId;

        /// <summary>Every notification currently on screen, most recently posted first.</summary>
        public IReadOnlyList<Notification> Active
        {
            get
            {
                var result = new List<Notification>(_active.Count);
                for (int i = _active.Count - 1; i >= 0; i--)
                {
                    var entry = _active[i];
                    result.Add(new Notification(entry.id, entry.severity, entry.message, entry.remaining, entry.countdownRemaining));
                }
                return result;
            }
        }

        /// <summary>
        /// Posts a new notification. countdownSeconds is optional and purely informational for the
        /// UI (e.g. "20s before this cargo is lost") - NotificationSystem does not itself destroy
        /// anything when a countdown reaches zero; the caller owning that consequence (e.g.
        /// BuilderRobotRuntime) drives its own timer and simply mirrors it here for display.
        /// </summary>
        public int Post(NotificationSeverity severity, string message, float durationSeconds, float? countdownSeconds = null)
        {
            int id = _nextId++;
            _active.Add((id, severity, message, durationSeconds, durationSeconds, countdownSeconds));
            return id;
        }

        /// <summary>Updates a previously posted notification's countdown display (e.g. as a robot's stranded-cargo timer ticks down). No-op if the notification already expired.</summary>
        public void UpdateCountdown(int id, float countdownRemainingSeconds)
        {
            for (int i = 0; i < _active.Count; i++)
            {
                if (_active[i].id != id) continue;
                var entry = _active[i];
                entry.countdownRemaining = countdownRemainingSeconds;
                _active[i] = entry;
                return;
            }
        }

        /// <summary>Dismisses a notification immediately (e.g. the situation it described resolved before its display duration elapsed).</summary>
        public void Dismiss(int id)
        {
            _active.RemoveAll(entry => entry.id == id);
        }

        public void Tick(float deltaTime)
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var entry = _active[i];
                entry.remaining -= deltaTime;
                if (entry.remaining <= 0f)
                {
                    _active.RemoveAt(i);
                }
                else
                {
                    _active[i] = entry;
                }
            }
        }
    }
}

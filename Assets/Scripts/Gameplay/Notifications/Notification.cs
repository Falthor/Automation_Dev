using System;

namespace Game.Gameplay.Notifications
{
    /// <summary>Severity of a notification - purely presentational (color/icon), no gameplay meaning.</summary>
    public enum NotificationSeverity
    {
        Info,
        Warning,
        Critical
    }

    /// <summary>
    /// One notification instance: a message with a severity, a display duration, and an optional
    /// countdown (e.g. seconds remaining before a robot's stranded cargo is destroyed). Immutable
    /// snapshot handed to the UI by NotificationSystem.Active - the UI never mutates one directly.
    /// </summary>
    public readonly struct Notification
    {
        public int Id { get; }
        public NotificationSeverity Severity { get; }
        public string Message { get; }
        public float RemainingSeconds { get; }
        public float? CountdownRemainingSeconds { get; }

        /// <summary>
        /// What clicking this notification does, or null for one that only informs.
        ///
        /// <b>A plain delegate, supplied by whoever posted</b> - which is what keeps an event that
        /// leads to an action from dragging presentation into the gameplay assembly. A notification
        /// about a robot wants to take the player to that robot; only the poster knows what a camera
        /// or a selection is, and it closes over them.
        /// </summary>
        public Action OnActivated { get; }

        /// <summary>Whether there is anywhere to go. The banner makes only these rows pickable, so an informational one still blocks nothing.</summary>
        public bool IsActionable => OnActivated != null;

        public Notification(int id, NotificationSeverity severity, string message, float remainingSeconds,
            float? countdownRemainingSeconds, Action onActivated = null)
        {
            Id = id;
            Severity = severity;
            Message = message;
            RemainingSeconds = remainingSeconds;
            CountdownRemainingSeconds = countdownRemainingSeconds;
            OnActivated = onActivated;
        }
    }
}

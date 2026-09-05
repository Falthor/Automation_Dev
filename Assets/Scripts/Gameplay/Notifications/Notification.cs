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

        public Notification(int id, NotificationSeverity severity, string message, float remainingSeconds, float? countdownRemainingSeconds)
        {
            Id = id;
            Severity = severity;
            Message = message;
            RemainingSeconds = remainingSeconds;
            CountdownRemainingSeconds = countdownRemainingSeconds;
        }
    }
}

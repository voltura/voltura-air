namespace VolturaAir.Host;

internal static class ActivitySimulationNotifications
{
    public static void PublishStateChanged(
        object sender,
        EventHandler? observers,
        IAppLogWriter appLog)
    {
        if (observers is null)
        {
            return;
        }

        foreach (EventHandler observer in observers.GetInvocationList())
        {
            try
            {
                observer(sender, EventArgs.Empty);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                WriteFailure(appLog, "state_notification_failed", exception);
            }
        }
    }

    public static void PublishFailureStreakStarted(
        object sender,
        EventHandler<ActivitySimulationFailureEventArgs>? observers,
        ActivitySimulationFailureEventArgs eventArgs,
        IAppLogWriter appLog)
    {
        if (observers is null)
        {
            return;
        }

        foreach (EventHandler<ActivitySimulationFailureEventArgs> observer in observers.GetInvocationList())
        {
            try
            {
                observer(sender, eventArgs);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                WriteFailure(appLog, "failure_notification_failed", exception);
            }
        }
    }

    private static void WriteFailure(IAppLogWriter appLog, string outcome, Exception exception) =>
        appLog.Write(new AppLogEntry(
            Event: "activity_simulation",
            Source: "windows_host",
            Action: "f15_key_up",
            Outcome: outcome,
            Detail: exception.Message));
}

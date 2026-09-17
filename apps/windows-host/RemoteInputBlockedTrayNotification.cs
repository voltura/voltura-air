namespace VolturaAir.Host;

internal static class RemoteInputBlockedTrayNotification
{
    internal const string Title = "PC input paused";
    internal const string Message = "An administrator app is active. Switch to another window or choose Show desktop in the web app.";

    internal static bool ShouldShow(bool isBlocked, bool hasActiveController)
    {
        return isBlocked && hasActiveController;
    }
}

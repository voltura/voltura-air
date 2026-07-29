namespace VolturaAir.Host.Features.CustomScreens;

internal sealed class CustomScreenEditorActivityLog(IAppLogWriter appLog)
{
    public void Write(string action, bool succeeded, string? code = null)
    {
        appLog.Write(new AppLogEntry(
            Event: "host_action",
            Source: "windows_host",
            Action: $"custom_screen_{action}",
            Outcome: succeeded ? "succeeded" : "failed",
            Code: succeeded ? null : code ?? "operation-rejected"));
    }
}

using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenEditorActivityLogTests
{
    [Fact]
    public void WritesStructuredEditorOutcomesWithoutScreenContent()
    {
        var writer = new RecordingAppLogWriter();
        var activity = new CustomScreenEditorActivityLog(writer);

        activity.Write("save", succeeded: true);
        activity.Write("preview", succeeded: false, code: "launch-failed");

        Assert.Collection(
            writer.Entries,
            entry =>
            {
                Assert.Equal("host_action", entry.Event);
                Assert.Equal("windows_host", entry.Source);
                Assert.Equal("custom_screen_save", entry.Action);
                Assert.Equal("succeeded", entry.Outcome);
                Assert.Null(entry.Detail);
            },
            entry =>
            {
                Assert.Equal("custom_screen_preview", entry.Action);
                Assert.Equal("failed", entry.Outcome);
                Assert.Equal("launch-failed", entry.Code);
                Assert.Null(entry.Detail);
            });
    }

    private sealed class RecordingAppLogWriter : IAppLogWriter
    {
        public List<AppLogEntry> Entries { get; } = [];

        public void Write(AppLogEntry entry) => Entries.Add(entry);
    }
}

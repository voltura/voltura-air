namespace VolturaAir.Host.Features.Connect;

internal sealed class IntroductionController
{
    internal const string ValueName = "Introduction";
    private IntroductionRecord _record;
    private bool _persistenceFailed;
    private bool _replaying;

    public IntroductionController(bool hasRememberedDevices)
    {
        _record = HostSettingsJsonValue.Load(ValueName,
            new IntroductionRecord(1, hasRememberedDevices ? "suppressed" : "eligible"),
            new IntroductionRecord(1, "suppressed"),
            static value => value.Version == 1 && value.State is "eligible" or "pending" or "suppressed");
        // Establish eligibility before the first device can be remembered.
        Persist();
    }

    public bool IsVisible => _replaying || _record.State == "pending";

    public void DeviceConnected()
    {
        if (_record.State != "eligible") return;
        _record = new IntroductionRecord(1, "pending");
        Persist();
    }

    public void Replay() => _replaying = true;

    public void Dismiss()
    {
        _replaying = false;
        if (_record.State == "suppressed") return;
        _record = new IntroductionRecord(1, "suppressed");
        Persist();
    }

    private void Persist()
    {
        if (_persistenceFailed) return;
        try { HostSettingsJsonValue.Save(ValueName, _record); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Keep the session state authoritative. Never retry on connection/UI loops.
            _persistenceFailed = true;
        }
    }
}

internal sealed record IntroductionRecord(int Version, string State);

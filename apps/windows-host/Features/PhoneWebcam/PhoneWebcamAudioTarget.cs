using NAudio.CoreAudioApi;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal enum PhoneWebcamAudioTargetState
{
    Ready,
    InstalledButUnavailable,
    NotInstalled,
    DetectionFailed
}

internal sealed record PhoneWebcamAudioTargetStatus(
    PhoneWebcamAudioTargetState State,
    string Message,
    string? EndpointId = null)
{
    internal bool IsReady => State == PhoneWebcamAudioTargetState.Ready && EndpointId is not null;
}

internal sealed record PhoneWebcamAudioEndpoint(string Id, bool IsActive, string FriendlyName, string DeviceFriendlyName);

internal interface IPhoneWebcamAudioTarget
{
    PhoneWebcamAudioTargetStatus Status { get; }
    event EventHandler? StatusChanged;
    PhoneWebcamAudioTargetStatus Refresh();
    PhoneWebcamAudioTargetStatus ReportDetectionFailure();
    void InvalidateRefresh();
    MMDevice OpenReadyEndpoint();
}

internal sealed class PhoneWebcamAudioTarget : IPhoneWebcamAudioTarget
{
    private const string CableEndpointPrefix = "CABLE Input";
    private const string CableDeviceName = "VB-Audio Virtual Cable";
    private readonly Lock _gate = new();
    private readonly Func<IReadOnlyList<PhoneWebcamAudioEndpoint>> _enumerate;
    private readonly Func<string, MMDevice> _openEndpoint;
    private long _refreshGeneration;
    private PhoneWebcamAudioTargetStatus _status = new(
        PhoneWebcamAudioTargetState.DetectionFailed,
        "Phone microphone support has not been checked.");

    internal PhoneWebcamAudioTarget() : this(EnumerateEndpoints, OpenEndpoint) { }

    internal PhoneWebcamAudioTarget(
        Func<IReadOnlyList<PhoneWebcamAudioEndpoint>> enumerate,
        Func<string, MMDevice>? openEndpoint = null)
    {
        _enumerate = enumerate;
        _openEndpoint = openEndpoint ?? OpenEndpoint;
    }

    public event EventHandler? StatusChanged;

    public PhoneWebcamAudioTargetStatus Status
    {
        get
        {
            lock (_gate) return _status;
        }
    }

    public PhoneWebcamAudioTargetStatus Refresh()
    {
        long generation;
        lock (_gate) generation = ++_refreshGeneration;
        PhoneWebcamAudioTargetStatus next;
        try
        {
            next = Classify(_enumerate());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            next = new PhoneWebcamAudioTargetStatus(
                PhoneWebcamAudioTargetState.DetectionFailed,
                "Voltura Air could not check VB-CABLE. Try again from the Phone webcam page.");
        }

        return PublishRefreshStatus(next, generation);
    }

    public MMDevice OpenReadyEndpoint()
    {
        PhoneWebcamAudioTargetStatus status = Refresh();
        if (!status.IsReady)
        {
            throw new InvalidOperationException("The VB-CABLE audio endpoint is unavailable.");
        }

        try
        {
            MMDevice endpoint = _openEndpoint(status.EndpointId!);
            if (endpoint.State != DeviceState.Active || !IsBaseCable(endpoint))
            {
                endpoint.Dispose();
                throw new InvalidOperationException("The VB-CABLE audio endpoint changed before it could be opened.");
            }
            return endpoint;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            PhoneWebcamAudioTargetStatus refreshed = Refresh();
            if (refreshed.IsReady)
            {
                PublishStatus(new PhoneWebcamAudioTargetStatus(
                    PhoneWebcamAudioTargetState.DetectionFailed,
                    "Voltura Air could not open VB-CABLE. Check the device and try again."));
            }
            throw new InvalidOperationException("The VB-CABLE audio endpoint could not be opened.", exception);
        }
    }

    public PhoneWebcamAudioTargetStatus ReportDetectionFailure()
    {
        var failed = new PhoneWebcamAudioTargetStatus(
            PhoneWebcamAudioTargetState.DetectionFailed,
            "Voltura Air could not check VB-CABLE. Try again from the Phone webcam page.");
        PublishStatus(failed);
        return failed;
    }

    public void InvalidateRefresh()
    {
        lock (_gate) _refreshGeneration++;
    }

    private void PublishStatus(PhoneWebcamAudioTargetStatus next)
    {
        bool changed;
        lock (_gate)
        {
            _refreshGeneration++;
            changed = _status != next;
            _status = next;
        }
        if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private PhoneWebcamAudioTargetStatus PublishRefreshStatus(PhoneWebcamAudioTargetStatus next, long generation)
    {
        bool changed;
        PhoneWebcamAudioTargetStatus current;
        lock (_gate)
        {
            if (generation != _refreshGeneration) return _status;
            changed = _status != next;
            _status = next;
            current = next;
        }
        if (changed) StatusChanged?.Invoke(this, EventArgs.Empty);
        return current;
    }

    private static MMDevice OpenEndpoint(string endpointId)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(endpointId);
    }

    private static bool IsBaseCable(MMDevice endpoint) =>
        endpoint.DataFlow == DataFlow.Render &&
        IsBaseCable(endpoint.FriendlyName, endpoint.DeviceFriendlyName);

    private static IReadOnlyList<PhoneWebcamAudioEndpoint> EnumerateEndpoints()
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDeviceCollection endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.All);
        var results = new List<PhoneWebcamAudioEndpoint>();
        foreach (MMDevice endpoint in endpoints)
        {
            using (endpoint)
            {
                results.Add(new PhoneWebcamAudioEndpoint(
                    endpoint.ID,
                    endpoint.State == DeviceState.Active,
                    endpoint.FriendlyName,
                    endpoint.DeviceFriendlyName));
            }
        }
        return results;
    }

    internal static PhoneWebcamAudioTargetStatus Classify(IEnumerable<PhoneWebcamAudioEndpoint> endpoints)
    {
        PhoneWebcamAudioEndpoint? active = endpoints.FirstOrDefault(endpoint =>
            endpoint.IsActive && IsBaseCable(endpoint.FriendlyName, endpoint.DeviceFriendlyName));
        if (active is not null)
        {
            return new PhoneWebcamAudioTargetStatus(
                PhoneWebcamAudioTargetState.Ready,
                "Phone microphone support is ready.",
                active.Id);
        }
        return endpoints.Any(endpoint => IsBaseCable(endpoint.FriendlyName, endpoint.DeviceFriendlyName))
            ? new PhoneWebcamAudioTargetStatus(
                PhoneWebcamAudioTargetState.InstalledButUnavailable,
                "VB-CABLE is installed, but its CABLE Input endpoint is unavailable. Enable it in Windows Sound settings or restart Windows.")
            : new PhoneWebcamAudioTargetStatus(
                PhoneWebcamAudioTargetState.NotInstalled,
                "VB-CABLE is not installed. It is optional third-party donationware and is not included with Voltura Air.");
    }

    internal static bool IsBaseCableIdentity(string friendlyName, string deviceFriendlyName) =>
        deviceFriendlyName.Contains(CableDeviceName, StringComparison.OrdinalIgnoreCase) ||
        friendlyName.StartsWith(CableEndpointPrefix, StringComparison.OrdinalIgnoreCase) &&
        friendlyName.Contains(CableDeviceName, StringComparison.OrdinalIgnoreCase);

    private static bool IsBaseCable(string friendlyName, string deviceFriendlyName) =>
        IsBaseCableIdentity(friendlyName, deviceFriendlyName);
}

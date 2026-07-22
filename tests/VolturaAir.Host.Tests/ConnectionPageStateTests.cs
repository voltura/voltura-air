using System.Net;
using System.Net.NetworkInformation;
using VolturaAir.Host.Features.Connection;

namespace VolturaAir.Host.Tests;

public sealed class ConnectionPageStateTests
{
    [Theory]
    [InlineData(true, true, "Automatic")]
    [InlineData(true, false, "Adapter: Automatic · Port: Custom")]
    [InlineData(false, true, "Adapter: Custom · Port: Automatic")]
    [InlineData(false, false, "Custom adapter and port")]
    public void ActiveSelectionModeSummarizesActualRuntimeChoices(
        bool adapterAutomatic,
        bool portAutomatic,
        string expected)
    {
        Assert.Equal(expected, ConnectionPagePresenter.GetActiveSelectionMode(adapterAutomatic, portAutomatic));
    }

    [Fact]
    public void AdapterSelectionAndCustomPortArePendingUntilDiscarded()
    {
        var automatic = AutomaticConfiguration();
        var candidate = CreateCandidate();
        var state = CreateState(automatic, automatic, [candidate]);

        state.SelectAdapter(candidate);
        state.ManualPortText = "51396";
        state.SetUseCustomPort(true);
        state.SetManualPort(51396);

        Assert.True(state.HasPendingChanges);
        Assert.Equal(NetworkSelectionMode.Manual, state.PendingConfiguration.NetworkMode);
        Assert.Equal(PortSelectionMode.Manual, state.PendingConfiguration.PortMode);
        Assert.Equal(51396, state.PendingConfiguration.ManualPort);

        state.DiscardPendingChanges();

        Assert.False(state.HasPendingChanges);
        Assert.Equal(automatic, state.PendingConfiguration);
    }

    [Fact]
    public void RefreshPreservesMissingPendingAdapterAndPreventsSavingIt()
    {
        var automatic = AutomaticConfiguration();
        var candidate = CreateCandidate();
        var state = CreateState(automatic, automatic, [candidate]);
        state.SelectAdapter(candidate);

        state.SetCandidates([]);

        Assert.True(state.HasPendingChanges);
        Assert.False(state.IsPendingAdapterAvailable);
        Assert.Null(state.PendingAdapter);
    }

    [Fact]
    public void FailedSaveRemainsRetryableWhenRegistryNowMatchesPendingValues()
    {
        var automatic = AutomaticConfiguration();
        var state = CreateState(automatic, automatic);
        state.ManualPortText = "51396";
        state.SetUseCustomPort(true);
        state.SetManualPort(51396);
        var pending = state.PendingConfiguration;

        state.ReconcileSavedAfterFailure(pending);

        Assert.True(state.HasPendingChanges);
        Assert.True(state.SaveRetryRequired);
    }

    [Fact]
    public void VirtualAdapterExplainsReachabilityConsequence()
    {
        var candidate = LanAddressSelector.CreateCandidate(
            IPAddress.Parse("192.168.192.1"),
            "vEthernet",
            "Hyper-V Virtual Ethernet Adapter",
            NetworkInterfaceType.Ethernet,
            hasIpv4Gateway: false);

        var item = Assert.Single(ConnectionCandidateItem.Create([candidate], candidate));

        Assert.Equal("Virtual adapter — unlikely to work with devices", item.Status);
        Assert.Contains(candidate.Address.ToString(), item.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void PendingChangeRowsShowOnlyChangedSettings()
    {
        var automatic = AutomaticConfiguration();
        var candidate = CreateCandidate();
        var state = CreateState(automatic, automatic, [candidate]);

        state.SelectAdapter(candidate);
        Assert.Equal("Automatic → Wi-Fi (Test adapter)", ConnectionPagePresenter.GetAdapterChange(state));

        state.DiscardPendingChanges();
        state.ManualPortText = "51396";
        state.SetUseCustomPort(true);
        state.SetManualPort(51396);
        Assert.Equal("Automatic → Custom · 51396", ConnectionPagePresenter.GetPortChange(state));

        state.SelectAdapter(candidate);
        Assert.Equal("Automatic → Wi-Fi (Test adapter)", ConnectionPagePresenter.GetAdapterChange(state));
        Assert.Equal("Automatic → Custom · 51396", ConnectionPagePresenter.GetPortChange(state));

        state.MarkSaved(state.PendingConfiguration);
        Assert.Equal("Restarting Voltura Air…", ConnectionPagePresenter.GetActionHeading(state));
        Assert.Equal(string.Empty, ConnectionPagePresenter.GetActionGuidance(state));

        state.MarkRestartRequestFailed();
        Assert.Equal("Restart required", ConnectionPagePresenter.GetActionHeading(state));
        Assert.Equal(
            "Connection settings are saved. Restart Voltura Air to apply them.",
            ConnectionPagePresenter.GetActionGuidance(state));
    }

    [Fact]
    public void PortHeaderDistinguishesActivePendingAndPendingRestartValues()
    {
        var automatic = AutomaticConfiguration();
        var custom = automatic with { PortMode = PortSelectionMode.Manual, ManualPort = 51396 };

        var activeAutomatic = CreateState(automatic, automatic);
        Assert.Equal("Automatic · 51395", ConnectionPagePresenter.GetPortHeaderStatus(activeAutomatic, activePortAutomatic: true));

        var activeCustom = CreateState(custom, custom);
        Assert.Equal("Custom · 51395", ConnectionPagePresenter.GetPortHeaderStatus(activeCustom, activePortAutomatic: false));

        activeAutomatic.ManualPortText = "52000";
        activeAutomatic.SetUseCustomPort(true);
        activeAutomatic.SetManualPort(52000);
        Assert.Equal("Pending: Custom · 52000", ConnectionPagePresenter.GetPortHeaderStatus(activeAutomatic, activePortAutomatic: true));

        activeCustom.SetUseCustomPort(false);
        Assert.Equal("Pending: Automatic", ConnectionPagePresenter.GetPortHeaderStatus(activeCustom, activePortAutomatic: false));

        var pendingRestart = new ConnectionPageState(
            automatic,
            custom,
            "Wi-Fi (Test adapter)",
            "192.168.1.20",
            51395,
            [],
            detectExistingRestartRequirement: true);
        Assert.Equal("Pending restart: Custom · 51396", ConnectionPagePresenter.GetPortHeaderStatus(pendingRestart, activePortAutomatic: true));
    }

    [Fact]
    public void DefaultSummarySuppressesOnlyNeutralMultipleAdapterGuidance()
    {
        Assert.Equal(
            string.Empty,
            ConnectionPagePresenter.GetDisplayedConnectionWarning(
                LanAddressSelector.MultipleAdaptersWarning,
                portWarning: null));
        Assert.Equal(
            $"Selected network may not be reachable.{Environment.NewLine}Port changed.",
            ConnectionPagePresenter.GetDisplayedConnectionWarning(
                "Selected network may not be reachable.",
                "Port changed."));
    }

    private static ConnectionConfiguration AutomaticConfiguration() => new(
        NetworkSelectionMode.Automatic,
        ManualHostAddress: null,
        ManualAdapterId: null,
        ManualAdapterName: null,
        PortSelectionMode.Automatic,
        ManualPort: null);

    private static ConnectionPageState CreateState(
        ConnectionConfiguration active,
        ConnectionConfiguration saved,
        IReadOnlyList<LanAddressCandidate>? candidates = null) => new(
            active,
            saved,
            "Wi-Fi (Test adapter)",
            "192.168.1.20",
            51395,
            candidates ?? []);

    private static LanAddressCandidate CreateCandidate() => LanAddressSelector.CreateCandidate(
        IPAddress.Parse("192.168.1.20"),
        "Wi-Fi",
        "Test adapter",
        NetworkInterfaceType.Wireless80211,
        hasIpv4Gateway: true,
        adapterId: "adapter-id");
}

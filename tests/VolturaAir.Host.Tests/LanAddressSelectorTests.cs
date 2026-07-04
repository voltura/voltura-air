using System.Net;
using System.Net.NetworkInformation;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class LanAddressSelectorTests
{
    [Fact]
    public void WifiWithGatewayBeatsVpnWithoutGateway()
    {
        var candidates = new[]
        {
            Candidate("10.46.224.101", "Local Area Connection", "Private Internet Access Network Adapter", NetworkInterfaceType.Ethernet, hasGateway: false),
            Candidate("192.168.1.177", "WiFi 2", "Intel(R) Wi-Fi 6E AX210 160MHz", NetworkInterfaceType.Wireless80211, hasGateway: true)
        };

        var result = LanAddressSelector.Select(candidates, AutomaticSettings());

        Assert.NotNull(result);
        Assert.Equal(IPAddress.Parse("192.168.1.177"), result.Address);
    }

    [Fact]
    public void RealTenDotLanCanWin()
    {
        var candidates = new[]
        {
            Candidate("10.0.0.25", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
        };

        var result = LanAddressSelector.Select(candidates, AutomaticSettings());

        Assert.NotNull(result);
        Assert.Equal(IPAddress.Parse("10.0.0.25"), result.Address);
    }

    [Fact]
    public void LoopbackAndPublicAddressesAreNotPrivateLanAddresses()
    {
        Assert.False(LanAddressSelector.IsPrivateIpv4Address(IPAddress.Parse("127.0.0.1")));
        Assert.False(LanAddressSelector.IsPrivateIpv4Address(IPAddress.Parse("8.8.8.8")));
        Assert.True(LanAddressSelector.IsPrivateIpv4Address(IPAddress.Parse("192.168.1.177")));
        Assert.True(LanAddressSelector.IsPrivateIpv4Address(IPAddress.Parse("172.16.4.22")));
        Assert.True(LanAddressSelector.IsPrivateIpv4Address(IPAddress.Parse("10.0.0.25")));
    }

    [Fact]
    public void NoCandidatesReturnsNoSelectionForCallerFallback()
    {
        var result = LanAddressSelector.Select(Array.Empty<LanAddressCandidate>(), AutomaticSettings());

        Assert.Null(result);
    }

    [Fact]
    public void AutomaticModeReusesLastAdvertisedIpWhenStillAvailable()
    {
        var candidates = new[]
        {
            Candidate("192.168.1.177", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true),
            Candidate("192.168.1.52", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
        };
        var settings = AutomaticSettings() with { LastAutomaticHostAddress = "192.168.1.52" };

        var result = LanAddressSelector.Select(candidates, settings);

        Assert.NotNull(result);
        Assert.Equal(IPAddress.Parse("192.168.1.52"), result.Address);
    }

    [Fact]
    public void MissingManualIpFallsBackToAutomaticWithWarning()
    {
        var candidates = new[]
        {
            Candidate("192.168.1.177", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true)
        };
        var settings = AutomaticSettings() with
        {
            NetworkMode = NetworkSelectionMode.Manual,
            ManualHostAddress = "192.168.1.90"
        };

        var result = LanAddressSelector.Select(candidates, settings);

        Assert.NotNull(result);
        Assert.False(result.UsedManualAddress);
        Assert.Equal(IPAddress.Parse("192.168.1.177"), result.Address);
        Assert.Contains("not currently available", result.Warning);
    }

    private static NetworkSettingsSnapshot AutomaticSettings()
    {
        return new NetworkSettingsSnapshot(
            NetworkSelectionMode.Automatic,
            ManualHostAddress: null,
            ManualAdapterId: null,
            ManualAdapterName: null,
            PortSelectionMode.Automatic,
            ManualPort: null,
            LastAutomaticPort: null,
            LastAutomaticHostAddress: null);
    }

    private static LanAddressCandidate Candidate(
        string address,
        string adapterName,
        string adapterDescription,
        NetworkInterfaceType adapterType,
        bool hasGateway)
    {
        return LanAddressSelector.CreateCandidate(
            IPAddress.Parse(address),
            adapterName,
            adapterDescription,
            adapterType,
            hasGateway);
    }
}

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
            Candidate("10.46.224.101", "vpn-1", "Local Area Connection", "Private Internet Access Network Adapter", NetworkInterfaceType.Ethernet, hasGateway: false),
            Candidate("192.168.1.177", "wifi-1", "WiFi 2", "Intel(R) Wi-Fi 6E AX210 160MHz", NetworkInterfaceType.Wireless80211, hasGateway: true)
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
            Candidate("10.0.0.25", "eth-1", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
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
            Candidate("192.168.1.177", "wifi-1", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true),
            Candidate("192.168.1.52", "eth-1", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
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
            Candidate("192.168.1.177", "wifi-1", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true)
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

    [Fact]
    public void ManualModeSelectsSavedAdapterIdEvenWhenAnotherAdapterHasBetterScore()
    {
        var candidates = new[]
        {
            Candidate("192.168.1.177", "wifi-1", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: false),
            Candidate("192.168.1.52", "eth-1", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
        };
        var settings = AutomaticSettings() with
        {
            NetworkMode = NetworkSelectionMode.Manual,
            ManualAdapterId = "wifi-1",
            ManualHostAddress = "192.168.1.177"
        };

        var result = LanAddressSelector.Select(candidates, settings);

        Assert.NotNull(result);
        Assert.True(result.UsedManualAddress);
        Assert.Equal(IPAddress.Parse("192.168.1.177"), result.Address);
        Assert.Equal("wifi-1", result.Candidate?.AdapterId);
    }

    [Fact]
    public void ManualModeReselectsSavedAdapterWhenDhcpAddressChanged()
    {
        var candidates = new[]
        {
            Candidate("192.168.1.201", "wifi-1", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true),
            Candidate("192.168.1.52", "eth-1", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
        };
        var settings = AutomaticSettings() with
        {
            NetworkMode = NetworkSelectionMode.Manual,
            ManualAdapterId = "wifi-1",
            ManualHostAddress = "192.168.1.177"
        };

        var result = LanAddressSelector.Select(candidates, settings);

        Assert.NotNull(result);
        Assert.True(result.UsedManualAddress);
        Assert.Equal(IPAddress.Parse("192.168.1.201"), result.Address);
        Assert.Equal("wifi-1", result.Candidate?.AdapterId);
        Assert.Contains("different IP address", result.Warning);
    }

    [Fact]
    public void MissingManualAdapterFallsBackToAutomaticWithWarning()
    {
        var candidates = new[]
        {
            Candidate("192.168.1.177", "wifi-1", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true)
        };
        var settings = AutomaticSettings() with
        {
            NetworkMode = NetworkSelectionMode.Manual,
            ManualAdapterId = "missing-adapter",
            ManualHostAddress = "192.168.1.90"
        };

        var result = LanAddressSelector.Select(candidates, settings);

        Assert.NotNull(result);
        Assert.False(result.UsedManualAddress);
        Assert.Equal(IPAddress.Parse("192.168.1.177"), result.Address);
        Assert.Contains("adapter is not currently available", result.Warning);
    }

    [Fact]
    public void AutomaticModeWarnsWhenMultipleAdaptersExist()
    {
        var candidates = new[]
        {
            Candidate("192.168.1.177", "wifi-1", "WiFi", "Intel Wi-Fi", NetworkInterfaceType.Wireless80211, hasGateway: true),
            Candidate("192.168.1.52", "eth-1", "Ethernet", "Realtek PCIe GbE", NetworkInterfaceType.Ethernet, hasGateway: true)
        };

        var result = LanAddressSelector.Select(candidates, AutomaticSettings());

        Assert.NotNull(result);
        Assert.Contains("Multiple network adapters", result.Warning);
    }

    [Theory]
    [InlineData("vpn-1", "VPN", "Private Internet Access Network Adapter")]
    [InlineData("virtual-1", "Virtual LAN", "Virtual Network Adapter")]
    public void SelectedVpnOrVirtualAdapterGetsWarning(string adapterId, string adapterName, string adapterDescription)
    {
        var candidates = new[]
        {
            Candidate("10.46.224.101", adapterId, adapterName, adapterDescription, NetworkInterfaceType.Ethernet, hasGateway: true)
        };

        var result = LanAddressSelector.Select(candidates, AutomaticSettings());

        Assert.NotNull(result);
        Assert.Equal(IPAddress.Parse("10.46.224.101"), result.Address);
        Assert.Contains("VPN or virtual", result.Warning);
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
        string adapterId,
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
            hasGateway,
            adapterId);
    }
}

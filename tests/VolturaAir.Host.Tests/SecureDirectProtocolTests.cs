using System.Text;
using System.Net;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class SecureDirectProtocolTests
{
    [Theory]
    [InlineData(false, true, "192.168.68.51", true)]
    [InlineData(false, true, "127.0.0.1", false)]
    [InlineData(false, true, "8.8.8.8", false)]
    [InlineData(false, true, "::1", false)]
    [InlineData(false, false, "192.168.68.51", false)]
    [InlineData(true, true, "192.168.68.51", false)]
    public void HostComposesSecureDirectOnlyForAnEnabledPrivateIpv4Address(
        bool isolatedTestMode,
        bool enhancedCapabilitiesEnabled,
        string advertisedHostAddress,
        bool expected)
    {
        Assert.Equal(
            expected,
            WebHostService.SelectSecureDirectBindAddress(
                isolatedTestMode,
                enhancedCapabilitiesEnabled,
                advertisedHostAddress) is not null);
    }

    [Theory]
    [InlineData("invalid-address")]
    [InlineData("native")]
    [InlineData("missing-dll")]
    [InlineData("missing-entry-point")]
    [InlineData("bad-image")]
    public void NativePeerStartupFailureRejectsOnlyTheSession(string failure)
    {
        var failures = 0;
        var sessions = new SecureDirectSessions(
            IPAddress.Parse("192.168.1.10"),
            (_, _, _, _) => Task.CompletedTask,
            (_, _) => Task.CompletedTask,
            () => failures++,
            _ => { },
            _ => throw CreateNativeStartupException(failure));

        var started = sessions.TryStart(Guid.NewGuid(), new byte[16], CancellationToken.None);

        Assert.False(started);
        Assert.Equal(1, failures);
    }

    private static Exception CreateNativeStartupException(string failure) => failure switch
    {
        "invalid-address" => new ArgumentException("Secure Direct requires a private IPv4 bind address."),
        "native" => new InvalidOperationException("native startup failed"),
        "missing-dll" => new DllNotFoundException("datachannel.dll"),
        "missing-entry-point" => new EntryPointNotFoundException("rtcCreatePeerConnection"),
        "bad-image" => new BadImageFormatException("datachannel.dll"),
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };

    [Fact]
    public void ParsesOnlyExactBoundedAnswerShape()
    {
        Assert.True(SecureDirectSessions.TryParseDescription(
            Encoding.UTF8.GetBytes("{\"type\":\"secure.answer\",\"sdp\":\"v=0\\r\\n\"}"),
            "secure.answer",
            out var sdp));
        Assert.Equal("v=0\r\n", sdp);
        Assert.False(SecureDirectSessions.TryParseDescription(
            Encoding.UTF8.GetBytes("{\"type\":\"secure.answer\",\"sdp\":\"v=0\",\"extra\":true}"),
            "secure.answer",
            out _));
    }
}

using System.Text;
using VolturaAir.Host.Features.Apps;

namespace VolturaAir.Host.Tests;

public sealed class AppsProtocolTests
{
    private const string Revision = "0123456789abcdef0123456789abcdef";
    private const string WindowId = "fedcba9876543210fedcba9876543210";

    [Fact]
    public void PreviewRequestAcceptsOnlyExactBoundedOpaqueIdentifiers()
    {
        byte[] request = new byte[66];
        request[0] = 0x11;
        Encoding.ASCII.GetBytes(Revision, request.AsSpan(1, 32));
        request[33] = 1;
        Encoding.ASCII.GetBytes(WindowId, request.AsSpan(34, 32));

        Assert.True(AppsProtocol.TryParseRequest(request, out string revision, out string[] windowIds));
        Assert.Equal(Revision, revision);
        Assert.Equal([WindowId], windowIds);

        Assert.False(AppsProtocol.TryParseRequest(request.AsMemory(0, request.Length - 1), out _, out _));
        request[0] = 0x21;
        Assert.False(AppsProtocol.TryParseRequest(request, out _, out _));

        request[0] = 0x11;
        request[1] = (byte)'A';
        Assert.False(AppsProtocol.TryParseRequest(request, out _, out _));
    }

    [Fact]
    public void PreviewRecordsRejectInvalidBoundsAndNativeIdentifiers()
    {
        Assert.Equal(43, AppsProtocol.CreatePreviewHeader(WindowId, 1024, 640, 1024).Length);
        Assert.Equal(37 + 3, AppsProtocol.CreatePreviewData(WindowId, 0, [1, 2, 3]).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => AppsProtocol.CreatePreviewHeader("42", 100, 100, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AppsProtocol.CreatePreviewData(WindowId, 0, new byte[AppsProtocol.PreviewChunkBytes + 1]));
    }

    [Theory]
    [InlineData(false, true, false, false, true, false, true, true, false, true, "Title", "App", false)]
    [InlineData(true, true, true, false, true, false, true, true, false, true, "Title", "App", false)]
    [InlineData(true, true, false, false, true, false, true, true, true, false, "Title", "Voltura Air", false)]
    [InlineData(true, false, false, false, true, false, true, true, false, true, "Hidden helper", "System", false)]
    [InlineData(true, true, false, false, true, false, true, true, false, true, "Title", "App", true)]
    [InlineData(true, true, false, true, false, false, true, true, false, true, "Owned app window", "App", true)]
    [InlineData(true, true, false, false, false, false, true, true, false, true, "Inactive popup", "App", false)]
    public void WindowFilterExcludesNonApplicationAndHostWindows(
        bool isWindow,
        bool isVisible,
        bool isToolWindow,
        bool isAppWindow,
        bool isRootOwnerPopup,
        bool isCloaked,
        bool isCurrentSession,
        bool isCurrentDesktop,
        bool isVolturaAir,
        bool includeVolturaAir,
        string title,
        string applicationName,
        bool expected)
    {
        Assert.Equal(expected, WindowsAppsWindowAdapter.ShouldIncludeCandidate(
            isWindow,
            isVisible,
            isToolWindow,
            isAppWindow,
            isRootOwnerPopup,
            isCloaked,
            isCurrentSession,
            isCurrentDesktop,
            isVolturaAir,
            includeVolturaAir,
            title,
            applicationName));
    }

    [Fact]
    public void PopupResolutionStopsWhenWindowsReturnsTheSameHiddenPopup()
    {
        nint rootOwner = 1;
        nint hiddenPopup = 2;
        var inspected = new List<nint>();

        nint result = WindowsAppsWindowAdapter.ResolveRootOwnerPopup(
            rootOwner,
            window =>
            {
                inspected.Add(window);
                return window == rootOwner ? hiddenPopup : window;
            },
            _ => false);

        Assert.Equal(nint.Zero, result);
        Assert.Equal([rootOwner, hiddenPopup], inspected);
    }

    [Fact]
    public void PopupResolutionReturnsTheVisibleLastActivePopup()
    {
        nint result = WindowsAppsWindowAdapter.ResolveRootOwnerPopup(
            1,
            _ => 2,
            window => window == 2);

        Assert.Equal((nint)2, result);
    }

    [Fact]
    public void User32MessageImportsBindTheUnicodeEntryPoints()
    {
        Exception? exception = Record.Exception(() =>
        {
            _ = AppsWindowNativeMethods.PostMessage(nint.Zero, 0, nint.Zero, nint.Zero);
            _ = AppsWindowNativeMethods.PrintWindow(nint.Zero, nint.Zero, 0);
        });

        Assert.Null(exception);
    }
}

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;
using VolturaAir.Host.Features.CustomScreens;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class CustomScreenBrowserPreviewTests : WebHostServiceTestBase
{
    [Fact]
    public void LauncherUsesLoopbackAndTheOpaqueSavedScreenId()
    {
        var windows = new RecordingPreviewWindowLauncher();
        var launcher = new CustomScreenBrowserPreviewLauncher(51395, windows);

        var result = launcher.Open(
            "screen.preview-1",
            new CustomScreenViewport(800, 1180, "portrait"),
            controlDepth: true);

        Assert.True(result.Succeeded);
        var request = Assert.Single(windows.Opened);
        Assert.Equal("127.0.0.1", request.Uri.Host);
        Assert.Equal(51395, request.Uri.Port);
        Assert.Equal(
            "?customScreenPreview=screen.preview-1&controlDepth=true",
            request.Uri.Query);
        Assert.Equal(
            new CustomScreenViewport(800, 1180, "portrait"),
            request.Viewport);
        Assert.InRange(request.Width, 1, 800);
        Assert.InRange(request.Height, 1, 1180);
        Assert.InRange(
            Math.Abs((double)request.Width / request.Height - 800d / 1180d),
            0,
            0.002);

        launcher.CloseAll();
        Assert.Equal(1, windows.CloseAllCount);
    }

    [Fact]
    public void LauncherIncludesPairedDevicesForListPreview()
    {
        using var pairingStore = new TempPairingStore();
        using var key = new PairingTestKey();
        pairingStore.Store.Save(
        [
            new PairingRecord(
                "client-mobile",
                key.PublicKey,
                "Mobile device",
                ControlDepthOverride: true,
                CustomScreenViewport:
                    new CustomScreenViewport(393, 852, "portrait"))
        ]);
        var windows = new RecordingPreviewWindowLauncher();
        var launcher = new CustomScreenBrowserPreviewLauncher(
            51395,
            windows,
            new PairingManager(pairingStore.Store));

        var result = launcher.Open(
            "screen.preview-list",
            controlDepth: false);

        Assert.True(result.Succeeded);
        var request = Assert.Single(windows.Opened);
        Assert.Null(request.SelectedDeviceId);
        var device = Assert.Single(request.Devices);
        Assert.Equal("client-mobile", device.ClientId);
        Assert.Equal("Mobile device", device.Name);
        Assert.Equal(
            new CustomScreenViewport(393, 852, "portrait"),
            device.Viewport);
        Assert.True(device.ControlDepth);
    }

    [Fact]
    public void OversizedPreviewPreservesTemplateAspectRatioWithinTheWorkArea()
    {
        var size = CustomScreenBrowserPreviewLauncher.FitToWorkArea(
            2400,
            1200,
            1600,
            900);

        Assert.Equal(1552, size.Width);
        Assert.Equal(776, size.Height);
    }

    [Fact]
    public async Task LoopbackPreviewReturnsOnlyTheSavedVisualDefinition()
    {
        using var pairingStore = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var pairingManager = new PairingManager(pairingStore.Store);
        var screens = new CustomScreenService(
            new InMemoryCustomScreenStore(),
            new global::VolturaAir.Host.Tests.FakeAppLaunchService());
        var draft = CustomScreenService.CreateDraft();
        var sourceSection = draft.Sections[0];
        draft = draft with
        {
            ShowNavigationHeader = false,
            Sections =
            [
                sourceSection with
                {
                    Buttons =
                    [
                        sourceSection.Buttons[0] with
                        {
                            Presentation = "label",
                            Action = new CustomScreenAction(
                                "text",
                                Text: "private preview payload")
                        }
                    ]
                }
            ]
        };
        Assert.True(screens.TrySave(draft, out var saved, out var saveError), saveError);
        await using var webHost = new WebHostService(
            pairingManager,
            new InputDispatcher(inputInjector),
            customScreenService: screens,
            isolatedTestMode: true,
            configureWebHost: builder => builder.UseTestServer());
        await webHost.StartAsync();

        var response = await webHost.Application!.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = $"/api/custom-screens/preview/{saved.Id}";
        });
        using var reader = new StreamReader(response.Response.Body);
        var json = await reader.ReadToEndAsync();

        Assert.Equal(StatusCodes.Status200OK, response.Response.StatusCode);
        Assert.Equal("no-store", response.Response.Headers.CacheControl);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement
            .GetProperty("screen")
            .GetProperty("showNavigationHeader")
            .GetBoolean());
        Assert.DoesNotContain("private preview payload", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PreviewDefinitionIsNotAvailableToLanRequests()
    {
        using var pairingStore = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var pairingManager = new PairingManager(pairingStore.Store);
        var screens = new CustomScreenService(
            new InMemoryCustomScreenStore(),
            new global::VolturaAir.Host.Tests.FakeAppLaunchService());
        Assert.True(screens.TrySave(
            CustomScreenService.CreateDraft(),
            out var saved,
            out var saveError), saveError);
        await using var webHost = new WebHostService(
            pairingManager,
            new InputDispatcher(inputInjector),
            customScreenService: screens,
            isolatedTestMode: true,
            configureWebHost: builder => builder.UseTestServer());
        await webHost.StartAsync();

        var response = await webHost.Application!.GetTestServer().SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.68.80");
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = $"/api/custom-screens/preview/{saved.Id}";
        });

        Assert.Equal(StatusCodes.Status404NotFound, response.Response.StatusCode);
    }

    private sealed class RecordingPreviewWindowLauncher :
        ICustomScreenPreviewWindowLauncher
    {
        public List<CustomScreenPreviewWindowRequest> Opened { get; } = [];

        public int CloseAllCount { get; private set; }

        public void Open(CustomScreenPreviewWindowRequest request) =>
            Opened.Add(request);

        public void CloseAll() => CloseAllCount++;
    }
}

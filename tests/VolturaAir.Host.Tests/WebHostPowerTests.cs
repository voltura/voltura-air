using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPowerTests
{
    [Fact]
    public async Task AppliesPermissionGatedBasicAwakeControlWithoutChangingHostScreenSetting()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var awake = new NoOpAwakeService(new AwakeState(AwakeMode.Off, true, 60, null));

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowAwakeControl = true });
            await using var fixture = await PowerHostFixture.StartAsync(new FakeSystemPowerController(), awakeService: awake);
            using var socket = await fixture.ConnectAsync();
            var paired = await fixture.PairAsync(socket);

            var capability = paired.GetProperty("capabilities").GetProperty("awake");
            Assert.True(capability.GetProperty("canControl").GetBoolean());
            Assert.False(capability.GetProperty("active").GetBoolean());

            await SendAsync(socket, new { type = "awake.set", enabled = true });
            var result = await ReceiveTypeAsync(socket, "awake.result");

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(AwakeMode.Indefinite, awake.State.Mode);
            Assert.True(awake.State.KeepScreenOn);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task RejectsAwakeControlWhenHostPermissionIsOff()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var awake = new NoOpAwakeService();

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowAwakeControl = false });
            await using var fixture = await PowerHostFixture.StartAsync(new FakeSystemPowerController(), awakeService: awake);
            using var socket = await fixture.ConnectAsync();
            var paired = await fixture.PairAsync(socket);

            Assert.False(paired.GetProperty("capabilities").GetProperty("awake").GetProperty("canControl").GetBoolean());
            var result = await SendAndReceiveAsync(socket, new { type = "awake.set", enabled = true });

            Assert.Equal("VAIR-AWAKE-DENIED", result.GetProperty("code").GetString());
            Assert.Equal(AwakeMode.Off, awake.State.Mode);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ExecutesOnlyExplicitlyAllowedPowerActions()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var powerActions = new FakeSystemPowerController();

        try
        {
            AppPermissionSettings.Save(originalPermissions with
            {
                AllowPcLock = true,
                AllowBlackoutDisplay = true,
                AllowDisplayOff = true,
                AllowScreenSaver = true,
                AllowSignOut = true,
                AllowRestart = true,
                AllowShutdown = true
            });
            await using var fixture = await PowerHostFixture.StartAsync(powerActions);
            using var socket = await fixture.ConnectAsync();
            var paired = await fixture.PairAsync(socket);

            foreach (var action in new[] { "lock", "blackoutDisplay", "displayOff", "screenSaver", "signOut", "restart", "shutdown" })
            {
                var result = await SendAndReceiveAsync(socket, new { type = "system.power", action });
                Assert.Equal("system.power.result", result.GetProperty("type").GetString());
                Assert.True(result.GetProperty("succeeded").GetBoolean());
            }

            var capabilities = paired.GetProperty("capabilities").GetProperty("power");
            Assert.True(capabilities.GetProperty("lock").GetBoolean());
            Assert.Equal("notExplicitlyDisabled", capabilities.GetProperty("lockAvailability").GetString());
            Assert.True(capabilities.GetProperty("blackoutDisplay").GetBoolean());
            Assert.True(capabilities.GetProperty("displayOff").GetBoolean());
            Assert.True(capabilities.GetProperty("screenSaver").GetBoolean());
            Assert.True(capabilities.GetProperty("screenSaverAvailable").GetBoolean());
            Assert.True(capabilities.GetProperty("signOut").GetBoolean());
            Assert.True(capabilities.GetProperty("restart").GetBoolean());
            Assert.True(capabilities.GetProperty("shutdown").GetBoolean());
            Assert.Equal(new[] { "lock", "blackoutDisplay", "displayOff", "screenSaver", "signOut", "restart", "shutdown" }, powerActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReportsAndBlocksDisabledPowerActions()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var powerActions = new FakeSystemPowerController();

        try
        {
            AppPermissionSettings.Save(originalPermissions with
            {
                AllowPcLock = false,
                AllowBlackoutDisplay = false,
                AllowDisplayOff = false,
                AllowScreenSaver = false,
                AllowSignOut = false,
                AllowRestart = false,
                AllowShutdown = false
            });
            await using var fixture = await PowerHostFixture.StartAsync(powerActions);
            using var socket = await fixture.ConnectAsync();
            var paired = await fixture.PairAsync(socket);

            var lockResult = await SendAndReceiveAsync(socket, new { type = "system.power", action = "lock" });
            var shutdownResult = await SendAndReceiveAsync(socket, new { type = "system.power", action = "shutdown" });

            var capabilities = paired.GetProperty("capabilities").GetProperty("power");
            Assert.False(capabilities.GetProperty("lock").GetBoolean());
            Assert.False(capabilities.GetProperty("shutdown").GetBoolean());
            Assert.Equal("VAIR-POWER-DENIED", lockResult.GetProperty("code").GetString());
            Assert.Equal("VAIR-POWER-DENIED", shutdownResult.GetProperty("code").GetString());
            Assert.Empty(powerActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReportsUnsupportedPowerActionsWithoutClosingTheSocket()
    {
        await using var fixture = await PowerHostFixture.StartAsync(new FakeSystemPowerController());
        using var socket = await fixture.ConnectAsync();
        await fixture.PairAsync(socket);

        var result = await SendAndReceiveAsync(socket, new { type = "system.power", action = "hibernate" });
        var status = await SendAndReceiveAsync(socket, new { type = "status.get" });

        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("VAIR-POWER-UNSUPPORTED", result.GetProperty("code").GetString());
        Assert.True(status.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task HidesAndRejectsScreenSaverWhenWindowsDoesNotExposeIt()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var powerActions = new FakeSystemPowerController { ScreenSaverAvailable = false };

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenSaver = true });
            await using var fixture = await PowerHostFixture.StartAsync(powerActions);
            using var socket = await fixture.ConnectAsync();
            var paired = await fixture.PairAsync(socket);

            var result = await SendAndReceiveAsync(socket, new { type = "system.power", action = "screenSaver" });

            Assert.False(paired.GetProperty("capabilities").GetProperty("power").GetProperty("screenSaverAvailable").GetBoolean());
            Assert.Equal("VAIR-POWER-UNAVAILABLE", result.GetProperty("code").GetString());
            Assert.Empty(powerActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task RemoteInputDismissesBlackoutBeforeInputDispatch()
    {
        var powerActions = new FakeSystemPowerController { BlackoutActive = true };
        await using var fixture = await PowerHostFixture.StartAsync(powerActions);
        using var socket = await fixture.ConnectAsync();
        await fixture.PairAsync(socket);

        var inputAck = await SendAndReceiveAsync(socket, new { type = "keyboard.special", key = "Tab", seq = 21 });

        Assert.Equal("input.ack", inputAck.GetProperty("type").GetString());
        Assert.Equal(1, powerActions.BlackoutDismissals);
    }

    [Theory]
    [InlineData(WorkstationLockPolicyState.Disabled, "disabledByPolicy", "VAIR-POWER-LOCK-DISABLED")]
    [InlineData(WorkstationLockPolicyState.Unavailable, "unavailable", "VAIR-POWER-LOCK-UNAVAILABLE")]
    public async Task ReportsUnavailableWindowsLockPolicyWithoutCallingNativeLock(
        WorkstationLockPolicyState policyState,
        string expectedAvailability,
        string expectedCode)
    {
        var originalPermissions = AppPermissionSettings.Load();
        var powerActions = new FakeSystemPowerController();
        var lockPolicy = new FakeWorkstationLockPolicy(policyState);

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPcLock = true });
            await using var fixture = await PowerHostFixture.StartAsync(powerActions, lockPolicy);
            using var socket = await fixture.ConnectAsync();
            var paired = await fixture.PairAsync(socket);

            var result = await SendAndReceiveAsync(socket, new { type = "system.power", action = "lock" });

            Assert.Equal(expectedAvailability, paired.GetProperty("capabilities").GetProperty("power").GetProperty("lockAvailability").GetString());
            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(expectedCode, result.GetProperty("code").GetString());
            Assert.Empty(powerActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReportsNativeFailureAndContinuesProcessingMessages()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var powerActions = new FakeSystemPowerController { Result = new SystemPowerExecutionResult(false, 5) };
        var appLog = new FakeAppLog();

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPcLock = true, AllowDisplayOff = true });
            await using var fixture = await PowerHostFixture.StartAsync(powerActions, appLog: appLog);
            using var socket = await fixture.ConnectAsync();
            await fixture.PairAsync(socket);

            var result = await SendAndReceiveAsync(socket, new { type = "system.power", action = "lock" });
            powerActions.Result = SystemPowerExecutionResult.Success;
            var nextAction = await SendAndReceiveAsync(socket, new { type = "system.power", action = "displayOff" });
            var status = await SendAndReceiveAsync(socket, new { type = "status.get" });

            Assert.Equal("VAIR-POWER-EXECUTION-FAILED", result.GetProperty("code").GetString());
            Assert.True(nextAction.GetProperty("succeeded").GetBoolean());
            Assert.True(status.GetProperty("connected").GetBoolean());
            Assert.Equal(new[] { "lock", "displayOff" }, powerActions.Actions);
            Assert.Contains(appLog.Entries, entry => entry.Event == "command_received" && entry.MessageType == "system.power" && entry.Action == "lock");
            Assert.Contains(appLog.Entries, entry => entry.Event == "action_taken" && entry.Action == "lock" && entry.Outcome == "execution_failed" && entry.Win32Error == 5);
            Assert.Contains(appLog.Entries, entry => entry.Event == "response_sent" && entry.Action == "lock" && entry.Outcome == "failed" && entry.Code == "VAIR-POWER-EXECUTION-FAILED");
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    private static async Task<JsonElement> SendAndReceiveAsync(WebSocket socket, object payload)
    {
        await SendAsync(socket, payload);
        var response = await ReceiveTextAsync(socket);
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    private static async Task<JsonElement> ReceiveTypeAsync(WebSocket socket, string type)
    {
        while (true)
        {
            var response = await ReceiveTextAsync(socket);
            using var document = JsonDocument.Parse(response);
            if (document.RootElement.GetProperty("type").GetString() == type)
            {
                return document.RootElement.Clone();
            }
        }
    }

    private static Task SendAsync(WebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<WebSocketCloseStatus?> ReceiveCloseStatusAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        while (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return result.CloseStatus;
            }
        }

        return socket.CloseStatus;
    }

    private sealed class PowerHostFixture : IAsyncDisposable
    {
        private readonly TempPairingStore _store;
        private readonly FakeInputInjector _inputInjector;

        private PowerHostFixture(TempPairingStore store, FakeInputInjector inputInjector, PairingManager manager, WebHostService webHost)
        {
            _store = store;
            _inputInjector = inputInjector;
            Manager = manager;
            WebHost = webHost;
        }

        private PairingManager Manager { get; }
        private WebHostService WebHost { get; }

        public static async Task<PowerHostFixture> StartAsync(
            ISystemPowerController powerController,
            IWorkstationLockPolicy? workstationLockPolicy = null,
            IAppLog? appLog = null,
            IAwakeService? awakeService = null)
        {
            var store = new TempPairingStore();
            var inputInjector = new FakeInputInjector();
            var manager = new PairingManager(store.Store);
            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                powerController: powerController,
                awakeService: awakeService,
                workstationLockPolicy: workstationLockPolicy ?? new FakeWorkstationLockPolicy(WorkstationLockPolicyState.NotExplicitlyDisabled),
                appLog: appLog,
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            await webHost.StartAsync();
            return new PowerHostFixture(store, inputInjector, manager, webHost);
        }

        public Task<WebSocket> ConnectAsync()
        {
            var app = WebHost.Application ?? throw new InvalidOperationException("The in-memory web host has not started.");
            return app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
        }

        public Task<JsonElement> PairAsync(WebSocket socket)
        {
            return SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = Manager.CreatePairingToken()
            });
        }

        public async ValueTask DisposeAsync()
        {
            await WebHost.StopAsync();
            await WebHost.DisposeAsync();
            _store.Dispose();
            _inputInjector.Dispose();
        }
    }

    private sealed class FakeSystemPowerController : ISystemPowerController
    {
        public List<string> Actions { get; } = new();

        public SystemPowerExecutionResult Result { get; set; } = SystemPowerExecutionResult.Success;

        public bool ScreenSaverAvailable { get; set; } = true;

        public bool BlackoutActive { get; set; }

        public int BlackoutDismissals { get; private set; }

        public SystemPowerExecutionResult TryExecute(string action)
        {
            Actions.Add(action);
            return Result;
        }

        public bool IsActionAvailable(string action)
        {
            return action != SystemPowerActions.ScreenSaver || ScreenSaverAvailable;
        }

        public bool DismissBlackoutIfActive()
        {
            if (!BlackoutActive)
            {
                return false;
            }

            BlackoutActive = false;
            BlackoutDismissals += 1;
            return true;
        }
    }

    private sealed class FakeWorkstationLockPolicy : IWorkstationLockPolicy
    {
        public FakeWorkstationLockPolicy(WorkstationLockPolicyState state)
        {
            State = state;
        }

        public WorkstationLockPolicyState State { get; private set; }

        public event EventHandler? Changed;

        public WorkstationLockPolicyStatus GetStatus() => new(State);

        public WorkstationLockEnableResult TryEnable()
        {
            State = WorkstationLockPolicyState.NotExplicitlyDisabled;
            Changed?.Invoke(this, EventArgs.Empty);
            return new WorkstationLockEnableResult(true, "Windows locking is enabled for this user.");
        }
    }

    private sealed class FakeAppLog : IAppLog
    {
        public string LogDirectory => string.Empty;

        public List<AppLogEntry> Entries { get; } = new();

        public void Write(AppLogEntry entry)
        {
            Entries.Add(entry);
        }

        public AppLogReadResult Read(AppLogQuery query) => new(true, []);

        public AppLogDeleteResult DeleteAll() => new(true, 0);
    }
}

# Setup

This is the operational authority for installing Voltura Air, starting a
development host, using host command-line options, and making the first
connection. Product capabilities live in [features.md](features.md); connection
behavior and recovery live in [network-and-host-selection.md](network-and-host-selection.md),
[pairing-feedback.md](pairing-feedback.md), and
[troubleshooting.md](troubleshooting.md).

## Install a release build

Choose one package from the latest GitHub release:

- `VolturaAir-Setup-<version>-win-x64.exe` checks for the .NET 10 Windows
  Desktop and ASP.NET Core runtimes and downloads missing runtimes. That step
  requires internet access and may request administrator approval because the
  runtimes are installed for the PC.
- `VolturaAir-Setup-<version>-win-x64-full.exe` includes the required runtimes
  and works without a runtime download.
- `VolturaAir-<version>-win-x64.zip` is the portable package.

Both installers install per user under `%LOCALAPPDATA%\Programs\Voltura Air`,
create Start Menu shortcuts, and retain pairing and settings data under
`%APPDATA%\Voltura Air` when uninstalled. Start-at-sign-in is controlled by the
in-app setting.

## First connection

1. Start Voltura Air on the Windows 11 PC.
2. Open its Connect page from the window or tray menu.
3. Put the phone or tablet on the same Wi-Fi or LAN as the PC.
4. Scan the current QR code and confirm the device name.

The QR code contains the selected host origin and a short-lived pairing token.
During pairing, the browser creates a P-256 key pair, keeps the private key in
local browser storage, and registers only the public key with the host. Paired
devices reconnect by signing a fresh host challenge with that private key.

If the PC address, adapter, or port changes, scan a fresh QR code or use the
mobile manual-host recovery. See [network and host selection](network-and-host-selection.md)
and [troubleshooting](troubleshooting.md).

## Developer run

Install dependencies, build both runtime halves, and start the host:

```powershell
npm ci
npm run build
dotnet run --project apps/windows-host/VolturaAir.Host.csproj
```

Use `npm install` instead of `npm ci` when intentionally changing dependency
manifests.

## Development loop

To browse every root command with a plain-English description and the underlying
script, run:

```powershell
npm run help
```

`npm run` itself lists script names and their raw command strings; npm does not
support per-script descriptions in `package.json`.

Run the normal combined development command:

```powershell
npm run dev
```

This starts Vite on port `5173` for React fast refresh and starts the Windows
host through `dotnet run`. The QR code normally opens the host-served client so
the app and `/ws` use the same origin.

For the quickest phone layout and interaction loop, run:

```powershell
npm run dev:quick
```

This starts the current host through `dotnet run` while running a fast Vite
bundle build in parallel without type checking, linting, tests, or the
bundle-budget gate. The quick path reuses a cached native cursor watchdog when
available and builds it once when the shared command-line output is empty. Quick
launches share a stable
command-line build cache with direct `dotnet` builds, tests, and other repository
automation. Command-line compiled and WPF-generated outputs use each .NET
project's `bin/cli` and `obj/cli` directories, while IDE design-time and
IDE-initiated builds retain the standard `bin` and `obj` output directories.
NuGet keeps its normal shared `obj` project metadata, which is safe to reuse and
preserves the SDK's generated-file exclusions. This keeps competing WPF-generated
sources out of each other's way without sacrificing incremental reuse between
command-line workflows. Release builds retain the standard paths used by the
packaging commands. The
host serves client files directly from `apps/mobile-web/dist` in Debug, so an
existing bookmarked app can receive the rebuilt client through the normal
build-ID auto-refresh as soon as the bundle finishes. Restart
`npm run dev:quick` after another source edit. Use the normal build and
validation commands before treating the change as complete.

For direct mobile Vite hot reload:

```powershell
$env:VOLTURA_AIR_USE_VITE_CLIENT = "1"
npm run dev
```

The QR code then opens the Vite LAN URL and includes the Windows host as its
WebSocket host hint.

Run one side only when needed:

```powershell
npm run dev:web
npm run dev:host
```

For an isolated interactive Chrome device-mode session, use `npm run dev:ui`.
For the corresponding automated smoke coverage, use `npm run test:ui`. These
workflows use disposable loopback pairing and settings and must not run beside a
normal host. See [screenshots.md](screenshots.md) for capture-specific behavior.

## Windows host command-line options

The packaged Release host supports only normal startup and safe validation
options:

| Option | Purpose |
| --- | --- |
| `--minimized` | Start without opening the main window. Used for start-at-sign-in. |
| `--isolated-test-mode` | Use the normal single-instance scope while binding only to loopback, isolating settings, disabling real system power actions, and avoiding persistence of automatic network/port choices. |

Debug builds additionally support development and capture options:

| Option | Purpose |
| --- | --- |
| `--client-url <URL>` | Put a development client URL in the pairing link. `VOLTURA_AIR_CLIENT_URL` provides the same Debug-only override. |
| `--print-host-client-url` | Print the selected host URL after successful startup. Used by `npm run dev:host`. |
| `--pairing-store-root <path>` | Redirect pairing persistence to a disposable directory. Use only with `--isolated-test-mode`. |
| `--pairing-url-file <path>` | Write the real pairing URL for local automation. The file contains a live token and must remain temporary and private. |
| `--enable-alpha-features` | Enable alpha features only inside isolated test settings. |
| `--site-screenshot-mode` | Enable public-safe screenshot rendering; requires `--isolated-test-mode`. |
| `--site-screenshot-theme <Light\|Dark\|System>` | Choose the screenshot host theme. |
| `--site-screenshot-preferences-section <name>` | Open a named Preferences section for host capture. |

Release builds ignore Debug-only options and `VOLTURA_AIR_CLIENT_URL`.

## Development prerequisites

- Node.js and npm.
- .NET 10 SDK.
- Visual Studio Build Tools with the Desktop development with C++ workload for
  the native cursor recovery watchdog.
- NSIS 3.12 or later only when building Windows installers.

## Platform limitations

- The host currently targets Windows 11.
- Voltura Air is LAN-only and has no cloud relay.
- Browser speech recognition depends on browser support and origin policy.
- Normal input injection cannot control UAC, the secure desktop, the lock screen,
  or an elevated application when the host runs at lower integrity.
- Windows Firewall or network isolation can block inbound LAN traffic.
- A sleeping or shut-down host cannot receive a remote wake command.

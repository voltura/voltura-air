# Windows host

Read `../../docs/host-quality.md`, `../../docs/architecture.md`,
`../../docs/ui-system.md`, and `../../docs/host-ui-guidelines.md`.

Before a Windows-host command, run:

```powershell
./scripts/host-preflight.ps1
```

## Code

- Target .NET 10 and stable C# 14.
- Keep the tray host event-driven. Avoid polling, hot-path registry reads, and
  unnecessary allocations or background work.
- Keep the checked-in analyzer policy. Use only narrow, justified suppressions.

## Ownership

- `WpfHostRuntime` owns startup rollback and shutdown. UI requests work;
  services own timers, subscriptions, native resources, workers, and storage.
- Every long-lived worker or resource has one owner, bounded input,
  cancellation, deterministic disposal, and a shutdown wait.
- Serialize registered-WebSocket sends. Use the existing coalescing status
  broadcaster; do not create parallel sends or workers for the same state.
- The Presentation alpha gate defaults on. Explicit off removes its capability
  and blocks its commands. Disabled features allocate no feature resources.

## Boundaries

- Prefer source-generated `LibraryImport`, exact Unicode APIs, explicit
  marshalling, System32 DLL search, and `SafeHandle` where appropriate.
- Bound and validate pairing-store reads; replace it atomically. Never persist
  or log reconnect keys or proofs.
- Bound, authenticate, authorize, normalize, and decode WebSocket input once
  before dispatch. Expected failures restore state and keep the host running.
- Use `IAwakeService` for keep-awake work; do not edit power plans or require
  elevation.

## WPF

- Use WPF. Limit WinForms/System.Drawing to tray interop or narrow legacy code.
- `MainWindow` is the shell; feature types own feature behavior and state. Do
  not add `MainWindow` partial files as feature boundaries.
- Prefer declarative XAML and standard WPF layout. Containers own sibling gaps;
  use tokenized `SpacingStackPanel` and `SpacingWrapPanel`.
- Follow `docs/pairing-feedback.md` for pairing and connection failures.

## Verify

- Test changed native, registry, filesystem, process, network, persistence, and
  resource boundaries for success, expected failure, and cleanup.
- UI-only changes need focused UI/state tests where useful, a warning-free host
  build, and quick checks at relevant size, DPI, theme, focus, and scroll states.
- Use normal project tests, not ad-hoc reflection or UI scripting.
- Run host build and tests sequentially:

```powershell
dotnet build VolturaAir.slnx
dotnet test VolturaAir.slnx --no-build
```

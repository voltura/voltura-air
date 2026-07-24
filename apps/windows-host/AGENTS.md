# Windows host instructions

These instructions apply to the .NET Windows host. Follow
`../../docs/host-quality.md`, `../../docs/architecture.md`,
`../../docs/ui-system.md`, and `../../docs/host-ui-guidelines.md`.

## Required preflight

Before any command that uses the Windows host, run:

```powershell
./scripts/host-preflight.ps1
```

It must be run.

## Runtime and language

- Target .NET 10 and stable C# 14. Use current language and runtime features when
  they simplify ownership, improve correctness, or reduce meaningful allocation
  or CPU cost. Do not introduce legacy syntax for compatibility or rewrite clear
  code solely to showcase newer syntax.
- Keep the continuously running tray host lightweight and event-driven. Avoid
  polling, unnecessary background work, hot-path registry reads, and repeated
  lifetime allocations. Measure uncertain optimizations and do not trade away
  maintainability for insignificant savings.
- The checked-in compiler, formatting, and reviewed analyzer policy is enforced
  as errors. Do not enable every optional analyzer, weaken a rule, or add a broad
  suppression merely to make the build pass. Suppress only a narrow, justified
  platform or ownership exception beside the affected code.

## Ownership and resources

- `WpfHostRuntime` owns startup rollback and process-resource shutdown.
  `WpfTrayApplicationContext` and WPF windows render state and request commands
  or shutdown. Services own their timers, subscriptions, native resources,
  protocol workers, and persistence. UI code does not dispose or operate service
  internals directly.
- Every long-lived worker and resource has one owner, bounded input,
  cancellation, deterministic disposal, and a shutdown wait. UI event handlers
  may enqueue owned service work but do not create competing fire-and-forget
  workers.
- Serialize sends per registered WebSocket and bound them with cancellation and
  operation timeouts. Status changes use the single host-owned coalescing
  broadcaster; do not add parallel sends or another worker for the same state.
- The **Enable alpha features** setting defaults on for Presentation. An
  explicit off choice omits its capability and blocks it again at every
  production command boundary. Another incomplete feature must not inherit this
  default-on activation without a separately reviewed decision. Disabled
  features allocate no feature-specific worker, timer, subscription, native
  resource, or network work.

## Native and persisted boundaries

- Use source-generated `LibraryImport` when it can faithfully represent the API.
  Use exact Unicode entry points for text-sensitive APIs, explicit native boolean
  and string marshalling, the System32 DLL search policy, and `SafeHandle` where
  it clarifies ownership. Keep classic interop only for unsupported callback,
  activation, or lifetime cases with a narrow rationale.
- Treat pairing data as untrusted. Bound and validate reads and records, replace
  the store atomically in its directory, and never persist or log private
  reconnect keys or reconnect proofs.
- Keep WebSocket input bounded, authenticated, permission checked, normalized,
  and decoded once before dispatch. Expected boundary failures restore partial
  state, preserve the running host, and give useful feedback.
- Keep keep-awake execution-state calls on the dedicated Awake service thread.
  Tray, WPF, and protocol surfaces use `IAwakeService`; they do not edit Windows
  power plans or require elevation.

## WPF UI

- The host UI is WPF-first. WinForms/System.Drawing is limited to tray interop or
  narrow legacy code not yet removed. Qualify or alias collision-prone framework
  names such as `Application`, `Brushes`, `Button`, `CheckBox`, `Color`,
  `DataFormats`, `Image`, and `HorizontalAlignment`.
- Follow the host-presentation ownership in `../../docs/architecture.md`:
  `MainWindow` is the WPF shell and composition root, while feature-owned types
  own non-visual state and behavior. Do not add `MainWindow` partial files as a
  substitute for a feature boundary. Keep partial types for WPF XAML,
  source-generated interop, or another framework requirement.
- Prefer declarative XAML and device-independent `Grid`, `DockPanel`,
  `StackPanel`, `ScrollViewer`, `ItemsControl`, and `ListView` layout over manual
  positioning or code-built ordinary visual trees.
- Composition containers own sibling gaps. Use `SpacingStackPanel` and
  `SpacingWrapPanel` with generated tokens; reusable leaf controls have no
  adjacency margin. Local margins remain valid for insets, overlays, template
  geometry, and documented optical corrections.
- Keep one explicit owner for sizing, scrolling, focus, disclosure, and adaptive
  behavior. Do not add custom scrollbars or runtime layout shims to repair a
  violated layout contract.
- Pairing and connection failure surfaces follow `../../docs/pairing-feedback.md`
  and never leave the user on apparently active controls without explanation.

## Validation

- For a changed native, registry, filesystem, process, network, persistence, or
  resource boundary, first run a focused acceptance path covering success,
  expected failure, and cleanup or restoration. Test packaged assets through the
  same production API when applicable.
- Presentation-only host changes use focused UI/state tests where meaningful,
  the warning-free host build, and quick human checks at relevant Windows compact
  and regular sizes, DPI, theme, focus, and scrolling states.
- Use conventional project tests. Do not substitute PowerShell reflection,
  dynamic assembly loading, or ad-hoc UI scripting for the normal test runner.
- Run host build and test commands sequentially:

```powershell
dotnet build VolturaAir.slnx
dotnet test VolturaAir.slnx --no-build
```

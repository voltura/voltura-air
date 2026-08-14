# Windows host

Inherits root; read relevant architecture/host-quality and WPF UI guidance.

- Long-lived resources: one owner; bounded input/cancellation/deterministic
  disposal/awaited shutdown. `WpfHostRuntime` owns rollback/shutdown; services
  own work/resources.
- Serialize registered-socket sends; use coalescing status broadcaster.
- Pairing store: bound/validate reads; replace atomically; never persist/log
  reconnect keys/proofs.
- Authenticate/authorize/normalize/decode bounded input once.
- Custom screens and Presentation remain composed, advertised, permission-gated,
  and executable. Experimental features use explicit, feature-owned Developer
  tools toggles; do not add a global alpha gate.
- Prefer generated interop/exact Unicode APIs/explicit marshalling/System32
  search/`SafeHandle`.
- Use `IAwakeService`; never edit power plans or require elevation.
- `MainWindow` is shell; features own behavior; no feature partials; use
  declarative WPF/shared spacing.
- This project mixes a Windows Forms tray with WPF windows. In WPF source,
  alias or fully qualify UI types and dependency properties; never rely on an
  unqualified type when both `System.Windows.Forms` and `System.Windows`
  provide that name (for example `Control`, `Brush`, `Brushes`, `Image`,
  `Point`, or `Size`).

Verify with warning-free `dotnet build VolturaAir.slnx`; focused
`dotnet test --filter` for changed behavior. Structural changes add
`npm run host:ownership:check`. Shared
lifecycle/native/resource/registry/persistence/protocol work needs focused
boundary coverage. Full `npm run test:host` only for broad/shared work. UI
follows root checkpoint.

# Windows host quality

## Enforced baseline

- Host and host tests use nullable reference types, configured .NET analyzers,
  and warnings as errors.
- `.editorconfig` promotes reviewed disposal, cancellation, async, interop,
  security, ASP.NET Core, performance, and behavior-neutral rules. Production
  suppressions are narrow and explain their reason beside the code.
- Native calls prefer source-generated `LibraryImport`, exact Unicode APIs,
  explicit native Boolean marshalling, `SafeHandle`, and System32-only DLL
  search.
- One maintained file declares each host type. XAML/generated interop may use
  required partials; `npm run host:ownership:check` enforces the boundary.
## Runtime contract

- UI-affine work stays on the dispatcher; network, filesystem, and blocking
  native work do not.
- One owner controls every timer, subscription, socket, cancellation source,
  stream, native handle, tray object, worker, and recurring dispatcher action.
  Inputs and queues are bounded; cleanup is deterministic and awaited.
- Non-cancellable native calls have bounded callers. Late completion reconciles
  native state to the last committed state before more work.
- Authentication, origin validation, throttling, message bounds, and permission
  checks precede privileged actions. Input is normalized and decoded once;
  socket sends are serialized and status updates coalesced.
- Settings used on hot paths are cached. Pairing and other persisted data are
  bounded, validated, and atomically replaced.
- External failures stop at their owning boundary and are logged without
  secrets, payload contents, typed text, pointer coordinates, or credentials.
- Prefer event-driven work. Polling or lifetime allocations require measured
  justification.

## Validation by risk

| Change | Default check |
| --- | --- |
| Ordinary host code | Warning-free `dotnet build VolturaAir.slnx`; focused `dotnet test --filter` only when behavior changes. |
| Host source ownership | Add `npm run host:ownership:check`. |
| Native/resource, filesystem, registry/persistence, process, network, protocol, or shared lifecycle boundary | Focused production-path tests for success, expected failure, and cleanup/restoration. |
| Broad/shared host work | Full `npm run test:host`. |
| WPF-only presentation | Warning-free build, useful focused state tests, and the root visual-checkpoint policy. |
| Release or repository-wide shared contract | Sequential root `npm run build` and `npm test`. |

`npm run package:win:test` is an installer-iteration check whose
`artifacts/test` output is not releasable. Production packaging and release
verification belong to [release.md](release.md).

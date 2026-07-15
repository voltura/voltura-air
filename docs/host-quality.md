# Windows host quality standard

Voltura Air's Windows host is a long-running Windows 11 WPF tray application. Its quality gate prioritizes correctness, predictable lifetime ownership, bounded resource use, protocol safety, and maintainable modern C# over maximizing the number of enabled analyzer rules.

## Enforced baseline

- The host and host tests use nullable reference types, the current recommended .NET analyzer baseline, and warnings as errors.
- `apps/windows-host/.editorconfig` promotes reviewed disposal, cancellation, async, native interop, security, ASP.NET Core, performance, and behavior-neutral modern C# rules to errors.
- Rules designed for reusable public libraries or context-free libraries remain disabled when their assumptions conflict with a WPF executable. Every production suppression must be narrow and include its reason beside the affected code.
- Native calls use source-generated `LibraryImport` where supported, exact Unicode entry points for text-sensitive Windows APIs, explicit native boolean marshalling, and a process-wide System32 DLL search policy.

The checked-in policy runs during ordinary host and host-test builds. A clean build is the quality gate:

```powershell
dotnet build VolturaAir.slnx
```

## Runtime expectations

- UI-affine work stays on the WPF dispatcher; network, filesystem, and long-running native work must not block it.
- Timers, event subscriptions, sockets, cancellation sources, streams, native handles, tray resources, and background services have one owner and deterministic disposal.
- WebSocket concurrency and message sizes remain bounded. Authentication, origin validation, rate limiting, and permission checks precede privileged actions; sends are serialized and timed per connection, and status updates are coalesced through one host-owned worker.
- Persisted pairing data is bounded and validated before use and replaced atomically in its own directory, so a partial write cannot replace the last complete store.
- External failures are caught at their owning boundary, logged without secrets or payload contents, and must not terminate the tray host.
- Prefer event-driven behavior and avoid polling or allocations that repeat for the lifetime of the process without a measured reason.

## Modern C# policy

Use stable C# 14 and current .NET APIs when they make code clearer or reduce work: collection expressions, primary constructors, pattern matching, `Lock`, spans, cached serializer options, async disposal, and source-generated interop. Do not introduce a new abstraction or rewrite clear code solely to use newer syntax; the result must simplify ownership, improve correctness, or reduce meaningful allocation or CPU cost.

## Change gate

Start with the focused acceptance test for the changed production boundary, then run `npm run build` and `npm test` sequentially. `npm run package:win:test` may be used during installer iteration to skip NSIS compression while retaining installer-content and metadata checks; its outputs under `artifacts/test` are not releasable. Release verification additionally runs `npm run package:win`, which owns the compressed production publishes, installer creation, and Windows metadata validation.

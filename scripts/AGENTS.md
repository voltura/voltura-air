# Automation

Inherits root.

## Safety

- Launchers own preflight/cleanup; no competing hosts.
- UI/capture/temp hosts: `--isolated-test-mode`, loopback, isolated
  settings/pairing, no persisted auto network choice. Human
  `dev`/`dev:quick`: normal settings.
- Protocol automation: in-memory `TestServer`; never configured port/firewall.
- Reuse required ports or stop owners; never silently switch.
- `npm run dev`: checked development; `npm run dev:quick`: unchecked
  current-source validation.

## Destructive

- Validate explicit targets. Preview broad ignored cleanup; preserve `.vs` and
  `.vscode/settings.json`.
- `cache:purge`: stale icons only; stops host, resets current-user icon cache,
  restarts Explorer.
- Never `clean:git` during another Git operation.
- `maintenance:full` only when all intended: cache purge, ignored cleanup, Git
  maintenance, dependency updates.

## Verify

- `artifacts/test` installers are not release inputs; follow `docs/release.md`.
- Run relevant `tests/scripts/<file>` test. Full `npm run test:scripts` only for
  shared orchestration/root package-script composition.

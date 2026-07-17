# Voltura Air TODO

Approved work is listed in execution order. Each checkbox is one coherent
outcome. Current behavior is defined in
[features.md](features.md), governing documents are listed in
[README.md](README.md), and candidate directions are recorded in
[ideas.md](ideas.md).

## Order

1. Release-blocking correctness, security, connection, input, data-loss,
   recovery, and resource-lifetime defects take precedence.
2. Complete P0 architecture and quality work.
3. Complete P1 Presentation before evaluating another feature candidate.
4. Finish each item with implementation, obsolete-path removal, aligned
   documentation, focused acceptance coverage, and the proportionate quality
   gate. Remove completed items from this file.

## P0 - Architecture and quality

- [ ] **Complete the mobile target structure.** Move remaining root foundation
  modules and the `connection`, `input`, `pairing`, `pwa`, and `settings`
  ownership into `foundation/<domain>`. Keep `app` as composition,
  `features/<capability>` as vertical slices, and `ui` domain-neutral. Remove
  superseded paths and enforce the target dependency direction.
- [ ] **Complete UI-system conformance.** Bring React and WPF surfaces into
  `ui-system.md` and `host-ui-guidelines.md`: shared tokens and primitives,
  container-owned spacing, declarative composition, slice-local styles, clear
  scroll/focus ownership, and verified adaptive and accessible states.
- [ ] **Resolve ownership and lifecycle findings.** Review `npm run size:report`,
  split mixed responsibilities, and record justified cohesive exceptions.
  Inventory long-lived resources and their cleanup, close meaningful lifecycle
  test gaps, and add measured performance or bundle guardrails where useful.
- [ ] **Align documentation and automation.** Update target documents, scoped
  instructions, linting, size reporting, build commands, and CI to match the
  completed structure. Finish with a repository-wide contradiction,
  stale-command, and documentation-map review.

## P1 - Presentation

- [ ] **Graduate Presentation from alpha.** Complete the existing PowerPoint,
  Google Slides, and PDF/browser workflow across mobile UI, host commands,
  protocol, permissions, accessibility, lifecycle, documentation, and
  validation:
  - one owned path for each fixed target/action, capability, result, and state;
  - one in-flight acknowledged command, host-focus protection, permission
    enforcement, and honest delivery feedback;
  - complete portrait/landscape, theme, touch, keyboard, screen-reader,
    reconnect, denied, timeout, failure, and unavailable states;
  - deterministic timer/vibration cleanup with no inactive background work;
  - production-path success, failure, disconnect, and retry coverage plus stated
    real-app, Windows, browser, and device validation boundaries;
  - removal of the alpha gate from capability, permission, discovery, and command
    paths while retaining Presentation's permission and safety boundaries.

## Candidate promotion

An entry from [ideas.md](ideas.md) moves here after a product decision defines
its outcome, priority, structural boundary, and validation expectations.

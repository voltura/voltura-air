# Voltura Air TODO

Scheduled approved work is listed in execution order. Decision-complete designs
without assigned priority are linked separately. Each checkbox is one coherent
outcome. Current behavior is defined in
[features.md](features.md), governing documents are listed in
[README.md](README.md), and candidate directions are recorded in
[ideas.md](ideas.md).

## Order

1. Release-blocking correctness, security, connection, input, data-loss,
   recovery, and resource-lifetime defects take precedence.
2. Complete P1 Presentation before evaluating another feature candidate.
3. Finish each item with implementation, obsolete-path removal, aligned
   documentation, focused acceptance coverage, and the proportionate quality
   gate. Remove completed items from this file.

## P1 - Presentation

- [ ] **Graduate Presentation from alpha.** Complete the existing PowerPoint,
  Google Slides, and PDF/browser workflow across mobile UI, host commands,
  protocol, permissions, accessibility, lifecycle, documentation, and
  validation:
  - one owned path for each fixed target/action, capability, result, and state;
  - one in-flight acknowledged command, host-focus protection, permission
    enforcement, and honest delivery feedback;
  - complete portrait/landscape, theme, input-method, screen-reader, reconnect,
    denied, timeout, failure, and unavailable states under the
    [surface input priorities](ui-system.md#surface-input-priorities);
  - deterministic timer/vibration cleanup with no inactive background work;
  - production-path success, failure, disconnect, and retry coverage plus stated
    real-app, Windows, browser, and device validation boundaries;
  - removal of the alpha gate from capability, permission, discovery, and command
    paths while retaining Presentation's permission and safety boundaries.

## Candidate promotion

An entry from [ideas.md](ideas.md) moves here after a product decision defines
its outcome, priority, structural boundary, and validation expectations.

## Unscheduled approved design

- [PC-assisted controls within Dictate](dictate-pc-assist-plan.md) has an
  approved product and technical design. Its implementation timing and order
  are intentionally not assigned here.

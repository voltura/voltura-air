# Voltura Air candidate directions

These directions await a product decision. Each entry records the intended
value and the decision required before it can move into [todo.md](todo.md).

## Presentation alpha candidates

Presentation-specific candidates—including recent-presentation launch from the
device, canonical presentation identity, cross-session analytics, multi-select
bulk management, and deeper mail-provider integration—are maintained with their
platform, privacy, dependency, and decision context in the
[Presentation feature alpha authority](presentation-feature-alpha.md#candidate--v2-register).
They move into this general register when Presentation graduates.

## Control and personalization

### Custom shortcut panels

User value: Remote panels tailored to applications and personal workflows.

Proposed boundary:

- The host owns bounded panels, ordered buttons, and opaque action IDs; mobile
  receives labels, icons, enabled state, and IDs.
- Actions may cover allowlisted key chords, bounded key sequences, bounded text,
  the existing Open URL path, and existing host-approved application launches.
- Windows Preferences owns editing and reset; mobile owns selection and display.
- Candidate presets are Media PC, Browser, Presentation, Developer, and Swedish
  keyboard.
- Export/import covers versioned panel definitions while excluding executable
  paths, arguments, permissions, pairing data, and secrets.

Decision: shared panels for all devices or per-device assignments. Shared panels
are the smaller ownership model.

### Additional device preferences

Candidates include restoring the last supported mode per PC/client and assigning
a default Remote mode or shortcut panel. Theme, keyboard rows, split placement,
and panel layout remain browser-local unless a cross-device workflow justifies
host ownership.

Decision: identify the cross-device workflow and owner for each new setting.

## Public project and release

| Candidate | Value | Decision boundary |
| --- | --- | --- |
| Demo video/GIF | Show pairing and core controls quickly. | Isolated capture, captions, privacy-safe content, and no third-party copyrighted media. |
| Comparison table | Explain product position. | Verifiable current facts and primary-source research for named alternatives. |
| FAQ | Answer recurring setup, pairing, permission, diagnostics, network, and unsigned-download questions. | Add when real support demand justifies a maintained FAQ. |
| Privacy page | Hold legal contact, analytics/cookie, or retention disclosures. | Add when those disclosures outgrow the product-page trust section. |
| Release checksums | Let users verify the ZIP and installers. | Generate and verify one `SHA256SUMS.txt` against every uploaded release asset. |
| Release-note template and limitations | Make release scope and validation visible. | Assign ownership for human review and current limitations. |
| Update notification | Surface a newer official release. | Choose manual, opt-in periodic, or disabled-by-default checks plus privacy and failure behavior. |
| Code signing | Improve Windows publisher trust. | Organization certificate, cost, account/key ownership, CI signing, timestamping, renewal, revocation, and asset coverage. |
| Automatic update | Reduce manual upgrade work. | Requires integrity, signing, consent, rollback, failure recovery, privacy review, and a maintenance owner. |
| Microsoft Store | Add another trusted distribution channel. | Packaging, signing, account, policy, update-channel ownership, and demonstrated user benefit. |
| Additional issue forms and labels | Improve feature and connection intake. | Maintainer ownership, safe diagnostic fields, security routing, and language that avoids implying commitment. |

## Research-gated capabilities

The current LAN-only trust model is the baseline for these candidates.

| Candidate | Primary decision | Required evidence |
| --- | --- | --- |
| Wake-on-LAN | Choose an always-available sender while the PC host sleeps. | Router/NAS, separate LAN relay, or documented external setup; validated MAC/target data; explicit send confirmation; hardware/network prerequisites. |
| Screen preview | Choose still image, bounded low-FPS preview, or a separate remote-desktop product. | Windows capture API, consent and privacy indication, multi-monitor/protected-content behavior, encoding, browser decode, resource limits, authorization, and disconnect cleanup. |
| Phone-to-PC file send | Define a separate authenticated upload boundary. | File/quota limits, destination, confirmation, safe names, path containment, Windows provenance, cancellation, failure, and partial-file cleanup. |
| Motion or air mouse | Establish predictable sensor control across supported browsers. | Secure-context and permission behavior, calibration, dead zone, sensitivity, active-session collection, cleanup, and Trackpad fallback. |
| Gamepad mode | Select a Windows virtual-controller injection model. | Driver, signing, elevation, installation/removal, anti-cheat compatibility, disconnect-to-neutral behavior, and measured latency. |
| Native mobile apps | Demonstrate a user need the PWA cannot meet. | Wrapper versus native architecture, iOS/Android scope, protocol consistency, accessibility, privacy, distribution, updates, testing, crash reporting, and long-term ownership. |

## Platform and compatibility

### External compatibility contract

Trigger: deciding to guarantee upgrades between public versions.

Decision: version and support persisted settings, pairing data, protocol messages,
and client formats, including upgrade and failure-recovery behavior.

### HTTPS LAN transport and certificate handling

Decision: define the threat model and user benefit, then choose host identity,
certificate issuance and renewal, private-key custody, mobile trust, browser and
discovery behavior, reconnect/recovery, packaging, support, and lifecycle owner.
Self-signed certificates, a local CA, pinning, and public-domain infrastructure
are distinct trust models. Open URL support for HTTPS addresses is unrelated to
the app's own LAN transport.

## Dependencies

1. The custom panel model precedes presets and panel-related device preferences.
2. Release integrity and signing decisions precede automatic-update evaluation.
3. Store distribution depends on packaging, signing, account, and update-channel
   ownership.
4. External compatibility work precedes any upgrade guarantee.
5. Each research-gated capability receives its own decision and evidence task.

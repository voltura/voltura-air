# PC-assisted controls within Dictate

## Status

This is an approved design. Its implementation timing and order are not assigned.

## Outcome

Extend the existing **Dictate** feature with an optional PC-assisted path that
can inspect and control the Windows system-default recording device and open
Windows Voice Typing. This is not a standalone mode, navigation destination, or
separate product feature.

Device/browser dictation keeps its current functionality. Its layout may change
so the device and PC paths form one coherent Dictate experience.

The PC path is protected by the **Enable alpha features** umbrella
gate. Within that gate, PC-assisted Dictate support is allowed by default and
can be disabled globally or for an individual paired device.

## Mobile experience

- Compose Dictate from two compact paths: device dictation and PC-assisted
  dictation. Put device dictation first when browser speech recognition is
  available and PC assistance first when it is unavailable.
- Omit PC assistance when the host does not advertise its alpha capability.
  When supported but blocked by host permission, retain the section with the
  short state **Disabled on PC**.
- Request PC microphone status when Dictate opens, after reconnect, and after a
  PC microphone operation. Do not poll continuously.
- Show only concise state and useful actions on the main surface: microphone
  name, input level, availability, **Open on PC**, **Test**, **Unmute**, input
  level, refresh, and relevant Windows Settings recovery.
- Put explanatory copy behind an accessible information glyph and popover or
  dialog. Do not place long instructions on the main Dictate surface. Keep short
  actionable errors visible.
- The information surface explains that Windows Voice Typing uses Microsoft
  Speech Services, a text field on the PC must have focus, Voltura Air inspects
  only the Windows system-default input, Voice Typing may have another selected
  microphone, and hardware mute or exclusive capture can make a silent test
  inconclusive.
- Disable **Open on PC** while there is no usable default input, the endpoint is
  muted, or its configured input level is zero. A microphone test is optional,
  not a prerequisite.
- Allow explicit unmute only; do not silently unmute as part of another action.
- Let the phone change the system-wide default microphone level from 0 to 100.
  Commit pointer changes on release and keyboard changes on completion rather
  than sending every intermediate slider value.
- Permit one PC-dictation operation in flight. Disable conflicting controls
  until the matching result arrives, clear pending state on disconnect, and do
  not replay operations after reconnect.

## Host ownership and native behavior

- Add a default-on `AllowPcDictation` global permission and matching nullable
  per-device override using the existing inherit/allow/block model. Expose its
  host settings only while alpha features are enabled.
- Add a focused PC-dictation command handler and service. The handler owns
  protocol validation, authentication context, the alpha and permission checks,
  result mapping, and safe logging. The service owns Core Audio and Windows
  operations.
- Reuse or extract the existing Core Audio interop ownership rather than
  duplicating COM declarations. Preserve existing output-volume behavior.
- Query only the active system-default `eCapture`/`eConsole` endpoint. Read its
  display name, software mute state, and scalar level through Core Audio.
- Set level through `IAudioEndpointVolume`. Clamp nothing silently: reject wire
  values outside the integer range 0–100.
- Test activity for a fixed three-second window using only scalar
  `IAudioMeterInformation` peak values. A peak above the fixed implementation
  threshold reports **Signal detected**; otherwise report an inconclusive
  result. Never create an audio capture stream.
- Create and release native objects within each operation. Alpha-disabled or
  idle operation must allocate no feature-specific timer, subscription, worker,
  native resource, or network activity. Cancellation and disconnect must end a
  running test and release every COM object deterministically.
- Revalidate endpoint availability, mute, and level before opening Voice Typing.
  Inject the fixed `Win`+`H` shortcut through the existing protected input path.
  Apply host-window and higher-integrity foreground protection.
- A successful shortcut result means only that Windows accepted the input. It
  must not claim that Voice Typing is listening or inserted text. Windows owns
  first-use consent, Speech Services UI, microphone selection, and recognition.
- Open only fixed, allowlisted Windows Settings pages for default-input
  properties and microphone privacy. Never accept a URI from the client.
- Do not log microphone names, configured levels, peak values, acoustic-derived
  details, or any dictated text or audio.

## Protocol contract

While alpha features are enabled, advertise:

```json
{
  "capabilities": {
    "dictation": {
      "pcAssist": {
        "canControl": true
      }
    }
  }
}
```

Omit `capabilities.dictation` when the alpha gate is off. Set `canControl` to
the paired device's effective permission when the gate is on.

Use one acknowledged request shape:

```json
{
  "type": "dictation.pc.command",
  "operationId": "bounded-client-operation-id",
  "action": "status"
}
```

Allowed actions are `status`, `testSignal`, `unmute`, `setLevel`,
`openVoiceTyping`, `openInputSettings`, and `openPrivacySettings`. `setLevel`
requires an integer `level` field from 0 through 100. Every other action rejects
`level`, and every action rejects undeclared fields.

Return the matching result:

```json
{
  "type": "dictation.pc.command.result",
  "operationId": "bounded-client-operation-id",
  "action": "status",
  "succeeded": true,
  "message": "PC microphone is ready.",
  "status": {
    "deviceState": "available",
    "displayName": "Microphone",
    "muted": false,
    "level": 72,
    "signal": "notTested"
  }
}
```

`deviceState` is `available`, `missing`, or `unavailable`. Fields that cannot be
reported are omitted rather than sent as `null`. `signal` is `notTested`,
`detected`, or `inconclusive`. A successful silent test returns `inconclusive`;
silence is not treated as proof of microphone failure.

Expected failure codes are `feature-disabled`, `permission-denied`,
`no-device`, `device-unavailable`, `invalid-level`, `meter-unavailable`,
`host-ui-blocked`, `elevated-target-blocked`, `input-failed`,
`settings-open-failed`, and `operation-failed`. Expected failures keep the
authenticated socket open.

## Recovery behavior

- No active default input: disable Voice Typing and offer input settings.
- Software muted: show **Unmute** and input settings.
- Level zero: keep the slider available and offer input settings.
- Peak test inconclusive: allow retry, settings recovery, or opening Voice
  Typing; do not label the device broken.
- Meter unavailable or exclusive-mode interference: preserve known endpoint
  status and explain the test limitation behind the information glyph.
- Privacy or Speech Services setup failure: offer the fixed microphone privacy
  page and explain that the remaining consent flow belongs to Windows.
- Permission denied: show **Disabled on PC** without suggesting that changing a
  microphone setting will bypass the host permission.

## Validation

- Protocol tests cover exact schemas, undeclared and conditional fields, bounds,
  alpha omission and command enforcement, global and per-device permission
  resolution, correlated results, one-operation ownership, disconnect cleanup,
  and honest shortcut success wording.
- Native boundary tests cover present, missing, and unavailable endpoints; mute
  and level reads; explicit unmute; level setting; detected and inconclusive
  peak tests; COM failure; cancellation; and deterministic cleanup. Existing
  output audio tests must remain green.
- Mobile tests cover both path orderings, supported/blocked/omitted states,
  compact copy, accessible information disclosure, slider commit behavior,
  pending operations, reconnect, timeout, and recovery actions across portrait,
  landscape, light, and dark layouts.
- Automated host protocol tests use `TestServer` and isolated registry settings;
  they open no configured port, create no firewall rule, and never access
  production settings.
- Manual Windows 11 validation covers no microphone, unplugged or disabled
  input, software and hardware mute, zero level, exclusive capture, the
  first-use Microsoft Speech Services prompt, Voice Typing configured to another
  microphone, host-window focus, a higher-integrity foreground application, and
  both Settings destinations.

## Documentation boundary

When implemented, update the current feature inventory, protocol, architecture,
privacy, troubleshooting, and security-boundary diagrams. State clearly that
Voltura Air does not capture microphone audio and that optional Windows Voice
Typing uses Microsoft's online speech service.

Do not promote this alpha-gated path as a normal capability in the public root
README, marketing site, or machine-readable public summary. Reconsider those
surfaces only if the alpha gate is removed.

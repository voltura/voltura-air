# Security architecture diagrams

These diagrams summarize the security-sensitive runtime, pairing, authorization,
and release flows. The SVG files in `docs/diagrams/` are the viewable graphics;
the Mermaid blocks below keep the same flows easy to review in text. `docs/architecture.md`
remains the subsystem authority and `docs/protocol.md` remains the wire-contract
authority.

## Viewable graphics

- [Runtime data flow](diagrams/security-runtime-flow.svg)
- [Pairing and reconnect flow](diagrams/security-pairing-reconnect.svg)
- [Authorization decision path](diagrams/security-authorization-path.svg)
- [Release and artifact-production flow](diagrams/security-release-flow.svg)

## Runtime data flow

```mermaid
flowchart LR
  Client["PWA / browser client\nuntrusted UI and private-key storage"] -->|"HTTP app shell"| HostWeb["Windows host ASP.NET\nnormal mode: 0.0.0.0:selected port\ntest mode: 127.0.0.1"]
  Client -->|"WebSocket /ws\npair.hello then commands"| Session["WebSocketSessionHandler\nOrigin check, authentication,\nmessage validation"]
  Session --> Pairing["PairingManager\nshort-lived QR tokens\npaired-device records"]
  Pairing --> Store[("pairing.json\nreconnect public keys\npermission overrides")]
  Session --> Policy["HostStatusPayloadFactory\nhost + per-device permissions"]
  Session --> Handlers["Focused command handlers\ninput, text, clipboard,\nlaunch, URL, power, awake"]
  Handlers --> Windows["Windows user session\nSendInput, clipboard,\nprocess launch, power APIs"]

  Internet["Internet-origin website"] -. "browser WebSocket attempt\nOrigin is untrusted input" .-> Session
  Lan["LAN attacker"] -. "can reach listener if network allows" .-> HostWeb
  Local["Local user / same-user malware"] -. "can read user profile unless OS account is trusted" .-> Store
```

## Pairing and reconnect flow

```mermaid
sequenceDiagram
  participant Host as Windows host
  participant QR as QR/link
  participant Client as PWA client
  participant Store as Pairing store

  Host->>QR: Create short-lived pairToken
  QR->>Client: /pair?t=pairToken&v=version&h=host
  Client->>Client: Generate P-256 key pair
  Client->>Host: pair.hello(clientId, deviceName, pairToken, reconnectPublicKey)
  Host->>Host: Validate and consume current/overlap token
  Host->>Store: Store clientId + reconnect public key
  Host->>Client: pair.accepted (no credential)
  Note over Client: Keep private key in browser storage

  Client->>Host: pair.hello(clientId, deviceName)
  Host->>Store: Load registered public key
  Host->>Client: pair.challenge(clientId, challenge)
  Client->>Client: Sign session challenge with private key
  Client->>Host: pair.proof(clientId, signature)
  Host->>Host: Consume challenge, then verify signature
  Host->>Client: pair.accepted (no credential)

  Host-->>Client: Revocation closes active sockets
```

## Authorization decision path

```mermaid
flowchart TD
  Frame["Authenticated WebSocket frame\nuntrusted JSON"] --> Validate["ClientMessageValidator\nknown type, bounded fields"]
  Validate --> Dispatch["WebSocketSessionHandler\nsingle dispatch point"]

  Dispatch --> Input["pointer.* / keyboard.*"]
  Input --> RemoteInput{"AllowRemoteInput\nhost + per-device"}
  RemoteInput -- "false" --> InputDenied["input.error\nVAIR-INPUT-DENIED"]
  RemoteInput -- "true" --> SendInput["InputCommandHandler\nInputDispatcher\nSendInput"]

  Dispatch --> Text["text.send"]
  Text --> TextPerm{"AllowRemoteInput"}
  TextPerm -- "false" --> TextDenied["text.send.result\nVAIR-TEXT-DENIED"]
  TextPerm -- "true" --> TextSink["TextDestinationService"]

  Dispatch --> Privileged["launch / URL / clipboard / power / awake / presentation / audio"]
  Privileged --> SpecificPerm{"Specific host + per-device permission"}
  SpecificPerm -- "false" --> RecoverableDeny["Recoverable denied result"]
  SpecificPerm -- "true" --> WindowsAction["Focused Windows API or allowlisted process action"]
```

## Release and artifact-production flow

```mermaid
flowchart LR
  Source["Prepared main commit\nroot package version bumped"] --> Workflow["GitHub Actions\nPublish Voltura Air release"]
  Workflow --> Guard["Compare current and previous\npackage.json versions"]
  Guard -->|"version changed"| Test["npm test\nhost tests\nscript/doc checks"]
  Guard -->|"version unchanged"| Skip["Exit successfully\nno build or publication"]
  Test --> Package["package-win.ps1\nmobile build\n.NET publish\ncursor watchdog\nNSIS installers"]
  Package --> Artifacts["ZIP + installers\nartifacts/publish"]
  Artifacts --> Draft["Draft GitHub release\nmanual review before publish"]

  Actions["Third-party Actions"] -. "supply-chain trust" .-> Workflow
  Tooling["NSIS / build tooling"] -. "release-time tool trust" .-> Package
  Reviewer["Maintainer"] --> Draft
```

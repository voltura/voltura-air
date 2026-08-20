# Third-party software notices

Voltura Air is open source under the [MIT License](LICENSE), but that license
does not replace the licenses of third-party components used by the product.
We are grateful to their authors and contributors.

The notices below identify production runtime components. Build, test, and
maintenance-only dependencies remain identified by their package manifests and
lock files and are not included in distributed application code.

No third-party author, contributor, project, or vendor listed here endorses or
is affiliated with Voltura Air or Voltura AB. Third-party software is provided
under its own license and warranty disclaimer. Voltura AB offers no warranty or
liability on behalf of a third-party contributor.

## Native WebRTC screen transport

The Windows host distributes a Voltura-built `datachannel.dll` for Screen
viewing. The current binary has SHA-256
`88cba93015800e9c33dd0824d68629a5ef8c1d5f50d4fdd836a2c8df69d94e1b`.
Embedded build paths identify libdatachannel v0.24.5 and its bundled libjuice,
libsrtp, usrsctp, and plog sources; the binary identifies OpenSSL 3.6.3. The
DLL was compiled from a clean upstream checkout without changes to
libdatachannel or libjuice source files. Its tracked rebuild recipe is
[`scripts/build-libdatachannel.ps1`](scripts/build-libdatachannel.ps1).

The corresponding upstream libdatachannel source is available from the
[v0.24.5 source tag](https://github.com/paullouisageneau/libdatachannel/tree/v0.24.5),
commit `443f6934d9007eb7076ab7825ba330f355fcbead`, including the exact submodule
commits recorded in the distributed `SOURCE.txt`. The full MPL 2.0 texts and
dependency license texts distributed with every Windows artifact are in
[`apps/windows-host/ThirdPartyNotices/libdatachannel`](apps/windows-host/ThirdPartyNotices/libdatachannel).

| Component | Use | License | Source |
| --- | --- | --- | --- |
| libdatachannel 0.24.5 | Native WebRTC peer, RTP media, DTLS-SRTP, and data channel | MPL 2.0 | [paullouisageneau/libdatachannel](https://github.com/paullouisageneau/libdatachannel/tree/v0.24.5) |
| libjuice | ICE, STUN, and TURN inside `datachannel.dll` | MPL 2.0 | [paullouisageneau/libjuice](https://github.com/paullouisageneau/libjuice) |
| libsrtp | SRTP inside `datachannel.dll` | BSD 3-Clause | [cisco/libsrtp](https://github.com/cisco/libsrtp) |
| OpenSSL 3.6.3 | TLS and cryptography inside `datachannel.dll` | Apache 2.0 | [openssl/openssl](https://github.com/openssl/openssl/tree/openssl-3.6.3) |
| usrsctp | SCTP data channels inside `datachannel.dll` | BSD 3-Clause | [paullouisageneau/usrsctp](https://github.com/paullouisageneau/usrsctp) |
| plog | Native logging support inside `datachannel.dll` | MIT | [SergiusTheBest/plog](https://github.com/SergiusTheBest/plog) |
| nlohmann JSON | Bundled libdatachannel build dependency retained in its notice set | MIT | [nlohmann/json](https://github.com/nlohmann/json) |

## Windows host runtime

Every Windows artifact carries the applicable full license and notice text in
its `ThirdPartyNotices` directory. The full self-contained artifact also carries
the .NET runtime license and Microsoft third-party notices produced by the .NET
SDK used for that release.

| Component | Version | Use | License/source |
| --- | --- | --- | --- |
| Microsoft .NET, ASP.NET Core, and Windows Desktop runtime | 10.0 release family | Self-contained Windows runtime and local web host | Microsoft .NET redistribution terms and bundled third-party notices; source at [dotnet/runtime](https://github.com/dotnet/runtime) and [dotnet/aspnetcore](https://github.com/dotnet/aspnetcore) |
| Microsoft WebView2 SDK | 1.0.4129.50 | Host WebView integration and loader | BSD 3-Clause; included Microsoft license and NOTICE |
| QRCoder | 1.8.0 | Pairing QR generation | [MIT; codebude/QRCoder](https://github.com/codebude/QRCoder/tree/v1.8.0) |
| Vortice.Windows | 3.8.3 | Direct3D, DXGI, D3DCompiler, and Media Foundation bindings | [MIT; amerkoleci/Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows) |
| Vortice.Mathematics | 2.1.1 | Pinned Vortice runtime support | [MIT; amerkoleci/Vortice.Mathematics](https://github.com/amerkoleci/Vortice.Mathematics) |
| SharpGen.Runtime and SharpGen.Runtime.COM | 2.4.2-beta | Transitive native interop support | [MIT; SharpGenTools/SharpGenTools](https://github.com/SharpGenTools/SharpGenTools) |
| Concentus | 2.2.2 | Managed Opus decoding for optional Phone webcam audio | [BSD-style Opus licence; lostromb/concentus](https://github.com/lostromb/concentus/tree/6c2328dc19044601e33a9c11628b8d60e1f3011c) |
| NAudio.Wasapi and NAudio.Core | 2.3.0 | Windows Core Audio endpoint discovery and PCM output | [MIT; naudio/NAudio](https://github.com/naudio/NAudio/tree/c89fee940ee6f8d7374d18714a6b85d8b7a18ab0) |

## Mobile web application

The PWA includes `third-party-notices.txt` at its application root. That file is
generated from the exact installed production packages and contains the full
license text and copyright notices.

| Component | Version | Use | License/source |
| --- | --- | --- | --- |
| noble-curves and noble-hashes | 2.3.0 | Pairing and relay-session cryptography fallback | [MIT; paulmillr/noble-curves](https://github.com/paulmillr/noble-curves) and [paulmillr/noble-hashes](https://github.com/paulmillr/noble-hashes) |
| jsQR | 1.4.0 | Pairing QR decoding | [Apache 2.0; cozmo/jsQR](https://github.com/cozmo/jsQR) |
| Lucide React | 1.31.0 | User-interface icons | [ISC and derived Feather icons under MIT; lucide-icons/lucide](https://github.com/lucide-icons/lucide) |
| React, React DOM, and Scheduler | 19.2.8 / 0.27.0 | Mobile user interface runtime | [MIT; facebook/react](https://github.com/facebook/react) |

## Relay service

The Cloudflare relay implementation is Voltura Air source. Cloudflare is the
configured hosting and TURN provider, not the author of Voltura Air. The
advanced standalone Node.js adapter uses `ws` 8.21.3 under the MIT License;
its package distribution includes the upstream license from
[websockets/ws](https://github.com/websockets/ws).

If a shipped component or required notice appears to be missing, please report
it through the [security policy](SECURITY.md) or the repository issue tracker.

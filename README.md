# Voltura Air

<p align="center">
  <img src="apps/windows-host/Assets/VolturaAir-256.png" alt="Voltura Air application icon" width="128">
</p>

Turn a phone, tablet, or touch browser into a wireless remote for a Windows 11
PC. No app-store install, account, subscription, or cloud relay is required.

## What you can do

- Use a phone or tablet as a wireless trackpad and keyboard.
- Control presentations, use a laser pointer, track time, and review saved
  reports on the PC.
- Dictate, reuse snippets, and send text to a PC app, document, email draft, or
  clipboard.
- Control media, volume, browser tabs, windows, and applications selected on
  the PC.
- Keep the PC awake, lock it, blank its displays, restart it, or shut it down.
- Combine a keyboard and trackpad on a landscape tablet.

See the [complete implemented feature list](docs/features.md).

## Screenshots

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/site/assets/voltura-air-host-dark.png">
    <img src="docs/site/assets/voltura-air-host.png" alt="Voltura Air Windows host pairing screen" width="900">
  </picture>
  <br>
  <sub>Windows host pairing screen</sub>
</p>

<table>
  <tr>
    <td align="center" width="34%">
      <picture>
        <source media="(prefers-color-scheme: dark)" srcset="docs/site/assets/voltura-air-iphone-dark.png">
        <img src="docs/site/assets/voltura-air-iphone.png" alt="Voltura Air trackpad on a phone" width="320">
      </picture>
      <br>
      <sub>Phone trackpad</sub>
    </td>
    <td align="center" width="66%">
      <img src="docs/site/assets/voltura-air-split.png" alt="Voltura Air split keyboard and trackpad on a landscape tablet">
      <br>
      <sub>Landscape split keyboard and trackpad</sub>
    </td>
  </tr>
</table>

## Download and install

Voltura Air requires Windows 11. Choose one package from the
[latest GitHub release](https://github.com/voltura/voltura-air/releases/latest):

- **Standard installer:** `VolturaAir-Setup-<version>-win-x64.exe` downloads
  missing .NET 10 Windows Desktop/ASP.NET Core runtimes and may need internet or
  administrator approval.
- **Full installer:** `VolturaAir-Setup-<version>-win-x64-full.exe` includes
  those runtimes.
- **Portable:** `VolturaAir-<version>-win-x64.zip`.

Installers are per-user under `%LOCALAPPDATA%\Programs\Voltura Air`, create
Start Menu shortcuts, and retain pairing/settings under `%APPDATA%\Voltura Air`
on uninstall. Start-at-sign-in is an in-app setting.

## Connect

1. Install or extract Voltura Air and start it on the PC.
2. Open **Connect**.
3. Scan the QR code from a phone or tablet on the same Wi-Fi or LAN.

Paired devices are remembered until removed or their browser data is cleared.

## Trust, privacy, and distribution

Voltura Air is intended for trusted devices on a local network. It is not a
remote-desktop service, public-internet relay, file-sync product, or remote wake
solution for a sleeping or shut-down PC.

Voltura Air is freeware from Voltura AB and is open source under the
[MIT License](LICENSE). It can be used without payment, registration, trial
limits, or feature locks.

Release binaries are not code-signed. Windows can therefore show an
unknown-publisher or Microsoft Defender SmartScreen warning. Download only from
the [official product page](https://voltura.se/air/) or the
[official GitHub releases](https://github.com/voltura/voltura-air/releases/latest).

[Privacy policy](PRIVACY.md) · [Security policy](SECURITY.md)

Do not publish vulnerability details or pairing credentials in a public issue.

## Support

Support is optional:

- [Ko-fi](https://ko-fi.com/voltura)
- [PayPal](https://www.paypal.me/voltura)

## Develop from source

Requirements: Node.js/npm, .NET 10 SDK, and Visual Studio Build Tools with the
**Desktop development with C++** workload.

```powershell
git clone https://github.com/voltura/voltura-air.git
cd voltura-air
npm ci
npm run dev
```

- [Contributing](CONTRIBUTING.md)
- [Development workflows and validation](docs/setup.md#development-workflows)
- [Documentation map](docs/README.md)

## Statistics

[![Visitors](https://hits.sh/github.com/voltura/voltura-air.svg?style=flat&label=visitors&labelColor=555&color=5690f2&extraCount=19)](https://hits.sh/github.com/voltura/voltura-air/)
[![Code size](https://img.shields.io/github/languages/code-size/voltura/voltura-air)](https://github.com/voltura/voltura-air)
[![Stars](https://img.shields.io/github/stars/voltura/voltura-air)](https://github.com/voltura/voltura-air/stargazers)
[![Forks](https://img.shields.io/github/forks/voltura/voltura-air)](https://github.com/voltura/voltura-air/forks)
[![Last commit](https://img.shields.io/github/last-commit/voltura/voltura-air?color=red)](https://github.com/voltura/voltura-air/commits)
[![Languages](https://img.shields.io/github/languages/count/voltura/voltura-air)](https://github.com/voltura/voltura-air)
[![Top language](https://img.shields.io/github/languages/top/voltura/voltura-air)](https://github.com/voltura/voltura-air)

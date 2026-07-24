# Contributing

Thanks for considering a contribution to Voltura Air.

Voltura Air is a small freeware project from Voltura AB. Contributions should keep the app simple, trustworthy, and useful as a local-network PC remote.

## Good first contributions

- Bug reports with clear reproduction steps.
- UI polish that keeps the app simple on phones, tablets, and Windows.
- Documentation fixes.
- Small accessibility improvements.
- Security hardening for pairing, device trust, and local-network behavior.

## Before opening a pull request

1. Open an issue first for larger feature changes.
2. Keep pull requests focused on one change.
3. Update documentation when behavior changes.
4. Run the smallest checks for the changed risk boundary; use the
   [validation matrix](docs/setup.md#validation-by-change).

Install exact dependencies with `npm ci`; use `npm install` only when changing
dependency manifests.

## Release model

Voltura Air is distributed as freeware. Do not add licensing, payment, trial, telemetry, account, or cloud-relay behavior unless it has been explicitly accepted for the project.

## License of contributions

By contributing to this repository, you agree that your contribution is licensed under the MIT License used by the project.

No separate contributor license agreement is required.

## Code of conduct

All project activity must follow [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## Security reports

Do not report vulnerabilities with exploit details in public issues. Follow [SECURITY.md](SECURITY.md) instead.

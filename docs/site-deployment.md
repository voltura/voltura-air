# Site deployment

Publish `docs/site` to `https://voltura.se/air/`. Claims come from
[features](features.md), [setup](setup.md), [release](release.md), and
[security](../SECURITY.md).

## Publish

```powershell
npm run publish:site:password
npm run publish:site:list
npm run publish:site
```

The password prompt is hidden and DPAPI-encrypted for the current Windows user
outside Git at `%LOCALAPPDATA%\Voltura Air`. Remove it with
`npm run publish:site:password:clear`; never store it in files or logs.

Publication regenerates `stats.html` and uploads `docs/site` to `air`. It pins
server identity, overwrites matching files, adds new files, and retains
remote-only files. `publish:site:list` is read-only.

## Public-copy contract

- Lead with mobile control of a Windows PC.
- Show a few recognizable current use cases; omit implementation/secondary
  controls.
- Label gated alpha behavior and its default accurately.
- Make only authority-backed security/performance claims.
- Keep release links, package labels, screenshots, `index.php`, and `llms.txt`
  aligned.
- Never imply remote wake, internet relay, signed binaries, or disabled
  capability availability.

Catalog/link changes run `npm run docs:check`.

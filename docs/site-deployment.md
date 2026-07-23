# Voltura Air Site Deployment

The public product page lives in `docs/site`.

Target URL:

```text
https://voltura.se/air/
```

Upload the contents of `docs/site` to an `air` directory in the web root for `voltura.se`.
Source product claims from `features.md`, installation facts from `setup.md`,
publication behavior from `release.md`, and security claims from `SECURITY.md`.
Keep the page focused on core use cases.

## Publishing

Set up the SFTP password once, then publish whenever needed:

```powershell
npm run publish:site:password
npm run publish:site:list
npm run publish:site
```

The setup command hides your typed password and saves it encrypted with Windows
DPAPI for your current Windows account. Paste a long password with `Ctrl+V`;
the prompt reads it from the Windows clipboard without displaying it. It is
stored outside the repository in `%LOCALAPPDATA%\Voltura Air`; do not add the
password to a file, `package.json`, Git, or a CI log. Remove it with
`npm run publish:site:password:clear`.

Publishing first refreshes `docs/site/stats.html` without opening it locally or
printing the statistics table. It then uploads the contents of `docs/site` over SFTP to `ssh.voltura.se:22`
as `voltura.se`, targeting the `air` folder relative to the one.com SFTP login
directory. The first successful connection records the server identity locally;
later publishes stop if it changes. The command overwrites matching remote files
and uploads new files and folders; it intentionally does not delete remote-only
files.

Run `npm run publish:site:list` at any time to inspect the current top-level
contents of `air` without changing remote files.

Keep release links and feature copy aligned with the latest GitHub release and
installed app behavior.

Before editing public copy:

- lead with the primary job: controlling the Windows PC from a mobile device;
- present only a small set of recognizable core capabilities and use cases;
- omit secondary controls and implementation details;
- identify capability-gated alpha work accurately and state whether it is
  enabled by default;
- avoid absolute or performance claims unless the applicable authority and
  production behavior support them; and
- verify that every factual statement can be traced to `features.md`,
  `setup.md`, `release.md`, `SECURITY.md`, or the current implementation.

When connection behavior changes, update `docs/site/index.php`,
`docs/site/llms.txt`, and screenshots or copy before deploying the site.

Run `npm run docs:check` after adding, removing, or renaming a public page or
`llms.txt` entry cataloged in the [documentation map](README.md).

Before deployment, compare the public copy, package labels, links, and
screenshots with the current release. Do not imply remote wake from Windows
sleep, an internet relay, signed binaries, or availability of disabled
capabilities.

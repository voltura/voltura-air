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

Keep release links and feature copy aligned with the latest GitHub release and
installed app behavior.

Before editing public copy:

- lead with the primary job: controlling the Windows PC from a mobile device;
- present only a small set of recognizable core capabilities and use cases;
- omit secondary controls and implementation details;
- do not promote default-off alpha work as a normal product capability;
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
sleep, an internet relay, signed binaries, or availability of default-off alpha
features.

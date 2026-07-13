# Voltura Air Site Deployment

The public product page lives in `docs/site`.

Target URL:

```text
https://voltura.se/air/
```

Upload the contents of `docs/site` to an `air` directory in the web root for `voltura.se`.
The repository can stay private because the hosted site is just static PHP/HTML, CSS, and public image assets.

The page is a product/download page for the current Windows app. Keep release links and feature copy aligned with the latest GitHub release and installed app behavior.


When connection behavior changes, update `docs/site/index.php`, `docs/site/llms.txt`, and screenshots/copy before deploying the site. The public site should describe the current product behavior, not branch history.

Before deployment, compare the public copy with the current host and mobile UI. In particular, keep Power permission defaults, Lock PC policy handling, Blackout display behavior, conditional screen-saver availability, the Modern Standby limitation of the native display-off action, the opt-in application log, and the current Preferences/Diagnostics navigation accurate. Do not imply remote wake from Windows sleep, elevation, cloud logging, or automatic Windows sign-in changes.

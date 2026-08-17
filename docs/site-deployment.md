# Site deployment

Publish `apps/public-site` to `https://voltura.se/air/`. Claims come from
[features](features.md), [setup](setup.md), [release](release.md), and
[security](../SECURITY.md).

## Production publication boundary

The public repository owns all website, hosted-PWA, `/a`, and `/s` source plus local
validation and generation. The private `voltura-air-service` repository owns the
voltura.se SFTP endpoint, pinned server identity, protected credential, upload logic,
and release ordering. Public source commands never upload the production site.

## Custom-screen catalog

The catalog lives under `apps/public-site/screens` and requires PHP sessions plus a
MySQL database. This release requires a fresh database created from
`apps/public-site/screens/schema.sql`; application code has no schema migration
or legacy-format reader. Configure the ignored hosting-only
`apps/public-site/config.php` with `dsn`, `username`, `password`, `storage_path`,
and a high-entropy `catalog_secret`. The secret HMACs persistent email/source
rate-bucket keys and must remain stable. The site `.htaccess` blocks direct access
to that file, SQL schema files, and `.volturascreen` packages. Site publication
uploads `.htaccess` before the full directory, including the ignored config
file. Never commit database credentials or uploaded package files.

Accounts require email verification through one hashed 24-hour pending token.
Login, registration, and resend limits use persistent HMAC-keyed buckets and
`REMOTE_ADDR`; forwarded headers are not trusted. Catalog package files are
content-addressed. Upload/delete/import transactions enqueue exact-basename and
SHA-256 cleanup jobs; ordinary mutations drain a bounded number. Schedule or run
`php apps/public-site/screens/maintenance.php` for larger idempotent drains.
Missing files complete a job; referenced or hash-mismatched files remain intact.

Create the first administrator by changing the account's `role` to `admin`
after registration. Uploads remain pending until approved. The Windows app's
installer registers the `voltura-air://` protocol; catalog links still provide
a normal download fallback.

Generate the official collection with `npm run screens:official`. After every
included target passes the current Windows 11 smoke matrix, an administrator
uploads `artifacts/custom-screens/voltura-official-screens.zip` through
**Moderate** and explicitly confirms that matrix. The importer validates the
whole manifest and every exact-format package before one locked reconciliation;
any failure rejects the bundle. Provenance is unique by source/official ID,
user-owned screen-ID collisions reject the import, and stable official rows
preserve package IDs, ratings, and download counters. Only absent rows carrying
Voltura provenance are removed, with superseded files passed to the cleanup queue.

### Local development

Local catalog development requires Windows Package Manager (`winget`), PHP
8.5 with PDO MySQL, and MariaDB. Production uses one.com's **Latest stable**
PHP setting and must report PHP 8.5 before publication. The hosted one.com database does not
accept remote connections, so local development uses a separate local database
and never the production credentials.

Run the one-time initializer from an interactive PowerShell terminal:

```powershell
npm run site:dev:init
```

The initializer installs missing PHP and MariaDB packages through `winget`.
MariaDB installation is interactive so the developer controls its local root
password. Finish that installer before returning to the initializer, then enter
the same root password when prompted. The command creates the `voltura_air_dev` database and restricted
development user, verifies or applies the current fresh catalog schema, and
writes ignored configuration and package storage under `.site-dev`. It is safe
to rerun. Pass a non-default MariaDB port when needed with
`npm run site:dev:init -- -Port 3307`.

Run `npm run site:check` to verify the PHP runtime, required extensions, and
syntax of every maintained PHP entry point.

Start the local site with:

```powershell
npm run site:dev
```

After creating a local catalog account, promote it to administrator with:

```powershell
npm run site:dev:admin -- you@example.com
```

Sign out and sign in again after promotion so the session receives the new
role. Administrators then see the catalog's **Moderate** navigation link.

This serves `apps/public-site` at `http://127.0.0.1:8765/` and enables the loopback-only
session-cookie override needed for HTTP development. MariaDB must be running.
Use `npm run site:dev -- -Port 8766` to select another web port. Production
continues to require Secure session cookies and `apps/public-site/config.php`.

After initializing the isolated local catalog, run
`npm run test:site-import-integration`. It exercises all official-import write
and rollback boundaries against local MariaDB, verifies stable row IDs plus
download/rating preservation, and removes its test rows and files.

## Public-copy contract

- Lead with mobile control of a Windows PC.
- Show a few recognizable current use cases; omit implementation/secondary
  controls.
- Describe experimental features and their named, feature-owned toggles
  accurately.
- Make only authority-backed security/performance claims.
- Keep release links, package labels, screenshots, `index.php`, and `llms.txt`
  aligned.
- Never imply remote wake, signed binaries, or disabled capability availability.

Catalog/link changes run `npm run docs:check`.

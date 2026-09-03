# Site deployment

Publish `apps/public-site` to `https://voltura.se/air/`. Claims come from
[features](features.md), [setup](setup.md), [release](release.md), and
[security](../SECURITY.md).

## Production publication boundary

The public repository owns all website, hosted-PWA, `/a`, `/s`, and `/d` source plus local
validation and generation. The private `voltura-air-service` repository owns the
voltura.se SFTP endpoint, pinned server identity, protected credential, upload logic,
and release ordering. Public source commands never upload the production site.

`/a` and `/s` load the release-owned `/air/app/` build. Development host runs
use `/d`, which loads `/air/dev-app/`; building or publishing that scope must not
replace `/air/app/`, `/a`, or `/s`. Build it with
`npm run site:hosted:dev-build`. The private service repository owns its
credentialed `/d`-only publication command.

## Custom-screen catalog

The catalog lives under `apps/public-site/screens` and requires PHP sessions plus a
MySQL database. Create a fresh database from `apps/public-site/screens/schema.sql`.
For an existing catalog, apply the forward-only, idempotent
`apps/public-site/screens/schema-upgrade.sql` before publishing matching PHP; it
adds retention timestamps, cleanup state, and supporting indexes without dropping
or rewriting existing catalog content. Configure the ignored hosting-only
`apps/public-site/config.php` with `dsn`, `username`, `password`, `storage_path`,
and a high-entropy `catalog_secret`. The secret HMACs persistent email/source
rate-bucket keys and must remain stable. The site `.htaccess` blocks direct access
to that file, SQL schema files, and `.volturascreen` packages. Site publication
uploads `.htaccess` before the full directory, including the ignored config
file. Never commit database credentials or uploaded package files.

Accounts require email verification through one hashed 24-hour pending token.
Login, registration, resend, report, and download-count limits use persistent
HMAC-keyed buckets and `REMOTE_ADDR`; forwarded headers are not trusted. Login
failures are serialized by source and account, and a correct verified-account
password can clear a targeted account bucket when the source itself is allowed.
A fixed 100,000-operation daily gate is consumed before attacker-keyed rate rows,
and login password checks have a separate 10,000-attempt daily ceiling. Report
mail capacity is consumed only after the screen, reporter, and duplicate checks.
Catalog package files are
content-addressed. Upload/delete/import transactions enqueue exact-basename and
SHA-256 cleanup jobs; ordinary mutations drain a bounded number. Schedule or run
`php apps/public-site/screens/maintenance.php` for larger idempotent drains. The
same bounded maintenance obtains a five-minute lease during public rate-controlled
traffic. It expires report rows after 180 days, expired verification tokens after
a seven-day grace, never-verified accounts after 30 days without a live token or
package, and removed packages after 30 days through the durable package-file queue.
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
ordinary uploads cannot claim the reserved `official.` namespace, and legacy
user-owned screen IDs do not block reconciliation. Stable official rows
preserve package IDs, ratings, and download counters. Only absent rows carrying
Voltura provenance are removed, with superseded files passed to the cleanup queue.

## Usage statistics service

Usage statistics use the additive, idempotent
`apps/public-site/telemetry/schema.sql` in the same MariaDB database. It creates
only `air_telemetry_daily`, `air_telemetry_batches`,
`air_telemetry_rate_buckets`, `air_telemetry_ingest_daily`, and
`air_telemetry_maintenance`; it neither changes nor drops Custom Screens tables.
This schema is independent of the catalog upgrade. There is no production
migration runner and no destructive down migration.

The PHP endpoint reuses the existing ignored `dsn`, `username`, `password`, and
stable `catalog_secret`. Domain-separated HMAC inputs prevent a telemetry
pseudonym or source bucket from matching a catalog rate key. Do not add a second
database account or embed another application secret. Public ingest lives under
`telemetry/v1` and does not load catalog sessions or administrator cleanup.
`screens/telemetry.php` separately reuses the existing administrator role,
session, CSRF, layout, and theme for aggregate reporting and bounded cleanup.

The hosted one.com database is reachable only from hosted PHP. For a release
that first introduces these tables, review the exact committed SQL and import
that unchanged file through one.com/phpMyAdmin after the release candidate and
site snapshot are audited but before `release:full`. Save the import result. Do
not edit SQL in the control panel. A failed import stops the release; correct
and re-review additive SQL, then rerun it. Do not drop catalog tables or invent
rollback SQL. The inert telemetry tables may remain if publication is aborted.

After the private workflow uploads the prepared site, it requires:

1. `GET /air/telemetry/v1/health.php` to return empty `204`, proving the hosted
   PHP can read the required tables, columns, and maintenance state; and
2. one exact intentionally invalid POST to `ingest.php` to return the generic
   `400` body, proving route and parser deployment without inserting a product
   telemetry row.

Only then may the workflow publish GitHub Latest. A health or parser failure
stops on the same pinned public commit; fix that boundary and rerun without
rebuilding or advancing the version.

Daily aggregate and delivery-health rows expire after 180 days; batch and rate
buckets expire after 24 hours. Ingest and administrator GET access acquire a
singleton one-minute lease and delete at most 500 eligible rows per table per pass.
The 50,000 accepted-batch and 100,000 total-request daily ceilings keep maximum
public row creation below that automatic cleanup capacity.
After complete service inactivity, expired rows remain excluded from dashboard
queries and physical cleanup resumes on the next ingest or administrator
access. Administrator retention/date/delete-all actions preview exact counts,
recheck them under the transaction, delete at most 1,000 rows per request, and
continue explicitly until done. Delete-all clears the four data tables and
resets only the telemetry maintenance row; it does not drop schema.

## Local site development

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
development user, creates the catalog schema when absent, applies its additive
upgrade when present, and
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
role. Administrators then see the catalog's **Moderate** and **Usage
statistics** navigation links.

This serves `apps/public-site` at `http://127.0.0.1:8765/` and enables the loopback-only
session-cookie override needed for HTTP development. MariaDB must be running.
Use `npm run site:dev -- -Port 8766` to select another web port. Production
continues to require Secure session cookies and `apps/public-site/config.php`.

After initializing the isolated local catalog, run
`npm run test:site-catalog-integration` and
`npm run test:site-import-integration`. The first verifies bounded retention,
rollback, and durable file cleanup; the second exercises all official-import
write and rollback boundaries against local MariaDB, verifies stable row IDs plus
download/rating preservation, and removes its test rows and files.

The initializer applies and verifies the additive telemetry schema on every
run, even when the catalog schema already exists. Before any telemetry release,
run `npm run test:site-telemetry-integration` against this configured PHP
8.5/MariaDB environment. It owns uniquely identifiable fixtures and verifies
closed validation, binary HMAC storage, daily upsert/saturation, duplicate
idempotency, all rate dimensions, transaction rollback, bounded retention,
dashboard queries, preview equality, cleanup rollback/continuation, indexes,
and unchanged Custom Screens sentinel counts before removing its fixtures.

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

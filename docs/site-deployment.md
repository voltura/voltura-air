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

Publication rebuilds the catalog preview and hosted Relay PWA, regenerates
`stats.html`, uploads `docs/site` to `air`, and uploads the first-party short
redirect under the website-root `a` path. It pins
server identity, overwrites matching files, adds new files, and retains
remote-only files. `publish:site:list` is read-only.

## Custom-screen catalog

The catalog lives under `docs/site/screens` and requires PHP sessions plus a
MySQL database. Apply `docs/site/screens/schema.sql` once, then configure the
ignored hosting-only `docs/site/config.php` with `dsn`, `username`, `password`,
and `storage_path`. The site `.htaccess` blocks direct access to that file, SQL
schema and migration files, and `.volturascreen` packages. Site publication
uploads `.htaccess` before the full directory, including the ignored config
file. Never commit database credentials or uploaded package files.

Sites initialized before catalog ratings were added must import
`docs/site/screens/migration-002-ratings.sql` once in phpMyAdmin. Fresh databases
created from the current `schema.sql` already contain that table and must not
run the migration separately.

Sites initialized before reviewer feedback was added must run this once in
phpMyAdmin; no repository migration file is required:

```sql
ALTER TABLE air_screen_packages
ADD COLUMN rejection_feedback VARCHAR(1000) NULL AFTER status;
```

Create the first administrator by changing the account's `role` to `admin`
after registration. Uploads remain pending until approved. The Windows app's
installer registers the `voltura-air://` protocol; catalog links still provide
a normal download fallback.

### Local development

Local catalog development requires Windows Package Manager (`winget`), PHP
8.2 or newer with PDO MySQL, and MariaDB. The hosted one.com database does not
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
development user, applies the catalog schema and outstanding migrations, and
writes ignored configuration and package storage under `.site-dev`. It is safe
to rerun. Pass a non-default MariaDB port when needed with
`npm run site:dev:init -- -Port 3307`.

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

This serves `docs/site` at `http://127.0.0.1:8765/` and enables the loopback-only
session-cookie override needed for HTTP development. MariaDB must be running.
Use `npm run site:dev -- -Port 8766` to select another web port. Production
continues to require Secure session cookies and `docs/site/config.php`.

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

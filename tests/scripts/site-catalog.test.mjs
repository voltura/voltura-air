import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");

test("catalog ratings require an authenticated user and CSRF token", () => {
  const endpoint = read("docs/site/screens/rate.php");
  assert.match(endpoint, /air_screen_require_user\(\)/u);
  assert.match(endpoint, /air_screen_require_csrf\(\)/u);
  assert.match(endpoint, /rating < 1 \|\| \$rating > 5/u);
  assert.match(endpoint, /status = 'approved'/u);
  assert.match(endpoint, /ON DUPLICATE KEY UPDATE rating = VALUES\(rating\)/u);
});

test("ratings migration enforces one bounded vote per account and screen", () => {
  const migration = read("docs/site/screens/migration-002-ratings.sql");
  assert.match(migration, /PRIMARY KEY \(package_id, user_id\)/u);
  assert.match(migration, /CHECK \(rating BETWEEN 1 AND 5\)/u);
  assert.match(migration, /REFERENCES air_screen_packages\(id\) ON DELETE CASCADE/u);
  assert.match(migration, /REFERENCES air_screen_users\(id\) ON DELETE CASCADE/u);
});

test("catalog access rules deny credentials, SQL, and screen packages", () => {
  const accessRules = read("docs/site/.htaccess");
  assert.match(accessRules, /config\\\.php/u);
  assert.match(accessRules, /\\\.sql\$/u);
  assert.match(accessRules, /\\\.volturascreen\$/u);
});

test("local site development is isolated from production configuration", () => {
  const packageJson = JSON.parse(read("package.json"));
  const initializer = read("scripts/site-dev-init.ps1");
  const launcher = read("scripts/site-dev.ps1");
  const admin = read("scripts/site-dev-admin.ps1");
  const adminPhp = read("scripts/site-dev-admin.php");
  const gitignore = read(".gitignore");
  assert.match(packageJson.scripts["site:dev:init"], /site-dev-init\.ps1/u);
  assert.match(packageJson.scripts["site:dev"], /site-dev\.ps1/u);
  assert.match(packageJson.scripts["site:dev:admin"], /site-dev-admin\.ps1/u);
  assert.match(initializer, /PHP\.PHP\.8\.4/u);
  assert.match(initializer, /MariaDB\.Server/u);
  assert.match(initializer, /Finish the MariaDB installer completely/u);
  assert.match(initializer, /MariaDB root password selected in the installer/u);
  assert.doesNotMatch(initializer, /--protocol=tcp/u);
  assert.match(initializer, /Start-Process -FilePath \$Executable/u);
  assert.match(initializer, /-RedirectStandardError \$errorPath/u);
  assert.match(initializer, /voltura_air_dev/u);
  assert.match(launcher, /VOLTURA_AIR_SCREENS_CONFIG/u);
  assert.match(launcher, /VOLTURA_AIR_SITE_DEV/u);
  assert.match(admin, /site-dev-admin\.php/u);
  assert.match(adminPhp, /UPDATE air_screen_users SET role = 'admin'/u);
  assert.match(adminPhp, /SELECT id FROM air_screen_users WHERE email = :email/u);
  assert.match(gitignore, /\/\.site-dev\//u);
});

test("five-star picker fills the hovered star and every lower star", () => {
  const view = read("docs/site/screens/view.php");
  const styles = read("docs/site/styles.css");
  assert.match(view, /for \(\$rating = 5; \$rating >= 1; \$rating--\)/u);
  assert.match(view, /onchange="this\.form\.submit\(\)"/u);
  assert.match(styles, /\.catalog-page \.star-picker label:hover ~ label/u);
});

test("uploads always use the screen name contained in the package", () => {
  const upload = read("docs/site/screens/upload.php");
  assert.match(upload, /\$name = trim\(\(string\)air_screen_value\(\$screen, 'Name'\)\)/u);
  assert.doesNotMatch(upload, /name="name"/u);
  assert.match(upload, /The screen name comes from the package\./u);
});

test("failed upload persistence removes an uncommitted package file", () => {
  const upload = read("docs/site/screens/upload.php");
  const validation = read("docs/site/screens/lib.php");
  assert.match(upload, /\$uncommittedPackagePath = \$path/u);
  assert.ok(
    upload.indexOf("$uncommittedPackagePath = $path") <
      upload.indexOf("$stmt->execute"),
  );
  assert.ok(
    upload.indexOf("$stmt->execute") <
      upload.indexOf("$uncommittedPackagePath = null", upload.indexOf("$stmt->execute")),
  );
  assert.match(upload, /@unlink\(\$uncommittedPackagePath\)/u);
  assert.match(validation, /unset\(\$screen\['AssignedClientIds'\]\)/u);
  assert.match(validation, /'packageVersion' => 1/u);
});

test("package uploads expose a visually distinct drop target and selected state", () => {
  const upload = read("docs/site/screens/upload.php");
  const styles = read("docs/site/styles.css");
  assert.match(upload, /catalog-package-drop-icon/u);
  assert.match(upload, /catalog-package-drop-action/u);
  assert.match(upload, /classList\.toggle\("has-package", hasPackage\)/u);
  assert.match(styles, /label\.catalog-package-drop[\s\S]*border: 2px dashed/u);
  assert.match(styles, /\.catalog-package-drop\.is-dragging/u);
  assert.match(styles, /\.catalog-package-drop\.has-package/u);
});

test("catalog navigation uses specific labels and detail pages link back to browse", () => {
  const library = read("docs/site/screens/lib.php");
  const index = read("docs/site/screens/index.php");
  assert.match(library, />Voltura Air<\/a>/u);
  assert.match(library, />Browse screens<\/a>/u);
  assert.match(library, />Upload screen<\/a>/u);
  assert.match(library, /href="upload\.php#submissions"[^>]*>My submissions<\/a>/u);
  assert.match(library, />Moderate screens<\/a>/u);
  assert.match(library, />Sign out<\/a>/u);
  assert.match(
    library,
    /href="\.\/" aria-label="Browse community library of custom screens"><span aria-hidden="true">&larr;<\/span> Community library<\/a>/u,
  );
  assert.match(library, /<p class="eyebrow">Community library<\/p>/u);
  assert.match(index, /air_screen_layout\('Custom screens', \$body, false\)/u);
});

test("administrators can permanently delete a listed screen after confirmation", () => {
  const index = read("docs/site/screens/index.php");
  const endpoint = read("docs/site/screens/delete.php");
  const script = read("docs/site/screens/preview.js");
  const styles = read("docs/site/styles.css");
  assert.match(index, /\$isAdmin = \(\$user\['role'\] \?\? ''\) === 'admin'/u);
  assert.match(index, /data-delete-dialog-open/u);
  assert.match(index, /class="catalog-delete-dialog"/u);
  assert.match(index, /This permanently removes the screen, ratings, reports, and downloadable package\./u);
  assert.match(endpoint, /air_screen_require_admin\(\)/u);
  assert.match(endpoint, /air_screen_require_csrf\(\)/u);
  assert.match(endpoint, /status = 'approved'/u);
  assert.match(endpoint, /beginTransaction\(\)/u);
  assert.match(endpoint, /DELETE FROM air_screen_reports/u);
  assert.match(endpoint, /DELETE FROM air_screen_ratings/u);
  assert.match(endpoint, /DELETE FROM air_screen_packages/u);
  assert.match(endpoint, /@rename\(\$stagedPath, \$packagePath\)/u);
  assert.match(script, /data-delete-dialog-open/u);
  assert.match(script, /data-delete-dialog-close/u);
  assert.match(styles, /button\.catalog-delete-button/u);
  assert.match(styles, /\.catalog-delete-dialog::backdrop/u);
});

test("submission history uses linked rows, status pills, and an empty state", () => {
  const upload = read("docs/site/screens/upload.php");
  const styles = read("docs/site/styles.css");
  assert.match(upload, /class="catalog-submissions"/u);
  assert.match(upload, /id="submissions"/u);
  assert.match(upload, /class="catalog-submission-list"/u);
  assert.match(upload, /class="catalog-submission-status ' \. \$statusClass/u);
  assert.match(upload, /No submissions yet\./u);
  assert.match(styles, /\.catalog-submissions h2[\s\S]*font-size: 1\.3rem/u);
  assert.match(styles, /\.catalog-submission-status\.is-pending/u);
  assert.match(styles, /\.catalog-submission-status\.is-approved/u);
  assert.match(styles, /\.catalog-submission-status\.is-rejected/u);
});

test("rejection requires feedback that the author can read and clear by resubmitting", () => {
  const moderation = read("docs/site/screens/admin.php");
  const upload = read("docs/site/screens/upload.php");
  const edit = read("docs/site/screens/edit.php");
  assert.match(moderation, /\$status === 'rejected' && \$feedback === ''/u);
  assert.match(moderation, /name="rejection_feedback" maxlength="1000"/u);
  assert.match(moderation, /in_array\(\$status, \['approved', 'rejected'\], true\) && \$feedback !== '' \? \$feedback : null/u);
  assert.match(upload, /Reviewer feedback:/u);
  assert.match(edit, /Reviewer feedback/u);
  assert.match(edit, /status IN \('approved', 'rejected'\) THEN 'pending'/u);
  assert.match(edit, /rejection_feedback = NULL/u);
});

test("successful uploads use a temporary accessible toast", () => {
  const upload = read("docs/site/screens/upload.php");
  const library = read("docs/site/screens/lib.php");
  const previewScript = read("docs/site/screens/preview.js");
  const styles = read("docs/site/styles.css");
  assert.match(upload, /air_screen_toast\('Screen submitted for moderation'\)/u);
  assert.match(library, /class="catalog-toast" role="status"/u);
  assert.match(library, /catalog-toast-badge/u);
  assert.match(previewScript, /history\.replaceState/u);
  assert.match(previewScript, /2400/u);
  assert.match(styles, /bottom: max\(18px/u);
  assert.match(styles, /\.catalog-toast\.dismissed/u);
});

test("production uploads notify catalog administrators after persistence", () => {
  const upload = read("docs/site/screens/upload.php");
  const library = read("docs/site/screens/lib.php");
  assert.match(upload, /air_screen_notify_moderators/u);
  assert.ok(
    upload.indexOf("$stmt->execute") <
      upload.indexOf("air_screen_notify_moderators"),
  );
  assert.match(library, /SELECT email FROM air_screen_users WHERE role = 'admin'/u);
  assert.match(library, /VOLTURA_AIR_SITE_DEV/u);
  assert.match(library, /no-reply@voltura\.se/u);
  assert.match(library, /Review submission/u);
  assert.match(library, /\/screens\/admin\.php/u);
  assert.match(library, /@mail\(/u);
});

test("moderation emails approval or rejection status to the submitter", () => {
  const moderation = read("docs/site/screens/admin.php");
  const library = read("docs/site/screens/lib.php");
  assert.match(moderation, /SELECT p\.name, u\.email/u);
  assert.match(moderation, /air_screen_notify_submitter_status/u);
  assert.ok(
    moderation.indexOf("$stmt->execute") <
      moderation.indexOf("air_screen_notify_submitter_status"),
  );
  assert.match(library, /function air_screen_notify_submitter_status/u);
  assert.match(library, /\['approved', 'rejected'\]/u);
  assert.match(library, /Reviewer feedback/u);
  assert.match(moderation, /Optional for approval &middot; Required for rejection &middot; Emailed to author/u);
  assert.match(library, /View published screen/u);
  assert.match(library, /View my submissions/u);
  assert.match(library, /VOLTURA_AIR_SITE_DEV/u);
  assert.match(library, /@mail\(\$recipient/u);
});

test("catalog previews expose full content through compact and interactive modes", () => {
  const library = read("docs/site/screens/lib.php");
  const index = read("docs/site/screens/index.php");
  const previewScript = read("docs/site/screens/preview.js");
  assert.doesNotMatch(library, /array_slice\(\$sections/u);
  assert.doesNotMatch(library, /array_slice\(\$buttons/u);
  assert.match(library, /Generic phone/u);
  assert.match(library, /Generic tablet/u);
  assert.match(library, /Scrollable screen preview/u);
  assert.match(index, /air_screen_preview\([^;]+true\)/u);
  assert.match(index, />Preview<\/a>/u);
  assert.match(previewScript, /dataset\.orientation/u);
});

test("full catalog previews use the real mobile custom-screen renderer", () => {
  const packageJson = JSON.parse(read("package.json"));
  const entry = read("apps/mobile-web/src/app/catalog-preview.tsx");
  const viteConfig = read("apps/mobile-web/vite.catalog-preview.config.ts");
  const frame = read("docs/site/screens/preview-frame.php");
  const library = read("docs/site/screens/lib.php");
  const previewScript = read("docs/site/screens/preview.js");
  const styles = read("docs/site/styles.css");
  assert.match(entry, /CustomScreenWorkspace/u);
  assert.match(entry, /actions are disabled/u);
  assert.match(frame, /\['status'\] !== 'approved'/u);
  assert.match(frame, /Content-Security-Policy/u);
  assert.match(library, /preview-frame\.php\?id=/u);
  assert.match(library, /data-width="360" data-height="640"/u);
  assert.match(library, /data-width="820" data-height="1180"/u);
  assert.match(previewScript, /iframe\.style\.width = `\$\{width\}px`/u);
  assert.match(previewScript, /iframe\.style\.transform = `scale\(\$\{scale\}\)`/u);
  assert.match(entry, /custom-screen-browser-preview control-depth/u);
  assert.match(viteConfig, /publicDir: false/u);
  assert.match(viteConfig, /emptyOutDir: true/u);
  assert.match(packageJson.scripts["publish:site"], /site:preview:build/u);
});

test("catalog detail launches Voltura Air directly and keeps file fallback", () => {
  const view = read("docs/site/screens/view.php");
  const library = read("docs/site/screens/lib.php");
  assert.match(view, /href="voltura-air:\/\/import\?id=/u);
  assert.doesNotMatch(view, /href="install\.php\?id=/u);
  assert.match(view, /href="download\.php\?id=/u);
  assert.match(view, /source=/u);
  assert.match(library, /VOLTURA_AIR_SITE_DEV/u);
  assert.match(library, /127\\\.0\\\.0\\\.1\|localhost/u);
});

test("rating action is a top-level summary that opens an implicit-save modal", () => {
  const view = read("docs/site/screens/view.php");
  const previewScript = read("docs/site/screens/preview.js");
  const summaryPosition = view.indexOf("$ratingSummary");
  const notesPosition = view.indexOf("<h2>Author notes</h2>");
  assert.ok(summaryPosition >= 0 && notesPosition > summaryPosition);
  assert.match(view, /<small>Your rating<\/small>/u);
  assert.match(view, /data-rating-dialog-open/u);
  assert.match(view, /<dialog class="catalog-rating-dialog">/u);
  assert.match(view, /onchange="this\.form\.submit\(\)"/u);
  assert.doesNotMatch(view, /Your selection is saved immediately/u);
  assert.match(view, /data-tooltip=/u);
  assert.match(previewScript, /showModal\(\)/u);
});

test("a signed-in user can remove only their own existing rating", () => {
  const endpoint = read("docs/site/screens/rate.php");
  const view = read("docs/site/screens/view.php");
  assert.match(endpoint, /\$remove = \(\$_POST\['action'\] \?\? ''\) === 'remove'/u);
  assert.match(endpoint, /DELETE FROM air_screen_ratings WHERE package_id = :package AND user_id = :user/u);
  assert.match(endpoint, /ratingRemoved=1/u);
  assert.match(view, /if \(\$userRating !== null\)/u);
  assert.match(view, />Remove rating<\/button>/u);
});

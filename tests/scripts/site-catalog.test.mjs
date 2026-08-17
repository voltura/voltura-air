import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import test from "node:test";
import { fileURLToPath } from "node:url";
import { officialScreens } from "../../scripts/custom-screens/catalog.mjs";
import { stableJson } from "../../scripts/custom-screens/builders/validation.mjs";

const read = (path) => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");

test("catalog ratings require an authenticated user and CSRF token", () => {
  const endpoint = read("apps/public-site/screens/rate.php");
  assert.match(endpoint, /air_screen_require_user\(\)/u);
  assert.match(endpoint, /air_screen_require_csrf\(\)/u);
  assert.match(endpoint, /rating < 1 \|\| \$rating > 5/u);
  assert.match(endpoint, /status = 'approved'/u);
  assert.match(endpoint, /ON DUPLICATE KEY UPDATE rating = VALUES\(rating\)/u);
});

test("fresh schema enforces one bounded vote per account and screen", () => {
  const schema = read("apps/public-site/screens/schema.sql");
  assert.match(schema, /PRIMARY KEY \(package_id, user_id\)/u);
  assert.match(schema, /CHECK \(rating BETWEEN 1 AND 5\)/u);
  assert.match(schema, /REFERENCES air_screen_packages\(id\) ON DELETE CASCADE/u);
  assert.match(schema, /REFERENCES air_screen_users\(id\) ON DELETE CASCADE/u);
});

test("catalog access rules deny credentials, SQL, and screen packages", () => {
  const accessRules = read("apps/public-site/.htaccess");
  assert.match(accessRules, /config\\\.php/u);
  assert.match(accessRules, /\\\.sql\$/u);
  assert.match(accessRules, /\\\.volturascreen\$/u);
});

test("catalog downloads stay inside configured package storage and count completed streams", () => {
  const endpoint = read("apps/public-site/screens/download.php");
  assert.match(endpoint, /air_screen_package_path\(\(string\)\$item\['storage_basename'\]\)/u);
  assert.match(endpoint, /is_file\(\$packagePath\)/u);
  assert.match(endpoint, /fpassthru\(\$package\)/u);
  assert.ok(endpoint.indexOf("fpassthru") < endpoint.indexOf("downloads = downloads + 1"));
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
  assert.match(initializer, /PHP\.PHP\.8\.5/u);
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
  const view = read("apps/public-site/screens/view.php");
  const styles = read("apps/public-site/styles.css");
  assert.match(view, /for \(\$rating = 5; \$rating >= 1; \$rating--\)/u);
  assert.match(view, /onchange="this\.form\.submit\(\)"/u);
  assert.match(styles, /\.catalog-page \.star-picker label:hover ~ label/u);
});

test("uploads always use the exact current screen name field", () => {
  const upload = read("apps/public-site/screens/upload.php");
  assert.match(upload, /\$name = trim\(\(string\)\$screen\['name'\]\)/u);
  assert.doesNotMatch(upload, /name="name"/u);
  assert.match(upload, /The screen name comes from the package\./u);
});

test("upload commit ambiguity is owned by a durable content cleanup job", () => {
  const upload = read("apps/public-site/screens/upload.php");
  const validation = read("apps/public-site/screens/lib.php");
  assert.match(upload, /air_screen_enqueue_cleanup\(\$database, \$basename, \$sha256\)/u);
  assert.ok(upload.indexOf("air_screen_enqueue_cleanup") < upload.indexOf("fopen($path, 'x+b')"));
  assert.match(upload, /DELETE FROM air_screen_cleanup_jobs/u);
  assert.doesNotMatch(upload, /unlink\(/u);
  assert.match(validation, /function air_screen_drain_cleanup_jobs/u);
  assert.match(validation, /hash_mismatch/u);
  assert.match(validation, /referenced/u);
  assert.match(validation, /count\(\$screen\['assignedClientIds'\]\) !== 0/u);
  assert.doesNotMatch(validation, /AssignedClientIds/u);
  assert.match(validation, /'packageVersion' => 1/u);
});

test("package uploads expose a visually distinct drop target and selected state", () => {
  const upload = read("apps/public-site/screens/upload.php");
  const styles = read("apps/public-site/styles.css");
  assert.match(upload, /catalog-package-drop-icon/u);
  assert.match(upload, /catalog-package-drop-action/u);
  assert.match(upload, /classList\.toggle\("has-package", hasPackage\)/u);
  assert.match(styles, /label\.catalog-package-drop[\s\S]*border: 2px dashed/u);
  assert.match(styles, /\.catalog-package-drop\.is-dragging/u);
  assert.match(styles, /\.catalog-package-drop\.has-package/u);
});

test("catalog navigation preserves the main site links and detail pages link back to browse", () => {
  const library = read("apps/public-site/screens/lib.php");
  const logout = read("apps/public-site/screens/logout.php");
  const index = read("apps/public-site/screens/index.php");
  assert.match(library, /href="\.\.\/#features">Features<\/a>/u);
  assert.match(library, /href="\.\/" aria-current="page">Custom screens<\/a>/u);
  assert.match(library, /href="\.\.\/#download">Download<\/a>/u);
  assert.match(library, />Upload screen<\/a>/u);
  assert.match(library, /href="upload\.php#submissions"[^>]*>My submissions<\/a>/u);
  assert.match(library, />Moderate screens<\/a>/u);
  assert.match(library, /<form class="catalog-signout" method="post" action="logout\.php">/u);
  assert.match(library, /name="csrf" value="' \. air_screen_h\(air_screen_csrf\(\)\)/u);
  assert.match(library, />Sign out<\/button><\/form>/u);
  assert.match(logout, /\$_SERVER\['REQUEST_METHOD'\] !== 'POST'/u);
  assert.match(logout, /air_screen_require_csrf\(\)/u);
  assert.match(library, /href="\.\.\/sitemap\.php">Sitemap<\/a>/u);
  assert.match(
    library,
    /href="\.\/" aria-label="Browse community library of custom screens"><span aria-hidden="true">&larr;<\/span> Community library<\/a>/u,
  );
  assert.match(library, /<p class="eyebrow">Community library<\/p>/u);
  assert.match(index, /air_screen_layout\('Custom screens', \$body, false\)/u);
});

test("catalog sort options use the site color treatment when opened", () => {
  const index = read("apps/public-site/screens/index.php");
  const styles = read("apps/public-site/styles.css");
  const script = read("apps/public-site/screens/preview.js");
  assert.match(index, /data-catalog-sort/u);
  assert.match(index, /role="listbox"/u);
  assert.match(styles, /\.catalog-page \.catalog-sort-option:hover,[\s\S]*background: var\(--accent-strong\);/u);
  assert.match(script, /sort\.classList\.add\("is-enhanced"\)/u);
  assert.match(script, /select\.value = option\.dataset\.sortValue/u);
});

test("catalog search reveals a tag-style clear control only for entered text", () => {
  const index = read("apps/public-site/screens/index.php");
  const script = read("apps/public-site/screens/preview.js");
  const styles = read("apps/public-site/styles.css");
  assert.match(index, /data-catalog-query/u);
  assert.match(index, /class="catalog-tag-remove catalog-query-clear"/u);
  assert.match(index, /aria-label="Clear search" hidden/u);
  assert.match(script, /clear\.hidden = input\.value\.length === 0/u);
  assert.match(script, /input\.value = ""/u);
  assert.match(styles, /\.catalog-page \.catalog-query \.catalog-query-clear[\s\S]*transform: translateY\(-50%\)/u);
  assert.match(styles, /\.catalog-page \.catalog-query-clear\[hidden\][\s\S]*display: none;/u);
});

test("administrators can permanently delete a listed screen after confirmation", () => {
  const index = read("apps/public-site/screens/index.php");
  const view = read("apps/public-site/screens/view.php");
  const endpoint = read("apps/public-site/screens/delete.php");
  const script = read("apps/public-site/screens/preview.js");
  const styles = read("apps/public-site/styles.css");
  assert.match(index, /\$isAdmin = \(\$user\['role'\] \?\? ''\) === 'admin'/u);
  assert.match(index, /data-delete-dialog-open/u);
  assert.match(index, /class="catalog-delete-dialog"/u);
  assert.match(view, /\$isAdmin = \(\$user\['role'\] \?\? ''\) === 'admin'/u);
  assert.match(view, /<a class="button secondary" href="download\.php\?id=/u);
  assert.match(view, /catalog-delete-button catalog-delete-open/u);
  assert.match(view, /class="catalog-delete-dialog"/u);
  assert.match(index, /This permanently removes the screen, ratings, reports, and downloadable package\./u);
  assert.match(endpoint, /air_screen_require_admin\(\)/u);
  assert.match(endpoint, /air_screen_require_csrf\(\)/u);
  assert.match(endpoint, /status = 'approved'/u);
  assert.match(endpoint, /beginTransaction\(\)/u);
  assert.match(endpoint, /DELETE FROM air_screen_reports/u);
  assert.match(endpoint, /DELETE FROM air_screen_ratings/u);
  assert.match(endpoint, /DELETE FROM air_screen_packages/u);
  assert.match(endpoint, /air_screen_enqueue_cleanup\(\$database, \$basename, \$sha256\)/u);
  assert.match(endpoint, /air_screen_drain_cleanup_jobs\(\)/u);
  assert.doesNotMatch(endpoint, /rename\(|unlink\(/u);
  assert.match(endpoint, /air_screen_acquire_advisory_lock\(\$database, 'delete'/u);
  assert.match(endpoint, /air_screen_release_advisory_lock\(\$database, \$lockName\)/u);
  assert.match(script, /data-delete-dialog-open/u);
  assert.match(script, /data-delete-dialog-close/u);
  assert.match(styles, /button\.catalog-delete-button/u);
  assert.match(styles, /\.catalog-delete-dialog::backdrop/u);
});

test("submission history uses linked rows, status pills, and an empty state", () => {
  const upload = read("apps/public-site/screens/upload.php");
  const endpoint = read("apps/public-site/screens/remove-rejected.php");
  const script = read("apps/public-site/screens/preview.js");
  const styles = read("apps/public-site/styles.css");
  assert.match(upload, /class="catalog-submissions"/u);
  assert.match(upload, /id="submissions"/u);
  assert.match(upload, /class="catalog-submission-list"/u);
  assert.match(upload, /class="catalog-submission-status ' \. \$statusClass/u);
  assert.match(upload, /No submissions yet\./u);
  assert.match(upload, /status <> 'removed'/u);
  assert.match(upload, /Remove rejected \(' \. \$rejectedCount/u);
  assert.match(upload, /class="catalog-remove-rejected-dialog"/u);
  assert.match(upload, /Their stored records will not be permanently deleted\./u);
  assert.match(upload, /rejectedRemoved/u);
  assert.match(endpoint, /air_screen_require_user\(\)/u);
  assert.match(endpoint, /air_screen_require_csrf\(\)/u);
  assert.match(endpoint, /owner_id = :owner AND status = 'rejected'/u);
  assert.match(endpoint, /status = 'removed'/u);
  assert.match(endpoint, /rejectedRemoved=' \. \$statement->rowCount\(\)/u);
  assert.match(script, /data-remove-rejected-dialog-open/u);
  assert.match(script, /data-remove-rejected-dialog-close/u);
  assert.match(styles, /\.catalog-submissions h2[\s\S]*font-size: 1\.3rem/u);
  assert.match(styles, /button\.catalog-remove-rejected-open/u);
  assert.match(styles, /\.catalog-remove-rejected-dialog::backdrop/u);
  assert.match(styles, /\.catalog-remove-rejected-icon::before/u);
  assert.match(styles, /\.catalog-remove-rejected-icon::after/u);
  assert.match(styles, /\.catalog-remove-rejected-icon::before[\s\S]*translate\(-50%, -50%\) rotate\(45deg\)/u);
  assert.match(styles, /\.catalog-submission-status\.is-pending/u);
  assert.match(styles, /\.catalog-submission-status\.is-approved/u);
  assert.match(styles, /\.catalog-submission-status\.is-rejected/u);
});

test("screen submission tags use an accessible removable pill editor", () => {
  const upload = read("apps/public-site/screens/upload.php");
  const edit = read("apps/public-site/screens/edit.php");
  const layout = read("apps/public-site/screens/lib.php");
  const script = read("apps/public-site/screens/tag-editor.js");
  const styles = read("apps/public-site/styles.css");
  assert.match(upload, /data-tag-editor/u);
  assert.match(edit, /data-tag-editor/u);
  assert.match(upload, /<label for="catalog-upload-tags">Tags<\/label>/u);
  assert.match(edit, /<label for="catalog-edit-tags">Tags<\/label>/u);
  assert.doesNotMatch(upload, /<label>Tags<span class="catalog-tag-editor"/u);
  assert.doesNotMatch(edit, /<label>Tags<span class="catalog-tag-editor"/u);
  assert.match(upload, /name="tags"[^>]*data-tags-value/u);
  assert.match(edit, /name="tags"[^>]*data-tags-value/u);
  assert.match(layout, /src="tag-editor\.js" defer/u);
  assert.match(script, /event.key === ' '/u);
  assert.match(script, /event.key === ','/u);
  assert.match(script, /addEventListener\('blur', commit\)/u);
  assert.match(script, /Remove tag \$\{tag\}/u);
  assert.doesNotMatch(script, /remove\.textContent/u);
  assert.doesNotMatch(script, /remove\.title/u);
  assert.match(script, /event\.target === editor \|\| event\.target === pills/u);
  assert.match(script, /input\.focus\(\)/u);
  assert.match(script, /toLocaleLowerCase\(\) === tag\.toLocaleLowerCase\(\)/u);
  assert.match(styles, /\.catalog-tag-pill/u);
  assert.match(styles, /\.catalog-tag-remove:focus-visible/u);
  assert.match(styles, /\.catalog-tag-remove:focus:not\(:focus-visible\)/u);
  assert.match(styles, /\.catalog-tag-remove[\s\S]*height: 1\.375rem/u);
  assert.match(styles, /\.catalog-tag-remove::before/u);
  assert.match(styles, /\.catalog-tag-remove::after/u);
  assert.match(styles, /top: 48%/u);
  assert.match(styles, /width: 0\.625rem/u);
  assert.match(styles, /translate\(-50%, -50%\) rotate\(45deg\)/u);
});
test("published screen details render tags as safe static pills", () => {
  const view = read("apps/public-site/screens/view.php");
  const library = read("apps/public-site/screens/lib.php");
  const styles = read("apps/public-site/styles.css");
  const staticTagRenderer = library.slice(
    library.indexOf("function air_screen_tag_pills"),
    library.indexOf("function air_screen_local_catalog_source"),
  );
  assert.match(view, /air_screen_tag_pills\(\(string\)\$item\['tags'\]\)/u);
  assert.match(library, /function air_screen_tag_pills\(string \$tags\)/u);
  assert.match(library, /catalog-tag-pill is-static/u);
  assert.match(library, /None supplied\./u);
  assert.match(styles, /\.catalog-tag-list/u);
  assert.match(styles, /\.catalog-tag-pill\.is-static/u);
  assert.match(styles, /\.catalog-tag-pill\.is-static[\s\S]*background: var\(--surface-raised\)/u);
  assert.match(styles, /\.catalog-tag-pill\.is-static[\s\S]*color: var\(--text\)/u);
  assert.doesNotMatch(staticTagRenderer, /catalog-tag-remove|Remove tag/u);
});

test("catalog cards render their tags with the same safe static pills", () => {
  const index = read("apps/public-site/screens/index.php");
  const styles = read("apps/public-site/styles.css");
  assert.match(index, /class="catalog-card-tags"/u);
  assert.match(index, /air_screen_tag_pills\(\(string\)\$item\['tags'\]\)/u);
  assert.match(styles, /\.catalog-card-tags \{[\s\S]*display: flex;/u);
  assert.match(styles, /\.catalog-card-tags \.catalog-tag-list \{[\s\S]*min-width: 0;/u);
});

test("rejection requires feedback that the author can read and clear by resubmitting", () => {
  const moderation = read("apps/public-site/screens/admin.php");
  const upload = read("apps/public-site/screens/upload.php");
  const edit = read("apps/public-site/screens/edit.php");
  assert.match(moderation, /\$status === 'rejected' && \$feedback === ''/u);
  assert.match(moderation, /name="rejection_feedback" maxlength="1000"/u);
  assert.match(moderation, /in_array\(\$status, \['approved', 'rejected'\], true\) && \$feedback !== '' \? \$feedback : null/u);
  assert.match(upload, /Reviewer feedback:/u);
  assert.match(edit, /Reviewer feedback/u);
  assert.match(edit, /status IN \('approved', 'rejected'\) THEN 'pending'/u);
  assert.match(edit, /rejection_feedback = NULL/u);
});

test("successful uploads use a temporary accessible toast", () => {
  const upload = read("apps/public-site/screens/upload.php");
  const library = read("apps/public-site/screens/lib.php");
  const previewScript = read("apps/public-site/screens/preview.js");
  const styles = read("apps/public-site/styles.css");
  assert.match(upload, /air_screen_toast\('Screen submitted for moderation'\)/u);
  assert.match(library, /class="catalog-toast" role="status"/u);
  assert.match(library, /catalog-toast-badge/u);
  assert.match(previewScript, /history\.replaceState/u);
  assert.match(previewScript, /2400/u);
  assert.match(styles, /bottom: max\(18px/u);
  assert.match(styles, /\.catalog-toast\.dismissed/u);
});

test("production uploads notify catalog administrators after persistence", () => {
  const upload = read("apps/public-site/screens/upload.php");
  const library = read("apps/public-site/screens/lib.php");
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

test("catalog sessions and daily abuse limits are enforced by current database owners", () => {
  const library = read("apps/public-site/screens/lib.php");
  const upload = read("apps/public-site/screens/upload.php");
  const report = read("apps/public-site/screens/report.php");
  const login = read("apps/public-site/screens/login.php");
  const register = read("apps/public-site/screens/register.php");
  assert.match(library, /session\.use_only_cookies/u);
  assert.match(library, /session\.use_strict_mode/u);
  assert.match(library, /SELECT GET_LOCK\(:name, :timeout\)/u);
  assert.match(library, /SELECT RELEASE_LOCK\(:name\)/u);
  assert.match(upload, /air_screen_acquire_advisory_lock\(\$database, 'upload'/u);
  assert.match(upload, /finally/u);
  assert.match(report, /strtolower\(trim/u);
  assert.match(report, /air_screen_acquire_advisory_lock\(\$database, 'report'/u);
  assert.match(report, /finally/u);
  assert.match(login, /session_regenerate_id\(true\)/u);
  assert.match(login, /air_screen_rate_consume\('login_email'/u);
  assert.match(login, /verified_at/u);
  assert.match(register, /air_screen_rate_consume\('register_email'/u);
  assert.match(register, /air_screen_verification_tokens/u);
  assert.match(register, /If that address can be registered/u);
});

test("screen reports are emailed to Voltura Air after persistence", () => {
  const endpoint = read("apps/public-site/screens/report.php");
  const library = read("apps/public-site/screens/lib.php");
  const view = read("apps/public-site/screens/view.php");
  const previewScript = read("apps/public-site/screens/preview.js");
  assert.match(endpoint, /INSERT INTO air_screen_reports/u);
  assert.match(endpoint, /air_screen_notify_screen_report/u);
  assert.ok(
    endpoint.indexOf("$stmt->execute") <
      endpoint.indexOf("air_screen_notify_screen_report"),
  );
  assert.match(library, /function air_screen_notify_screen_report/u);
  assert.match(library, /@mail\('air@voltura\.se'/u);
  assert.match(library, /Reporter email/u);
  assert.match(library, /Reason for review/u);
  assert.match(library, /VOLTURA_AIR_SITE_DEV/u);
  assert.match(endpoint, /&reported=1/u);
  assert.match(view, /air_screen_toast\('Screen has been reported'\)/u);
  assert.match(previewScript, /url\.searchParams\.delete\("reported"\)/u);
});

test("moderation emails approval or rejection status to the submitter", () => {
  const moderation = read("apps/public-site/screens/admin.php");
  const library = read("apps/public-site/screens/lib.php");
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

test("notification emails share an Outlook-compatible presentation shell", () => {
  const library = read("apps/public-site/screens/lib.php");
  const shell = library.slice(
    library.indexOf("function air_screen_notification_email"),
    library.indexOf("function air_screen_notify_moderators"),
  );
  assert.match(shell, /role="presentation"/u);
  assert.match(shell, /width="600"/u);
  assert.match(shell, /cellpadding="0" cellspacing="0" border="0"/u);
  assert.match(shell, /bgcolor=/u);
  assert.match(shell, /<table[^>]*><tr>[\s\S]*<td[^>]*><a href=/u);
  assert.match(shell, /<td bgcolor="#0d8f7d" style="padding:13px 22px/u);
  assert.match(shell, /<a href="[^"]+" style="display:block/u);
  assert.doesNotMatch(shell, /display:\s*none|opacity:\s*0|mso-hide/u);
  assert.equal((shell.match(/<a href=/gu) ?? []).length, 2);
  assert.match(shell, /AIR_SCREEN_ORIGIN \. '\/"/u);
  assert.match(shell, /This is an automated email from Voltura Air\. Replies are not monitored\./u);
  assert.match(library, /air_screen_notification_subject\('Review needed', \$screenName\)/u);
  assert.match(library, /air_screen_notification_subject\(\$approved \? 'Approved' : 'Rejected', \$screenName\)/u);
  assert.match(library, /preg_replace\('\/\[\\r\\n\]\+\/u', ' ', \$screenName\)/u);
  assert.match(library, /mb_encode_mimeheader/u);
  assert.match(shell, /border-top:6px solid #0d8f7d/u);
  assert.doesNotMatch(shell, /#d6a84b/u);
  assert.doesNotMatch(shell, /If the button does not work|copy and paste this address/u);
});

test("catalog previews expose full content through compact and interactive modes", () => {
  const library = read("apps/public-site/screens/lib.php");
  const index = read("apps/public-site/screens/index.php");
  const previewScript = read("apps/public-site/screens/preview.js");
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
  const frame = read("apps/public-site/screens/preview-frame.php");
  const library = read("apps/public-site/screens/lib.php");
  const previewScript = read("apps/public-site/screens/preview.js");
  const styles = read("apps/public-site/styles.css");
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
});

test("admins can atomically bulk-import the generated official screen bundle", () => {
  const admin = read("apps/public-site/screens/admin.php");
  const importer = read("apps/public-site/screens/official-import.php");
  const schema = read("apps/public-site/screens/schema.sql");
  const library = read("apps/public-site/screens/lib.php");
  assert.match(admin, /action="official-import\.php"/u);
  assert.match(importer, /air_screen_require_admin\(\)/u);
  assert.match(importer, /air_screen_require_csrf\(\)/u);
  assert.match(importer, /ZipArchive::RDONLY/u);
  assert.match(importer, /basename\(\$name\) !== \$name/u);
  assert.match(importer, /count\(\$entries\) !== count\(\$validated\) \+ 1/u);
  assert.match(importer, /air_screen_require_exact_keys\(\$catalog/u);
  assert.match(importer, /air_screen_require_exact_keys\(\$metadata/u);
  assert.doesNotMatch(importer, /researchReferences|unifiedremote/iu);
  assert.match(importer, /beginTransaction\(\)/u);
  assert.match(importer, /GET_LOCK\('voltura_air_official_import', 30\)/u);
  assert.match(importer, /RELEASE_LOCK\('voltura_air_official_import'\)/u);
  assert.match(importer, /ON DUPLICATE KEY UPDATE/u);
  assert.doesNotMatch(importer, /downloads\s*=/u, "updates preserve download counters");
  assert.match(importer, /\$db->commit\(\)/u);
  assert.match(importer, /\$db->rollBack\(\)/u);
  assert.match(importer, /air_screen_official_import_failure\('db_rollback'\)/u);
  assert.match(importer, /official_source = 'voltura'/u);
  assert.match(importer, /screen_id = :screenId/u);
  assert.match(importer, /air_screen_enqueue_cleanup/u);
  assert.match(importer, /DELETE FROM air_screen_packages WHERE id = :id AND official_source = 'voltura'/u);
  assert.match(importer, /air_screen_write_content_file\(\$item\['finalPath'\], \$item\['json'\]\)/u);
  assert.ok(importer.indexOf("air_screen_official_import_failure('stage_write')") < importer.indexOf("SELECT GET_LOCK('voltura_air_official_import', 30)"));
  assert.match(importer, /hash_equals\(\$item\['hash'\], \(string\)hash_file\('sha256', \$item\['finalPath'\]\)\)/u);
  assert.match(importer, /air_screen_official_import_failure\('db_committed'\)/u);
  assert.match(importer, /VOLTURA_AIR_OFFICIAL_IMPORT_FAIL/u);
  assert.match(schema, /official_source VARCHAR\(64\) NULL/u);
  assert.match(schema, /UNIQUE KEY uq_air_screen_official \(official_source, official_id\)/u);
  assert.match(schema, /screen_id VARCHAR\(64\) NOT NULL/u);
  assert.match(schema, /is_official BOOLEAN NOT NULL DEFAULT FALSE/u);
  assert.match(library, /'urlOpen', 'knownApp', 'hostAction'/u);
  assert.doesNotMatch(library.slice(library.indexOf("function air_screen_validate_package")), /'appLaunch'/u);
});

test("the PHP package boundary executes the current semantic contract", () => {
  const source = officialScreens[0].screen;
  const run = screen => {
    const library = fileURLToPath(new URL("../../apps/public-site/screens/lib.php", import.meta.url)).replaceAll("\\", "/").replaceAll("'", "\\'");
    return spawnSync("php", ["-d", "display_errors=1", "-r", `require '${library}'; try { air_screen_validate_package(file_get_contents('php://stdin')); echo 'accepted'; } catch (Throwable $error) { fwrite(STDERR, $error->getMessage()); exit(2); }`], {
      encoding: "utf8",
      input: stableJson({ packageVersion: 1, format: "voltura-air.custom-screen", screen })
    });
  };
  assert.equal(run(source).status, 0);
  const gyro = structuredClone(source);
  gyro.sections[0].trackpadGyroControl = true;
  assert.equal(run(gyro).status, 0);
  const sixRows = structuredClone(source);
  sixRows.sections[0].rowLimit = 6;
  sixRows.sections[0].buttons[0].row = 6;
  sixRows.sections[0].buttons[0].portrait = { order: 0, visible: true, row: 6 };
  sixRows.sections[0].buttons[0].landscape = { order: 0, visible: true, row: 6 };
  assert.equal(run(sixRows).status, 0);
  const invalidValues = [
    screen => { screen.sections[0].widthColumns = 5; },
    screen => { screen.sections[0].trackpadGyroControl = "true"; },
    screen => { screen.sections[0].rowLimit = 7; },
    screen => { screen.sections[0].rowLimit = 6; screen.sections[0].buttons[0].row = 7; },
    screen => { screen.sections[0].buttons[0].portrait = { order: 0, visible: true, row: 7 }; },
    screen => { screen.sections[0].buttons[0].action = { kind: "urlOpen", url: "file:///Windows/System32/calc.exe" }; },
    screen => { screen.sections[0].buttons[0].action = { kind: "shortcut", key: "UnsupportedKey", modifiers: [] }; screen.sections[0].buttons[0].presentation = "label"; },
    screen => { screen.sections[0].buttons[0].action = { kind: "builtIn", builtIn: "unknown.action" }; },
    screen => { screen.sections[0].buttons[0].action = { kind: "knownApp", actionId: "arbitrary.exe" }; }
  ];
  for (const mutate of invalidValues) {
    const invalid = structuredClone(source);
    mutate(invalid);
    assert.equal(run(invalid).status, 2);
  }
});

test("the public site contains no forbidden competitor-site reference", () => {
  assert.doesNotMatch(read("apps/public-site/index.php"), /unifiedremote|unified\s+remote/iu);
});

test("catalog detail launches Voltura Air directly and keeps file fallback", () => {
  const view = read("apps/public-site/screens/view.php");
  const library = read("apps/public-site/screens/lib.php");
  assert.match(view, /href="voltura-air:\/\/import\?id=/u);
  assert.doesNotMatch(view, /href="install\.php\?id=/u);
  assert.match(view, /href="download\.php\?id=/u);
  assert.match(view, /source=/u);
  assert.match(library, /VOLTURA_AIR_SITE_DEV/u);
  assert.match(library, /127\\\.0\\\.0\\\.1\|localhost/u);
});

test("rating action is a top-level summary that opens an implicit-save modal", () => {
  const view = read("apps/public-site/screens/view.php");
  const previewScript = read("apps/public-site/screens/preview.js");
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
  const endpoint = read("apps/public-site/screens/rate.php");
  const view = read("apps/public-site/screens/view.php");
  assert.match(endpoint, /\$remove = \(\$_POST\['action'\] \?\? ''\) === 'remove'/u);
  assert.match(endpoint, /DELETE FROM air_screen_ratings WHERE package_id = :package AND user_id = :user/u);
  assert.match(endpoint, /ratingRemoved=1/u);
  assert.match(view, /if \(\$userRating !== null\)/u);
  assert.match(view, />Remove rating<\/button>/u);
});

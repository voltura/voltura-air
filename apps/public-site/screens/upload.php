<?php
require_once __DIR__ . '/lib.php';
$user = air_screen_require_user();
$error = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $database = air_screen_db();
    $lockName = null;
    try {
        $lockName = air_screen_acquire_advisory_lock($database, 'upload', (string)$user['id']);
        $quota = $database->prepare('SELECT COUNT(*) FROM air_screen_packages WHERE owner_id = :owner AND created_at > DATE_SUB(CURRENT_TIMESTAMP, INTERVAL 1 DAY)');
        $quota->execute(['owner' => $user['id']]);
        if ((int)$quota->fetchColumn() >= 10) {
            throw new RuntimeException('The daily upload limit has been reached.');
        }
        if (!isset($_FILES['package']) || $_FILES['package']['error'] !== UPLOAD_ERR_OK || $_FILES['package']['size'] > AIR_SCREEN_MAX_BYTES) {
            throw new InvalidArgumentException('Choose a valid .volturascreen package under 8 MB.');
        }
        $json = file_get_contents($_FILES['package']['tmp_name']);
        $package = air_screen_validate_package($json);
        $screen = $package['screen'];
        $name = trim((string)$screen['name']);
        $description = trim((string)($_POST['description'] ?? ''));
        $tags = trim((string)($_POST['tags'] ?? ''));
        if ($name === '' || strlen($name) > 24 || strlen($description) > 1000 || strlen($tags) > 500) {
            throw new InvalidArgumentException('Metadata exceeds the allowed length.');
        }
        $id = air_screen_uuid();
        $storedJson = json_encode($package, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
        $sha256 = hash('sha256', $storedJson);
        $basename = $sha256 . '.volturascreen';
        $path = air_screen_package_path($basename);

        $database->beginTransaction();
        air_screen_enqueue_cleanup($database, $basename, $sha256);
        $database->commit();

        if (is_file($path)) {
            $existingHash = hash_file('sha256', $path);
            if (!is_string($existingHash) || !hash_equals($sha256, $existingHash)) {
                throw new RuntimeException('The package storage hash is already occupied.');
            }
        } else {
            $stream = fopen($path, 'x+b');
            if ($stream === false) throw new RuntimeException('The package could not be stored.');
            try {
                if (fwrite($stream, $storedJson) !== strlen($storedJson) || !fflush($stream)) {
                    throw new RuntimeException('The package could not be stored.');
                }
            } finally {
                fclose($stream);
            }
        }
        $database->beginTransaction();
        $stmt = $database->prepare('INSERT INTO air_screen_packages (id, owner_id, name, description, tags, package_version, screen_json, storage_basename, screen_id) VALUES (:id, :owner, :name, :description, :tags, 1, :json, :basename, :screenId)');
        $stmt->execute(['id' => $id, 'owner' => $user['id'], 'name' => $name, 'description' => $description, 'tags' => $tags, 'json' => $storedJson, 'basename' => $basename, 'screenId' => (string)$screen['id']]);
        $database->prepare('DELETE FROM air_screen_cleanup_jobs WHERE storage_basename = :basename AND expected_sha256 = :hash')
            ->execute(['basename' => $basename, 'hash' => $sha256]);
        $database->commit();
        air_screen_drain_cleanup_jobs();
        air_screen_release_advisory_lock($database, $lockName);
        $lockName = null;
        air_screen_notify_moderators(
            $id,
            $name,
            (string)$user['display_name'],
            $description,
            $tags);
        air_screen_redirect('upload.php?submitted=1');
    } catch (Throwable $exception) {
        if ($database->inTransaction()) {
            try { $database->rollBack(); }
            catch (Throwable $rollbackError) { error_log('Custom-screen upload rollback failed: ' . $rollbackError::class); }
        }
        if ($exception instanceof InvalidArgumentException ||
            ($exception instanceof RuntimeException && in_array($exception->getMessage(), [
                'The daily upload limit has been reached.',
                'The package could not be stored.'
            ], true))) {
            $error = $exception->getMessage();
        } else {
            error_log('Custom-screen upload failed: ' . $exception::class);
            $error = 'The screen could not be submitted. Try again later.';
        }
    } finally {
        if ($lockName !== null) {
            air_screen_release_advisory_lock($database, $lockName);
        }
    }
}
$message = isset($_GET['submitted'])
    ? air_screen_toast('Screen submitted for moderation')
    : (isset($_GET['rejectedRemoved'])
        ? air_screen_toast('Removed ' . max(0, (int)$_GET['rejectedRemoved']) . ' rejected submission' . ((int)$_GET['rejectedRemoved'] === 1 ? '' : 's'))
        : '');
$mine = air_screen_db()->prepare("SELECT id, name, status, rejection_feedback FROM air_screen_packages WHERE owner_id = :owner AND status <> 'removed' ORDER BY created_at DESC LIMIT 20");
$mine->execute(['owner' => $user['id']]);
$submissions = $mine->fetchAll();
$rejectedCount = count(array_filter($submissions, static fn(array $item): bool => strtolower((string)$item['status']) === 'rejected'));
$removeRejected = $rejectedCount > 0
    ? '<button class="catalog-remove-rejected-open" type="button" data-remove-rejected-dialog-open>Remove rejected (' . $rejectedCount . ')</button><dialog class="catalog-remove-rejected-dialog"><form method="post" action="remove-rejected.php"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><span class="catalog-remove-rejected-icon" aria-hidden="true"></span><h2>Remove ' . $rejectedCount . ' rejected submission' . ($rejectedCount === 1 ? '' : 's') . '?</h2><p>Their stored records will not be permanently deleted.</p><div class="catalog-remove-rejected-dialog-actions"><button class="catalog-remove-rejected-cancel" type="button" data-remove-rejected-dialog-close>Cancel</button><button class="catalog-remove-rejected-button" type="submit">Remove rejected</button></div></form></dialog>'
    : '';
$owned = '<section class="catalog-submissions" id="submissions" aria-labelledby="catalog-submissions-heading"><header><h2 id="catalog-submissions-heading">Your submissions</h2>' . $removeRejected . '</header>';
if (!$submissions) {
    $owned .= '<p class="catalog-submissions-empty">No submissions yet.</p>';
} else {
    $owned .= '<ul class="catalog-submission-list">';
}
foreach ($submissions as $item) {
    $status = strtolower((string)$item['status']);
    $statusClass = match ($status) {
        'pending' => 'is-pending',
        'approved' => 'is-approved',
        'rejected' => 'is-rejected',
        default => 'is-neutral'
    };
    $statusLabel = match ($status) {
        'pending' => 'Pending',
        'approved' => 'Approved',
        'rejected' => 'Rejected',
        default => 'Unknown'
    };
    $feedback = in_array($status, ['approved', 'rejected'], true) ? trim((string)$item['rejection_feedback']) : '';
    $owned .= '<li><div class="catalog-submission-details"><a href="edit.php?id=' . air_screen_h($item['id']) . '">' . air_screen_h($item['name']) . '</a>' . ($feedback !== '' ? '<p><strong>Reviewer feedback:</strong> ' . air_screen_h($feedback) . '</p>' : '') . '</div><span class="catalog-submission-status ' . $statusClass . '">' . $statusLabel . '</span></li>';
}
if ($submissions) {
    $owned .= '</ul>';
}
$owned .= '</section>';
$body = $message . ($error ? '<p>' . air_screen_h($error) . '</p>' : '') . '<p class="catalog-lede">Upload a screen package for review. The screen name comes from the package. It stays private until approved.</p><form method="post" enctype="multipart/form-data"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><label class="catalog-package-drop" data-package-drop><span class="catalog-package-drop-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 16V4m0 0-5 5m5-5 5 5M5 14v5h14v-5"/></svg></span><span class="catalog-package-drop-title">Upload screen package</span><span class="catalog-package-drop-instruction">Drop a <code>.volturascreen</code> package here</span><span class="catalog-package-drop-action">Choose a file</span><input class="sr-only" name="package" type="file" accept=".volturascreen,application/json" required data-package-input><span class="catalog-package-drop-name" data-package-name aria-live="polite">No package selected &middot; 8 MB maximum</span></label><label>Author notes<textarea name="description" maxlength="1000" placeholder="Describe its layout and use"></textarea></label><div class="catalog-tag-field"><label for="catalog-upload-tags">Tags</label><span class="catalog-tag-editor" data-tag-editor><span class="catalog-tag-pills" data-tag-pills></span><input id="catalog-upload-tags" data-tag-input maxlength="500" placeholder="media, presentation, productivity"><input type="hidden" name="tags" value="" data-tags-value></span></div><button>Submit for review</button></form><script>document.addEventListener("DOMContentLoaded", function () { const dropZone = document.querySelector("[data-package-drop]"); const input = document.querySelector("[data-package-input]"); const name = document.querySelector("[data-package-name]"); if (!dropZone || !input || !name) return; const update = function () { const hasPackage = Boolean(input.files && input.files.length); name.textContent = hasPackage ? input.files[0].name : "No package selected · 8 MB maximum"; dropZone.classList.toggle("has-package", hasPackage); }; ["dragenter", "dragover"].forEach(function (eventName) { dropZone.addEventListener(eventName, function (event) { event.preventDefault(); dropZone.classList.add("is-dragging"); }); }); ["dragleave", "drop"].forEach(function (eventName) { dropZone.addEventListener(eventName, function () { dropZone.classList.remove("is-dragging"); }); }); dropZone.addEventListener("drop", function (event) { event.preventDefault(); if (event.dataTransfer.files.length) { input.files = event.dataTransfer.files; input.dispatchEvent(new Event("change", { bubbles: true })); } }); input.addEventListener("change", update); });</script>' . $owned;
air_screen_layout('Upload a custom screen', $body);

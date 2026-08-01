<?php
require_once __DIR__ . '/lib.php';
$user = air_screen_require_user();
$error = '';
if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
    $uncommittedPackagePath = null;
    try {
        $quota = air_screen_db()->prepare('SELECT COUNT(*) FROM air_screen_packages WHERE owner_id = :owner AND created_at > DATE_SUB(CURRENT_TIMESTAMP, INTERVAL 1 DAY)');
        $quota->execute(['owner' => $user['id']]);
        if ((int)$quota->fetchColumn() >= 10) {
            throw new RuntimeException('The daily upload limit has been reached.');
        }
        if (!isset($_FILES['package']) || $_FILES['package']['error'] !== UPLOAD_ERR_OK || $_FILES['package']['size'] > AIR_SCREEN_MAX_BYTES) {
            throw new InvalidArgumentException('Choose a valid .volturascreen package under 8 MB.');
        }
        $json = file_get_contents($_FILES['package']['tmp_name']);
        $package = air_screen_validate_package($json);
        $screen = air_screen_value($package, 'Screen');
        $name = trim((string)air_screen_value($screen, 'Name'));
        $description = trim((string)($_POST['description'] ?? ''));
        $tags = trim((string)($_POST['tags'] ?? ''));
        if ($name === '' || strlen($name) > 24 || strlen($description) > 1000 || strlen($tags) > 500) {
            throw new InvalidArgumentException('Metadata exceeds the allowed length.');
        }
        $id = air_screen_uuid();
        $path = air_screen_storage_path() . DIRECTORY_SEPARATOR . $id . '.volturascreen';
        $storedJson = json_encode($package, JSON_THROW_ON_ERROR | JSON_UNESCAPED_SLASHES);
        $uncommittedPackagePath = $path;
        if (file_put_contents($path, $storedJson, LOCK_EX) === false) {
            throw new RuntimeException('The package could not be stored.');
        }
        $stmt = air_screen_db()->prepare('INSERT INTO air_screen_packages (id, owner_id, name, description, tags, package_version, screen_json, storage_path) VALUES (:id, :owner, :name, :description, :tags, 1, :json, :path)');
        $stmt->execute(['id' => $id, 'owner' => $user['id'], 'name' => $name, 'description' => $description, 'tags' => $tags, 'json' => $storedJson, 'path' => $path]);
        $uncommittedPackagePath = null;
        air_screen_notify_moderators(
            $id,
            $name,
            (string)$user['display_name'],
            $description,
            $tags);
        air_screen_redirect('upload.php?submitted=1');
    } catch (Throwable $exception) {
        if ($uncommittedPackagePath !== null && is_file($uncommittedPackagePath)) {
            @unlink($uncommittedPackagePath);
        }
        $error = $exception->getMessage();
    }
}
$message = isset($_GET['submitted'])
    ? air_screen_toast('Screen submitted for moderation')
    : '';
$mine = air_screen_db()->prepare('SELECT id, name, status, rejection_feedback FROM air_screen_packages WHERE owner_id = :owner ORDER BY created_at DESC LIMIT 20');
$mine->execute(['owner' => $user['id']]);
$submissions = $mine->fetchAll();
$owned = '<section class="catalog-submissions" id="submissions" aria-labelledby="catalog-submissions-heading"><header><h2 id="catalog-submissions-heading">Your submissions</h2></header>';
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
$body = $message . ($error ? '<p>' . air_screen_h($error) . '</p>' : '') . '<p class="catalog-lede">Upload a screen package and tell reviewers and users what it is for. The screen name comes from the package. Nothing becomes public until an administrator approves it.</p><form method="post" enctype="multipart/form-data"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><label class="catalog-package-drop" data-package-drop><span class="catalog-package-drop-icon" aria-hidden="true"><svg viewBox="0 0 24 24"><path d="M12 16V4m0 0-5 5m5-5 5 5M5 14v5h14v-5"/></svg></span><span class="catalog-package-drop-title">Upload screen package</span><span class="catalog-package-drop-instruction">Drag and drop a <code>.volturascreen</code> package here</span><span class="catalog-package-drop-action">Choose a file</span><input class="sr-only" name="package" type="file" accept=".volturascreen,application/json" required data-package-input><span class="catalog-package-drop-name" data-package-name aria-live="polite">No package selected &middot; 8 MB maximum</span></label><label>Author notes<textarea name="description" maxlength="1000" placeholder="Describe the layout, intended use, and anything users should know"></textarea></label><label>Tags<input name="tags" maxlength="500" placeholder="media, presentation, productivity"></label><button>Submit for review</button></form><script>document.addEventListener("DOMContentLoaded", function () { const dropZone = document.querySelector("[data-package-drop]"); const input = document.querySelector("[data-package-input]"); const name = document.querySelector("[data-package-name]"); if (!dropZone || !input || !name) return; const update = function () { const hasPackage = Boolean(input.files && input.files.length); name.textContent = hasPackage ? input.files[0].name : "No package selected · 8 MB maximum"; dropZone.classList.toggle("has-package", hasPackage); }; ["dragenter", "dragover"].forEach(function (eventName) { dropZone.addEventListener(eventName, function (event) { event.preventDefault(); dropZone.classList.add("is-dragging"); }); }); ["dragleave", "drop"].forEach(function (eventName) { dropZone.addEventListener(eventName, function () { dropZone.classList.remove("is-dragging"); }); }); dropZone.addEventListener("drop", function (event) { event.preventDefault(); if (event.dataTransfer.files.length) { input.files = event.dataTransfer.files; input.dispatchEvent(new Event("change", { bubbles: true })); } }); input.addEventListener("change", update); });</script>' . $owned;
air_screen_layout('Upload a custom screen', $body);

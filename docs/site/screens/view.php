<?php
require_once __DIR__ . '/lib.php';
$stmt = air_screen_db()->prepare("SELECT p.id, p.name, p.description, p.tags, p.package_version, p.downloads, COALESCE(p.approved_at, p.created_at) AS published_at, p.screen_json, u.display_name AS author, (SELECT COALESCE(AVG(r.rating), 0) FROM air_screen_ratings r WHERE r.package_id = p.id) AS average_rating, (SELECT COUNT(*) FROM air_screen_ratings r WHERE r.package_id = p.id) AS rating_count FROM air_screen_packages p JOIN air_screen_users u ON u.id = p.owner_id WHERE p.id = :id AND p.status = 'approved'");
$stmt->execute(['id' => (string)($_GET['id'] ?? '')]);
$item = $stmt->fetch();
if (!$item) { http_response_code(404); exit('Screen not found.'); }
$package = json_decode((string)$item['screen_json'], true);
$screen = is_array($package) ? air_screen_value($package, 'Screen') : null;
$actions = [];
foreach (is_array($screen) ? (air_screen_value($screen, 'Sections') ?? []) : [] as $section) {
    foreach (is_array($section) ? (air_screen_value($section, 'Buttons') ?? []) : [] as $button) {
        $action = is_array($button) ? air_screen_value($button, 'Action') : null;
        $kind = is_array($action) ? (string)air_screen_value($action, 'Kind') : '';
        if ($kind !== '') { $actions[$kind] = ($actions[$kind] ?? 0) + 1; }
    }
}
$id = air_screen_h($item['id']);
$actionSummary = [];
foreach ($actions as $kind => $count) { $actionSummary[] = $kind . ': ' . $count; }
$date = date('F j, Y', strtotime((string)$item['published_at']));
$user = air_screen_user();
$isAdmin = ($user['role'] ?? '') === 'admin';
$userRating = null;
if ($user) {
    $ratingStatement = air_screen_db()->prepare('SELECT rating FROM air_screen_ratings WHERE package_id = :package AND user_id = :user');
    $ratingStatement->execute(['package' => $item['id'], 'user' => $user['id']]);
    $ratingValue = $ratingStatement->fetchColumn();
    $userRating = $ratingValue === false ? null : (int)$ratingValue;
}
$yourRating = $user
    ? '<button type="button" class="catalog-your-rating' . ($userRating === null ? '' : ' has-rating') . '" data-rating-dialog-open><small>Your rating</small><span>' . ($userRating === null ? '&#9734; Rate' : '&#9733; ' . $userRating . '/5') . '</span></button>'
    : '<a class="catalog-your-rating" href="login.php"><small>Your rating</small><span>&#9734; Sign in</span></a>';
$ratingSummary = '<div class="catalog-rating-summary"><div><small>Community rating</small>' . air_screen_stars((float)$item['average_rating'], (int)$item['rating_count']) . '</div>' . $yourRating . '</div>';
$body = '<div class="catalog-detail-grid">' . air_screen_preview((string)$item['screen_json'], (string)$item['name'], false, (string)$item['id']) . '<div class="catalog-detail">' . $ratingSummary . '<h2>Author notes</h2><p class="catalog-lede">' . air_screen_h($item['description']) . '</p><dl><div><dt>Author</dt><dd>' . air_screen_h($item['author']) . '</dd></div><div><dt>Published</dt><dd>' . air_screen_h($date) . '</dd></div><div><dt>Tags</dt><dd>' . air_screen_tag_pills((string)$item['tags']) . '</dd></div><div><dt>Actions</dt><dd>' . air_screen_h(implode(', ', $actionSummary) ?: 'none') . '</dd></div><div><dt>Package</dt><dd>Version ' . (int)$item['package_version'] . ' &middot; ' . (int)$item['downloads'] . ' downloads</dd></div></dl></div></div>';
$localCatalogSource = air_screen_local_catalog_source();
$catalogSourceQuery = $localCatalogSource === null
    ? ''
    : '&amp;source=' . rawurlencode($localCatalogSource);
$adminDelete = $isAdmin
    ? '<button class="catalog-delete-button catalog-delete-open" type="button" data-delete-dialog-open>Delete</button><dialog class="catalog-delete-dialog"><form method="post" action="delete.php"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input type="hidden" name="id" value="' . $id . '"><span class="catalog-delete-icon" aria-hidden="true">!</span><h2>Delete ' . air_screen_h($item['name']) . '?</h2><p>This permanently removes the screen, ratings, reports, and downloadable package.</p><div class="catalog-delete-dialog-actions"><button class="catalog-delete-cancel" type="button" data-delete-dialog-close>Cancel</button><button class="catalog-delete-button" type="submit">Delete screen</button></div></form></dialog>'
    : '';
$body .= '<div class="actions"><a class="button primary" href="voltura-air://import?id=' . rawurlencode((string)$item['id']) . $catalogSourceQuery . '">Install in Voltura Air</a><a class="button secondary" href="download.php?id=' . $id . '">Download file</a>' . $adminDelete . '</div>';
$reportMessage = isset($_GET['reported']) ? air_screen_toast('Screen has been reported') : '';
if ($user) {
    $ratedMessage = isset($_GET['rated']) ? air_screen_toast('Rating saved') : (isset($_GET['ratingRemoved']) ? air_screen_toast('Rating removed') : '');
    $body .= $ratedMessage . '<dialog class="catalog-rating-dialog"><div class="catalog-rating-hero" data-current-rating="' . ($userRating ?? '') . '" aria-hidden="true"><span>&#9733;</span><strong>' . ($userRating ?? '?') . '</strong></div><form method="dialog" class="catalog-rating-close"><button aria-label="Close rating dialog">&times;</button></form><p class="eyebrow">Rate this</p><h2>' . air_screen_h($item['name']) . '</h2><form class="catalog-rating-form" method="post" action="rate.php"><input type="hidden" name="id" value="' . $id . '"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><fieldset><legend>Choose from 1 to 5 stars</legend><div class="star-picker">';
    for ($rating = 5; $rating >= 1; $rating--) {
        $body .= '<input id="rating-' . $rating . '" name="rating" type="radio" value="' . $rating . '" onchange="this.form.submit()"' . ($userRating === $rating ? ' checked' : '') . ' required><label for="rating-' . $rating . '" data-rating-value="' . $rating . '" data-tooltip="' . $rating . ($rating === 1 ? ' star' : ' stars') . '">&#9733;<span class="sr-only">Rate ' . $rating . ' out of 5</span></label>';
    }
    $body .= '</div></fieldset></form>';
    if ($userRating !== null) {
        $body .= '<form class="catalog-remove-rating" method="post" action="rate.php"><input type="hidden" name="id" value="' . $id . '"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><button name="action" value="remove">Remove rating</button></form>';
    }
    $body .= '</dialog>';
}
$body .= $reportMessage . '<details class="catalog-report"><summary>Report this screen</summary><form method="post" action="report.php"><input type="hidden" name="id" value="' . $id . '"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input name="email" type="email" required placeholder="Your email"><textarea name="reason" required maxlength="1000" placeholder="Why are you reporting it?"></textarea><button>Send report</button></form></details>';
air_screen_layout($item['name'], $body);

<?php
require_once __DIR__ . '/lib.php';

try {
    $user = air_screen_user();
    $isAdmin = ($user['role'] ?? '') === 'admin';
    $query = trim((string)($_GET['q'] ?? ''));
    $sort = (string)($_GET['sort'] ?? 'popular');
    $sortOrders = [
        'popular' => 'average_rating DESC, rating_count DESC, p.downloads DESC, p.created_at DESC',
        'newest' => 'published_at DESC',
        'author' => 'u.display_name ASC, p.name ASC',
    ];
    if (!isset($sortOrders[$sort])) { $sort = 'popular'; }
    $sql = "SELECT p.id, p.name, p.description, p.tags, p.downloads, COALESCE(p.approved_at, p.created_at) AS published_at, p.screen_json, u.display_name AS author, (SELECT COALESCE(AVG(r.rating), 0) FROM air_screen_ratings r WHERE r.package_id = p.id) AS average_rating, (SELECT COUNT(*) FROM air_screen_ratings r WHERE r.package_id = p.id) AS rating_count FROM air_screen_packages p JOIN air_screen_users u ON u.id = p.owner_id WHERE p.status = 'approved' AND (p.name LIKE :query OR p.description LIKE :query OR p.tags LIKE :query OR u.display_name LIKE :query) ORDER BY " . $sortOrders[$sort] . ' LIMIT 60';
    $stmt = air_screen_db()->prepare($sql);
    $stmt->execute(['query' => '%' . $query . '%']);
    $items = $stmt->fetchAll();
    $body = (isset($_GET['deleted']) ? air_screen_toast('Custom screen deleted') : '') . '<p class="catalog-lede">Discover reviewed control surfaces made by the Voltura Air community.</p><form class="catalog-search" method="get"><label class="sr-only" for="catalog-query">Search screens</label><input id="catalog-query" name="q" value="' . air_screen_h($query) . '" placeholder="Search by name, author, description, or tag" maxlength="100"><label class="sr-only" for="catalog-sort">Sort screens</label><select id="catalog-sort" name="sort"><option value="popular"' . ($sort === 'popular' ? ' selected' : '') . '>Most popular</option><option value="newest"' . ($sort === 'newest' ? ' selected' : '') . '>Newest</option><option value="author"' . ($sort === 'author' ? ' selected' : '') . '>Author A-Z</option></select><button type="submit">Search</button></form><section class="feature-band catalog-grid">';
    foreach ($items as $item) {
        $date = date('M j, Y', strtotime((string)$item['published_at']));
        $detailUrl = 'view.php?id=' . air_screen_h($item['id']);
        $adminDelete = $isAdmin
            ? '<button class="catalog-delete-button catalog-delete-open" type="button" data-delete-dialog-open>Delete</button><dialog class="catalog-delete-dialog"><form method="post" action="delete.php"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><input type="hidden" name="id" value="' . air_screen_h($item['id']) . '"><span class="catalog-delete-icon" aria-hidden="true">!</span><h2>Delete ' . air_screen_h($item['name']) . '?</h2><p>This permanently removes the screen, ratings, reports, and downloadable package.</p><div class="catalog-delete-dialog-actions"><button class="catalog-delete-cancel" type="button" data-delete-dialog-close>Cancel</button><button class="catalog-delete-button" type="submit">Delete screen</button></div></form></dialog>'
            : '';
        $body .= '<article>' . air_screen_preview((string)$item['screen_json'], (string)$item['name'], true) . '<div class="catalog-card-copy"><h2><a href="' . $detailUrl . '">' . air_screen_h($item['name']) . '</a></h2>' . air_screen_stars((float)$item['average_rating'], (int)$item['rating_count']) . '<p>' . air_screen_h($item['description']) . '</p><small>By ' . air_screen_h($item['author']) . ' &middot; ' . air_screen_h($date) . '<br>' . air_screen_h($item['tags']) . ' &middot; ' . (int)$item['downloads'] . ' downloads</small><div class="catalog-card-actions"><a class="button secondary catalog-preview-link" href="' . $detailUrl . '">Preview</a>' . $adminDelete . '</div></div></article>';
    }
    if (!$items) {
        $body .= '<p class="catalog-empty">No approved screens match your search yet.</p>';
    }
    $body .= '</section>';
    air_screen_layout('Custom screens', $body, false);
} catch (Throwable $error) {
    http_response_code(503);
    air_screen_layout('Catalog unavailable', '<p>' . air_screen_h($error->getMessage()) . '</p>', false);
}

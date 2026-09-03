<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}

require_once __DIR__ . '/lib.php';
$limit = isset($argv[1]) ? (int)$argv[1] : AIR_SCREEN_MAINTENANCE_LIMIT;
$counts = air_screen_maybe_maintain_catalog($limit, true);
foreach ($counts as $owner => $completed) {
    fwrite(STDOUT, "Completed {$completed} {$owner} cleanup item(s).\n");
}

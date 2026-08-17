<?php
declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}

require_once __DIR__ . '/lib.php';
$limit = isset($argv[1]) ? (int)$argv[1] : 100;
$completed = air_screen_drain_cleanup_jobs($limit);
fwrite(STDOUT, "Completed {$completed} catalog cleanup job(s).\n");

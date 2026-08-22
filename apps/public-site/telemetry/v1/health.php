<?php
declare(strict_types=1);

require_once __DIR__ . '/lib.php';

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'GET') {
    http_response_code(405);
    header('Allow: GET');
    header('Cache-Control: no-store');
    exit;
}

try {
    $database = air_telemetry_db();
    air_telemetry_secret();
    $checks = [
        'SELECT activity_date, installation_hash, host_version, host_starts, connections_standard_local, '
            . 'connections_enhanced_direct, connections_relay, features_trackpad, features_keyboard, '
            . 'features_dictation, features_media_controls, features_presentation, features_custom_screens, '
            . 'features_files, features_screen_viewing, features_phone_webcam, features_gyro_mouse, '
            . 'first_received_at, last_received_at FROM ' . air_telemetry_table('daily') . ' LIMIT 0',
        'SELECT installation_hash, batch_id, received_at FROM ' . air_telemetry_table('batches') . ' LIMIT 0',
        'SELECT bucket_kind, bucket_hash, window_start, request_count FROM ' . air_telemetry_table('rates') . ' LIMIT 0',
        'SELECT activity_date, accepted, duplicate, invalid, rate_limited, server_failed, '
            . 'last_successful_ingest_at FROM ' . air_telemetry_table('ingest') . ' LIMIT 0',
    ];
    foreach ($checks as $sql) {
        $database->query($sql);
    }
    $maintenance = $database->query(
        'SELECT singleton_id, next_cleanup_at FROM ' . air_telemetry_table('maintenance') .
        ' WHERE singleton_id = 1')->fetch();
    if (!is_array($maintenance) || (int)$maintenance['singleton_id'] !== 1) {
        throw new RuntimeException('Telemetry maintenance state is unavailable.');
    }
    http_response_code(204);
    header('Cache-Control: no-store');
} catch (Throwable) {
    http_response_code(503);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-store');
    echo '{"schemaVersion":1,"status":"unavailable"}';
}

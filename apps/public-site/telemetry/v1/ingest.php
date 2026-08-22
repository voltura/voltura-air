<?php
declare(strict_types=1);

require_once __DIR__ . '/lib.php';

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') {
    air_telemetry_error_response(405);
}
$contentType = strtolower(trim((string)($_SERVER['CONTENT_TYPE'] ?? '')));
if (preg_match('/^application\/json(?:\s*;\s*charset=utf-8)?$/D', $contentType) !== 1) {
    air_telemetry_error_response(415);
}
$body = air_telemetry_read_body();
try {
    $decoded = json_decode($body, true, 8, JSON_THROW_ON_ERROR);
} catch (JsonException) {
    $invalidResult = air_telemetry_record_invalid((string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'));
    if ($invalidResult === null) {
        error_log('Voltura Air telemetry ingest failed.');
        air_telemetry_error_response(503);
    }
    if (!$invalidResult) {
        air_telemetry_error_response(429);
    }
    air_telemetry_error_response(400);
}
$batch = air_telemetry_validate_batch($decoded);
if ($batch === null) {
    $invalidResult = air_telemetry_record_invalid((string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'));
    if ($invalidResult === null) {
        error_log('Voltura Air telemetry ingest failed.');
        air_telemetry_error_response(503);
    }
    if (!$invalidResult) {
        air_telemetry_error_response(429);
    }
    air_telemetry_error_response(400);
}

try {
    $database = air_telemetry_db();
    $result = air_telemetry_ingest($database, $batch, (string)($_SERVER['REMOTE_ADDR'] ?? 'unknown'));
    try {
        air_telemetry_maybe_cleanup($database);
    } catch (Throwable) {
        error_log('Voltura Air telemetry maintenance failed.');
    }
    if ($result === 'rate-limited') {
        air_telemetry_error_response(429);
    }
    air_telemetry_fixed_response(202, '{"schemaVersion":1,"status":"accepted"}');
} catch (Throwable) {
    try {
        air_telemetry_record_server_failure();
    } catch (Throwable) {
        // Failure accounting cannot replace the endpoint's fixed 503 response.
    }
    error_log('Voltura Air telemetry ingest failed.');
    air_telemetry_error_response(503);
}

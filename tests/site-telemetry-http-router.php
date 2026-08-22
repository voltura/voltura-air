<?php
declare(strict_types=1);

$source = getenv('VOLTURA_AIR_TELEMETRY_TEST_SOURCE');
if (is_string($source) && $source !== '') {
    $_SERVER['REMOTE_ADDR'] = $source;
}

$documentRoot = realpath(dirname(__DIR__) . '/apps/public-site');
$requestPath = parse_url((string)($_SERVER['REQUEST_URI'] ?? ''), PHP_URL_PATH);
if (!is_string($documentRoot) || !is_string($requestPath)) {
    http_response_code(404);
    return true;
}
$target = realpath($documentRoot . '/' . ltrim($requestPath, '/'));
if (!is_string($target) || !str_starts_with($target, $documentRoot . DIRECTORY_SEPARATOR) || !is_file($target)) {
    http_response_code(404);
    return true;
}
require $target;
return true;

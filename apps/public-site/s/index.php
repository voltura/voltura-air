<?php
declare(strict_types=1);

header('Cache-Control: no-store');
header('Referrer-Policy: no-referrer');
header("Content-Security-Policy: default-src 'none'; frame-ancestors 'none'; base-uri 'none'");

$route = isset($_GET['r']) && is_string($_GET['r']) ? $_GET['r'] : '';
$version = isset($_GET['v']) && is_string($_GET['v']) ? $_GET['v'] : '';
if (!preg_match('/\A[A-Za-z0-9_-]{22}\z/D', $route) ||
    !preg_match('/\A\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?\z/D', $version) ||
    count($_GET) !== 2) {
    http_response_code(404);
    exit;
}

$location = '/air/app/?m=s&r=' . rawurlencode($route) . '&v=' . rawurlencode($version);
header('Location: ' . $location, true, 302);
exit;

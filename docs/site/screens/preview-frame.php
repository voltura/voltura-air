<?php
declare(strict_types=1);
require_once __DIR__ . '/lib.php';

$statement = air_screen_db()->prepare('SELECT screen_json, status FROM air_screen_packages WHERE id = :id');
$statement->execute(['id' => (string)($_GET['id'] ?? '')]);
$item = $statement->fetch();
$user = air_screen_user();
if (!$item || ($item['status'] !== 'approved' && ($user['role'] ?? '') !== 'admin')) {
    http_response_code(404);
    exit('Screen not found.');
}

$package = json_decode((string)$item['screen_json'], true, 32, JSON_THROW_ON_ERROR);
$json = json_encode($package, JSON_THROW_ON_ERROR | JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT | JSON_UNESCAPED_SLASHES);
header("Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'");
header('X-Content-Type-Options: nosniff');
?>
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <title>Custom screen preview</title>
  <link rel="stylesheet" href="assets/catalog-preview.css">
</head>
<body>
  <div id="root"></div>
  <script id="catalog-screen-package" type="application/json"><?= $json ?></script>
  <script src="assets/catalog-preview.js"></script>
</body>
</html>

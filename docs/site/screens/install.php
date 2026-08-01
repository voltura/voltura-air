<?php
require_once __DIR__ . '/lib.php';
$stmt = air_screen_db()->prepare("SELECT id, name FROM air_screen_packages WHERE id = :id AND status = 'approved'");
$stmt->execute(['id' => (string)($_GET['id'] ?? '')]);
$item = $stmt->fetch();
if (!$item) { http_response_code(404); exit('Screen not found.'); }
$id = rawurlencode($item['id']);
$body = '<p>Voltura Air will review this screen before importing it. If the app is not registered for the link, download the file instead.</p><p><a class="button primary" href="voltura-air://import?id=' . $id . '">Open in Voltura Air</a> <a class="button secondary" href="download.php?id=' . air_screen_h($item['id']) . '">Download file</a></p>';
air_screen_layout('Install ' . $item['name'], $body);

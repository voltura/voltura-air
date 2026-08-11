<?php
require_once __DIR__ . '/lib.php';
if ($_SERVER['REQUEST_METHOD'] !== 'POST') { http_response_code(405); exit('POST required.'); }
air_screen_require_csrf();
$_SESSION = [];
session_destroy();
air_screen_redirect('./');

<?php
require_once __DIR__ . '/lib.php';
$_SESSION = [];
session_destroy();
air_screen_redirect('./');

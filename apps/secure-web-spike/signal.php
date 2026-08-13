<?php
declare(strict_types=1);

const MAX_REQUEST_BYTES = 64 * 1024;
const MAX_CIPHERTEXT_CHARS = 56 * 1024;
const ROOM_TTL_SECONDS = 300;
const ROOM_PATTERN = '/\A[A-Za-z0-9_-]{43}\z/D';
const BASE64URL_PATTERN = '/\A[A-Za-z0-9_-]+\z/D';

header('Content-Type: application/json; charset=utf-8');
header('Cache-Control: no-store, max-age=0');
header('X-Content-Type-Options: nosniff');
header('Referrer-Policy: no-referrer');

function respond(int $status, array $body): never
{
    http_response_code($status);
    echo json_encode($body, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    exit;
}

function fail_request(int $status, string $message): never
{
    respond($status, ['ok' => false, 'error' => $message]);
}

function state_directory(): string
{
    $directory = rtrim(sys_get_temp_dir(), DIRECTORY_SEPARATOR) . DIRECTORY_SEPARATOR . 'voltura-air-webrtc-spike';
    if (!is_dir($directory) && !mkdir($directory, 0700, true) && !is_dir($directory)) {
        fail_request(500, 'Temporary signaling storage is unavailable.');
    }
    return $directory;
}

function room_path(string $room): string
{
    return state_directory() . DIRECTORY_SEPARATOR . hash('sha256', $room) . '.json';
}

function expire_stale_rooms(string $directory): void
{
    $now = time();
    foreach (glob($directory . DIRECTORY_SEPARATOR . '*.json') ?: [] as $path) {
        $modified = @filemtime($path);
        if ($modified !== false && $modified + ROOM_TTL_SECONDS < $now) @unlink($path);
    }
}

function validate_envelope(mixed $value): array
{
    if (!is_array($value) || count($value) !== 3 || !array_key_exists('v', $value) ||
        !array_key_exists('iv', $value) || !array_key_exists('ciphertext', $value) || $value['v'] !== 1 ||
        !is_string($value['iv']) || strlen($value['iv']) !== 16 || preg_match(BASE64URL_PATTERN, $value['iv']) !== 1 ||
        !is_string($value['ciphertext']) || $value['ciphertext'] === '' || strlen($value['ciphertext']) > MAX_CIPHERTEXT_CHARS ||
        preg_match(BASE64URL_PATTERN, $value['ciphertext']) !== 1) {
        fail_request(400, 'Invalid encrypted signaling payload.');
    }
    return ['v' => 1, 'iv' => $value['iv'], 'ciphertext' => $value['ciphertext']];
}

function read_state($handle): array
{
    rewind($handle);
    $contents = stream_get_contents($handle, MAX_REQUEST_BYTES);
    if (!is_string($contents) || $contents === '') fail_request(404, 'Room not found or expired.');
    try {
        $state = json_decode($contents, true, 8, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        fail_request(500, 'Temporary signaling state is invalid.');
    }
    if (!is_array($state) || !isset($state['createdAt']) || !array_key_exists('offer', $state) || !array_key_exists('answer', $state)) {
        fail_request(500, 'Temporary signaling state is invalid.');
    }
    if (!is_int($state['createdAt']) || $state['createdAt'] + ROOM_TTL_SECONDS < time()) {
        fail_request(404, 'Room not found or expired.');
    }
    return $state;
}

function write_state($handle, array $state): void
{
    $json = json_encode($state, JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    rewind($handle);
    if (!ftruncate($handle, 0) || fwrite($handle, $json) !== strlen($json) || !fflush($handle)) {
        fail_request(500, 'Could not update temporary signaling state.');
    }
}

function consume_answer($handle, string $path, array $state): ?array
{
    if (!is_array($state['answer'])) return null;
    $answer = $state['answer'];
    $state['answer'] = null;
    write_state($handle, $state);
    @unlink($path);
    return $answer;
}

function envelopes_equal(array $left, array $right): bool
{
    return $left === $right;
}

if (defined('VOLTURA_SIGNAL_FUNCTIONS_ONLY')) return;

if (($_SERVER['REQUEST_METHOD'] ?? '') !== 'POST') fail_request(405, 'POST is required.');
$contentLength = filter_input(INPUT_SERVER, 'CONTENT_LENGTH', FILTER_VALIDATE_INT);
if (is_int($contentLength) && $contentLength > MAX_REQUEST_BYTES) fail_request(413, 'Request is too large.');
$raw = file_get_contents('php://input', false, null, 0, MAX_REQUEST_BYTES + 1);
if (!is_string($raw) || $raw === '' || strlen($raw) > MAX_REQUEST_BYTES) fail_request(400, 'Request body is empty or too large.');
try {
    $request = json_decode($raw, true, 8, JSON_THROW_ON_ERROR);
} catch (JsonException) {
    fail_request(400, 'Request body must be valid JSON.');
}
if (!is_array($request)) fail_request(400, 'Request body must be a JSON object.');

$operation = $request['op'] ?? null;
$room = $request['room'] ?? null;
if (!is_string($operation) || !is_string($room) || preg_match(ROOM_PATTERN, $room) !== 1) fail_request(400, 'Invalid operation or room.');
if (!in_array($operation, ['create', 'get_offer', 'set_answer', 'get_answer', 'delete'], true)) fail_request(400, 'Unknown operation.');

$directory = state_directory();
expire_stale_rooms($directory);
$path = room_path($room);

if ($operation === 'create') {
    $offer = validate_envelope($request['payload'] ?? null);
    $handle = @fopen($path, 'x+b');
    if ($handle === false) fail_request(409, 'Room already exists.');
    @chmod($path, 0600);
    if (!flock($handle, LOCK_EX)) {
        fclose($handle);
        @unlink($path);
        fail_request(500, 'Could not lock temporary signaling state.');
    }
    write_state($handle, ['createdAt' => time(), 'offer' => $offer, 'answer' => null]);
    flock($handle, LOCK_UN);
    fclose($handle);
    respond(201, ['ok' => true]);
}

if ($operation === 'delete') {
    if (is_file($path)) @unlink($path);
    respond(200, ['ok' => true]);
}

$handle = @fopen($path, 'r+b');
if ($handle === false) fail_request(404, 'Room not found or expired.');
if (!flock($handle, LOCK_EX)) {
    fclose($handle);
    fail_request(500, 'Could not lock temporary signaling state.');
}
$state = read_state($handle);

if ($operation === 'get_offer') {
    if (!is_array($state['offer'])) {
        flock($handle, LOCK_UN);
        fclose($handle);
        fail_request(404, 'Offer already consumed.');
    }
    $offer = $state['offer'];
    $state['offer'] = null;
    write_state($handle, $state);
    flock($handle, LOCK_UN);
    fclose($handle);
    respond(200, ['ok' => true, 'payload' => $offer]);
}

if ($operation === 'set_answer') {
    if (is_array($state['offer'])) {
        flock($handle, LOCK_UN);
        fclose($handle);
        fail_request(409, 'Offer has not been consumed.');
    }
    $answer = validate_envelope($request['payload'] ?? null);
    if (is_array($state['answer']) && envelopes_equal($state['answer'], $answer)) {
        flock($handle, LOCK_UN);
        fclose($handle);
        respond(200, ['ok' => true]);
    }
    if (is_array($state['answer'])) {
        flock($handle, LOCK_UN);
        fclose($handle);
        fail_request(409, 'Answer already exists.');
    }
    $state['answer'] = $answer;
    write_state($handle, $state);
    flock($handle, LOCK_UN);
    fclose($handle);
    respond(200, ['ok' => true]);
}

if ($operation === 'get_answer') {
    $answer = consume_answer($handle, $path, $state);
    if ($answer === null) {
        flock($handle, LOCK_UN);
        fclose($handle);
        respond(200, ['ok' => true, 'ready' => false]);
    }
    flock($handle, LOCK_UN);
    fclose($handle);
    respond(200, ['ok' => true, 'ready' => true, 'payload' => $answer]);
}

flock($handle, LOCK_UN);
fclose($handle);
fail_request(500, 'Signaling operation was not completed.');

<?php
declare(strict_types=1);

define('VOLTURA_SIGNAL_FUNCTIONS_ONLY', true);
require dirname(__DIR__) . DIRECTORY_SEPARATOR . 'signal.php';

$answer = ['v' => 1, 'iv' => 'AAAAAAAAAAAAAAAA', 'ciphertext' => 'AA'];
if (!envelopes_equal($answer, $answer)) throw new RuntimeException('Identical answer retries must be idempotent.');
if (envelopes_equal($answer, ['v' => 1, 'iv' => 'BBBBBBBBBBBBBBBB', 'ciphertext' => 'AA'])) {
    throw new RuntimeException('A different answer must not be accepted as an idempotent retry.');
}

$path = tempnam(sys_get_temp_dir(), 'voltura-signal-test-');
if ($path === false) throw new RuntimeException('Could not create a signaling test file.');
$first = fopen($path, 'w+b');
if ($first === false) throw new RuntimeException('Could not open the first signaling test handle.');
write_state($first, [
    'createdAt' => time(),
    'offer' => null,
    'answer' => $answer
]);
$second = fopen($path, 'r+b');
if ($second === false) throw new RuntimeException('Could not open the concurrent signaling test handle.');

try {
    if (!flock($first, LOCK_EX)) throw new RuntimeException('Could not lock the first signaling test handle.');
    $consumed = read_answer(read_state($first));
    flock($first, LOCK_UN);
    if (!is_array($consumed)) throw new RuntimeException('The first reader did not retrieve the answer.');

    if (!flock($second, LOCK_EX)) throw new RuntimeException('Could not lock the second signaling test handle.');
    $remaining = read_state($second);
    flock($second, LOCK_UN);
    if (!envelopes_equal($remaining['answer'], $answer)) throw new RuntimeException('The answer was not retained for an idempotent retry.');
} finally {
    fclose($second);
    fclose($first);
    @unlink($path);
}

echo "signal-store-tests-passed\n";

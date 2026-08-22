<?php
declare(strict_types=1);

require_once __DIR__ . '/lib.php';
require_once __DIR__ . '/../telemetry/admin.php';

const AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION = 'air_telemetry_cleanup_preview_v1';
air_screen_require_admin();

$notice = '';
$error = '';
$preview = null;
$cleanupRequest = null;
$cleanupResult = null;
$cleanupToken = null;
$databaseState = air_telemetry_admin_database_state();
$database = $databaseState['database'];
if (!($database instanceof PDO)) {
    http_response_code(503);
    unset($_SESSION[AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION]);
    $error = $databaseState['error'];
}

if ($_SERVER['REQUEST_METHOD'] !== 'POST' && $database instanceof PDO) {
    try {
        air_telemetry_maybe_cleanup($database);
    } catch (Throwable) {
        $error = 'Automatic telemetry retention could not run. No product function is affected; retry this page after the database issue is resolved.';
    }
}

if ($_SERVER['REQUEST_METHOD'] === 'POST') {
    air_screen_require_csrf();
}

if ($_SERVER['REQUEST_METHOD'] === 'POST' && $database instanceof PDO) {
    try {
        $cleanupRequest = air_telemetry_admin_cleanup_request($_POST);
        $stage = isset($_POST['cleanup_stage']) && is_string($_POST['cleanup_stage'])
            ? $_POST['cleanup_stage'] : '';
        if ($stage === 'preview') {
            $preview = air_telemetry_admin_cleanup_preview($database, $cleanupRequest);
            $authorization = air_telemetry_admin_create_cleanup_authorization($cleanupRequest, $preview);
            $_SESSION[AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION] = $authorization;
            $cleanupToken = $authorization['token'];
        } elseif ($stage === 'execute' && ($_POST['cleanup_confirmed'] ?? '') === 'yes') {
            $submittedToken = isset($_POST['cleanup_token']) && is_string($_POST['cleanup_token'])
                ? $_POST['cleanup_token'] : '';
            $expected = air_telemetry_admin_consume_cleanup_authorization(
                $_SESSION,
                AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION,
                $cleanupRequest,
                $submittedToken);
            $cleanupResult = air_telemetry_admin_cleanup_chunk($database, $cleanupRequest, $expected);
            if (array_sum($cleanupResult['remaining']) > 0) {
                $authorization = air_telemetry_admin_create_cleanup_authorization(
                    $cleanupRequest,
                    $cleanupResult['remaining']);
                $_SESSION[AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION] = $authorization;
                $cleanupToken = $authorization['token'];
            }
            $deletedCount = array_sum($cleanupResult['deleted']);
            $remainingCount = array_sum($cleanupResult['remaining']);
            $notice = 'Committed deletion of ' . $deletedCount . ' telemetry row' . ($deletedCount === 1 ? '' : 's') . '.';
            if ($remainingCount > 0) {
                $notice .= ' ' . $remainingCount . ' row' . ($remainingCount === 1 ? '' : 's') . ' remain. Continue deleting to finish.';
            }
        } else {
            throw new InvalidArgumentException('Preview the cleanup and explicitly confirm it before deletion.');
        }
    } catch (AirTelemetryCleanupScopeChanged) {
        http_response_code(409);
        try {
            $preview = air_telemetry_admin_cleanup_preview($database, $cleanupRequest);
            $authorization = air_telemetry_admin_create_cleanup_authorization($cleanupRequest, $preview);
            $_SESSION[AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION] = $authorization;
            $cleanupToken = $authorization['token'];
            $error = 'The eligible row counts changed after preview. Nothing was deleted; review the refreshed counts and confirm again.';
        } catch (Throwable) {
            http_response_code(503);
            $preview = null;
            $error = 'The eligible row counts changed after preview. Nothing was deleted, and refreshed counts are temporarily unavailable.';
        }
    } catch (InvalidArgumentException $exception) {
        http_response_code(400);
        $error = $exception->getMessage();
    } catch (AirTelemetryCleanupOutcomeUnknown) {
        http_response_code(503);
        unset($_SESSION[AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION]);
        $preview = null;
        $error = 'The database could not confirm whether the cleanup commit or rollback completed. Reload this page and preview the current rows before any retry.';
    } catch (AirTelemetryCleanupFailedSafely) {
        http_response_code(503);
        $error = 'The telemetry cleanup failed before commit. No deletion was committed; preview again after the database issue is resolved.';
    } catch (Throwable) {
        http_response_code(503);
        unset($_SESSION[AIR_TELEMETRY_CLEANUP_PREVIEW_SESSION]);
        $preview = null;
        $error = 'The telemetry cleanup result could not be verified. Reload this page and preview the current rows before any retry.';
    }
}

try {
    $filters = air_telemetry_admin_filters($_GET);
} catch (InvalidArgumentException $exception) {
    http_response_code(400);
    $filterError = $exception->getMessage();
    $error = $error === '' ? $filterError : $error . ' ' . $filterError;
    $filters = air_telemetry_admin_filters([]);
}

if (!($database instanceof PDO)) {
    $dashboard = null;
} else {
    try {
        $dashboard = air_telemetry_admin_dashboard($database, $filters);
    } catch (Throwable) {
        http_response_code(503);
        $dashboard = null;
        $dashboardError = 'Usage statistics are temporarily unavailable.';
        $error = $error === '' ? $dashboardError : $error . ' ' . $dashboardError;
    }
}

$body = '';
if (getenv('VOLTURA_AIR_SITE_DEV') === '1') {
    $body .= '<p class="telemetry-notice">Local development dashboard. Values may be sample data.</p>';
}
if ($notice !== '') {
    $body .= '<p class="telemetry-notice" role="status">' . air_screen_h($notice) . '</p>';
}
if ($error !== '') {
    $body .= '<p class="catalog-moderation-error" role="alert">' . air_screen_h($error) . '</p>';
}

$body .= telemetry_filter_form($filters);
if ($dashboard === null) {
    $body .= '<p class="catalog-empty">No aggregate data can be shown until the telemetry schema and database connection are healthy.</p>';
} else {
    $body .= telemetry_dashboard_html($dashboard);
}
if ($database instanceof PDO) {
    $body .= '<details class="telemetry-maintenance"><summary>Data cleanup</summary><div class="telemetry-maintenance-content">'
        . telemetry_cleanup_html($preview, $cleanupRequest, $cleanupResult, $cleanupToken) . '</div></details>';
} else {
    $body .= '<p class="catalog-empty">Telemetry cleanup is unavailable until the database connection is healthy.</p>';
}

air_screen_layout('Usage statistics', $body);

function telemetry_filter_form(array $filters): string
{
    $links = '';
    foreach (['7', '30', '90', '180'] as $range) {
        $current = $filters['range'] === $range;
        $label = $range === '180' ? 'All (180 days)' : $range . ' days';
        $links .= '<a href="?range=' . $range . '"' . ($current ? ' aria-current="page"' : '') . '>' . $label . '</a>';
    }
    return '<nav class="telemetry-ranges" aria-label="Date range">' . $links . '</nav>';
}

function telemetry_dashboard_html(array $dashboard): string
{
    $summary = $dashboard['summary'];
    $installations = (int)($summary['installations'] ?? 0);
    $html = '<section class="telemetry-section" aria-labelledby="telemetry-overview"><h2 id="telemetry-overview">Active installations</h2>'
        . '<p class="telemetry-total">' . number_format($installations) . '</p>';
    if (!$dashboard['trend']) {
        $html .= '<p class="catalog-empty">No activity in this range.</p>';
    } else {
        $rows = '';
        foreach ($dashboard['trend'] as $row) {
            $rows .= '<tr><th scope="row">' . air_screen_h((string)$row['activity_date']) . '</th><td>' . number_format((int)$row['installations']) . '</td></tr>';
        }
        $html .= '<div class="telemetry-table-wrap"><table><thead><tr><th scope="col">UTC date</th><th scope="col">Installations</th></tr></thead><tbody>' . $rows . '</tbody></table></div>';
    }
    $html .= '</section>';

    $versionRows = '';
    foreach ($dashboard['versions'] as $row) {
        $count = (int)$row['installations'];
        $percentage = $installations > 0 ? round(($count / $installations) * 100, 1) : 0;
        $versionRows .= '<tr><th scope="row">' . air_screen_h((string)$row['host_version']) . '</th><td>' . number_format($count) . '</td><td>' . number_format($percentage, 1) . '%</td></tr>';
    }
    $html .= '<section class="telemetry-section"><h2>Versions in use</h2>'
        . telemetry_table_or_empty('Host version', '<th scope="col">Installations</th><th scope="col">Share</th>', $versionRows)
        . '</section>';

    $connections = [
        'Standard Local' => (int)$summary['standard_local'],
        'Enhanced Direct' => (int)$summary['enhanced_direct'],
        'Relay' => (int)$summary['relay'],
    ];
    $connectionRows = '';
    foreach ($connections as $label => $count) {
        $connectionRows .= '<tr><th scope="row">' . air_screen_h($label) . '</th><td>' . number_format($count) . '</td></tr>';
    }
    $html .= '<section class="telemetry-section"><h2>Connection methods</h2><div class="telemetry-table-wrap"><table><thead><tr><th scope="col">Method</th><th scope="col">Connections</th></tr></thead><tbody>' . $connectionRows . '</tbody></table></div></section>';

    $featureLabels = [
        'trackpad' => 'Trackpad', 'keyboard' => 'Keyboard', 'dictation' => 'Dictation',
        'mediaControls' => 'Media controls', 'presentation' => 'Presentation',
        'customScreens' => 'Custom screens', 'files' => 'Files', 'screenViewing' => 'Screen viewing',
        'phoneWebcam' => 'Phone webcam', 'gyroMouse' => 'Gyro mouse',
    ];
    $featureRows = '';
    foreach ($featureLabels as $key => $label) {
        $feature = $dashboard['features'][$key];
        $featureRows .= '<tr><th scope="row">' . $label . '</th><td>' . number_format($feature['installations']) . '</td><td>' . number_format($feature['sessions']) . '</td></tr>';
    }
    $html .= '<section class="telemetry-section"><h2>Features used</h2><div class="telemetry-table-wrap"><table><thead><tr><th scope="col">Feature</th><th scope="col">Installations</th><th scope="col">Sessions</th></tr></thead><tbody>' . $featureRows . '</tbody></table></div></section>';

    return $html;
}

function telemetry_cleanup_html(?array $preview, ?array $request, ?array $result, ?string $cleanupToken): string
{
    $csrf = air_screen_h(air_screen_csrf());
    $today = gmdate('Y-m-d');
    $html = '<section id="telemetry-cleanup" class="telemetry-section telemetry-cleanup"><h2>Telemetry cleanup</h2>'
        . '<form method="post" action="#telemetry-cleanup" data-loading-label="Preparing cleanup preview"><input type="hidden" name="csrf" value="' . $csrf . '"><input type="hidden" name="cleanup_stage" value="preview">'
        . '<fieldset><legend>What to delete</legend><label class="telemetry-cleanup-choice"><input type="radio" name="cleanup_action" value="retention" checked><span>Usage statistics older than 180 days; upload safeguards older than 24 hours</span></label>'
        . '<label class="telemetry-cleanup-choice"><input type="radio" name="cleanup_action" value="before"><span>Data before a date</span></label>'
        . '<label class="telemetry-cleanup-date">Delete data before (YYYY-MM-DD)<input type="text" name="cleanup_cutoff" value="' . $today . '" placeholder="YYYY-MM-DD" pattern="[0-9]{4}-[0-9]{2}-[0-9]{2}" maxlength="10" inputmode="numeric" autocomplete="off" spellcheck="false"></label>'
        . '<label class="telemetry-cleanup-choice"><input type="radio" name="cleanup_action" value="all"><span>All data</span></label></fieldset>'
        . '<button type="submit">Preview</button><span class="telemetry-submit-status" data-submit-status role="status" hidden></span></form>';

    $state = $result['remaining'] ?? $preview;
    if ($state !== null && $request !== null) {
        $labels = [
            'air_telemetry_daily' => 'Daily aggregates', 'air_telemetry_batches' => 'Batch deduplication',
            'air_telemetry_rate_buckets' => 'Rate buckets', 'air_telemetry_ingest_daily' => 'Request totals',
        ];
        $rows = '';
        foreach ($labels as $table => $label) {
            $rows .= '<tr><th scope="row">' . $label . '</th><td>' . number_format((int)($state[$table] ?? 0)) . '</td></tr>';
        }
        $description = match ($request['action']) {
            'retention' => 'aggregate and delivery-health dates before ' . $request['aggregate_cutoff']
                . ' UTC; deduplication and rate-bucket times before ' . $request['short_cutoff'] . ' UTC',
            'before' => 'UTC dates before ' . $request['cutoff'],
            'all' => 'all retained telemetry rows',
        };
        $remaining = array_sum($state);
        $html .= '<div class="telemetry-cleanup-preview"><h3>' . ($result === null ? 'Cleanup preview' : 'Remaining cleanup') . '</h3><p>Scope: ' . air_screen_h($description) . '.</p><div class="telemetry-table-wrap"><table><thead><tr><th scope="col">Telemetry data</th><th scope="col">Rows eligible</th></tr></thead><tbody>' . $rows . '</tbody></table></div>';
        if ($remaining > 0) {
            $html .= '<form method="post" action="#telemetry-cleanup" data-loading-label="Deleting one bounded telemetry chunk"><input type="hidden" name="csrf" value="' . $csrf . '"><input type="hidden" name="cleanup_stage" value="execute"><input type="hidden" name="cleanup_action" value="' . air_screen_h($request['action']) . '">';
            $html .= '<input type="hidden" name="cleanup_token" value="' . air_screen_h($cleanupToken ?? '') . '">';
            if ($request['cutoff'] !== null) {
                $html .= '<input type="hidden" name="cleanup_cutoff" value="' . air_screen_h($request['cutoff']) . '">';
            }
            if ($request['action'] === 'retention') {
                $html .= '<input type="hidden" name="cleanup_aggregate_cutoff" value="' . air_screen_h($request['aggregate_cutoff']) . '">'
                    . '<input type="hidden" name="cleanup_short_cutoff" value="' . air_screen_h($request['short_cutoff']) . '">';
            }
            if ($result === null) {
                $html .= '<label class="telemetry-cleanup-confirm"><input type="checkbox" name="cleanup_confirmed" value="yes" required><span>Confirm deletion</span></label><button class="catalog-delete-button" type="submit">Delete</button>';
            } else {
                $html .= '<input type="hidden" name="cleanup_confirmed" value="yes"><button class="catalog-delete-button" type="submit">Continue deleting</button>';
            }
            $html .= '<span class="telemetry-submit-status" data-submit-status role="status" hidden></span></form>';
        } else {
            $html .= '<p class="telemetry-notice" role="status">No eligible telemetry rows remain.</p>';
        }
        $html .= '</div>';
    }
    return $html . '</section>';
}

function telemetry_table_or_empty(string $firstHeading, string $remainingHeadings, string $rows): string
{
    return $rows === '' ? '<p class="catalog-empty">No version activity matches this range.</p>'
        : '<div class="telemetry-table-wrap"><table><thead><tr><th scope="col">' . air_screen_h($firstHeading) . '</th>' . $remainingHeadings . '</tr></thead><tbody>' . $rows . '</tbody></table></div>';
}

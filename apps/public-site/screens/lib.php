<?php
declare(strict_types=1);

const AIR_SCREEN_MAX_BYTES = 8 * 1024 * 1024;
const AIR_SCREEN_ORIGIN = 'https://voltura.se/air';
const AIR_SCREEN_MAX_BUTTON_ROWS = 6;

session_set_cookie_params([
    'httponly' => true,
    'secure' => getenv('VOLTURA_AIR_SITE_DEV') !== '1',
    'samesite' => 'Lax',
]);
session_start();

function air_screen_config(): array
{
    $path = getenv('VOLTURA_AIR_SCREENS_CONFIG');
    if (!$path) {
        $path = __DIR__ . '/../config.php';
    }
    if (!is_file($path)) {
        throw new RuntimeException('The custom-screen catalog is not configured.');
    }
    $config = require $path;
    if (!is_array($config)) {
        throw new RuntimeException('The custom-screen catalog configuration is invalid.');
    }
    return $config;
}

function air_screen_db(): PDO
{
    static $pdo;
    if (!$pdo) {
        $config = air_screen_config();
        $pdo = new PDO(
            (string)($config['dsn'] ?? ''),
            (string)($config['username'] ?? ''),
            (string)($config['password'] ?? ''),
            [PDO::ATTR_ERRMODE => PDO::ERRMODE_EXCEPTION, PDO::ATTR_DEFAULT_FETCH_MODE => PDO::FETCH_ASSOC]
        );
    }
    return $pdo;
}

function air_screen_storage_path(): string
{
    $path = (string)(air_screen_config()['storage_path'] ?? '');
    if ($path === '' || str_starts_with(realpath(dirname($path)) ?: '', realpath(__DIR__) ?: '')) {
        throw new RuntimeException('Catalog storage must be outside the public catalog code directory.');
    }
    if (!is_dir($path) && !mkdir($path, 0700, true) && !is_dir($path)) {
        throw new RuntimeException('Catalog storage could not be created.');
    }
    return $path;
}

function air_screen_csrf(): string
{
    return $_SESSION['air_screen_csrf'] ??= bin2hex(random_bytes(32));
}

function air_screen_require_csrf(): void
{
    if (!hash_equals((string)($_SESSION['air_screen_csrf'] ?? ''), (string)($_POST['csrf'] ?? ''))) {
        http_response_code(403);
        exit('Invalid request token.');
    }
}

function air_screen_user(): ?array
{
    return $_SESSION['air_screen_user'] ?? null;
}

function air_screen_require_user(): array
{
    $user = air_screen_user();
    if (!$user) {
        header('Location: login.php');
        exit;
    }
    return $user;
}

function air_screen_require_admin(): array
{
    $user = air_screen_require_user();
    if (($user['role'] ?? '') !== 'admin') {
        http_response_code(403);
        exit('Administrator access required.');
    }
    return $user;
}

function air_screen_redirect(string $location): never
{
    header('Location: ' . $location);
    exit;
}

function air_screen_h(string $value): string
{
    return htmlspecialchars($value, ENT_QUOTES | ENT_SUBSTITUTE, 'UTF-8');
}

function air_screen_tag_pills(string $tags): string
{
    $values = [];
    $seen = [];
    foreach (preg_split('/\s*,\s*/u', trim($tags), -1, PREG_SPLIT_NO_EMPTY) ?: [] as $tag) {
        $tag = trim($tag);
        $key = strtolower($tag);
        if ($tag === '' || isset($seen[$key])) {
            continue;
        }
        $seen[$key] = true;
        $values[] = '<span class="catalog-tag-pill is-static">' . air_screen_h($tag) . '</span>';
    }
    return $values === []
        ? '<span class="catalog-tags-empty">None supplied.</span>'
        : '<span class="catalog-tag-list" aria-label="Tags">' . implode('', $values) . '</span>';
}

function air_screen_local_catalog_source(): ?string
{
    if (getenv('VOLTURA_AIR_SITE_DEV') !== '1') {
        return null;
    }

    $host = (string)($_SERVER['HTTP_HOST'] ?? '127.0.0.1:8765');
    if (!preg_match('/^(?:127\.0\.0\.1|localhost)(?::[0-9]{1,5})?$/i', $host)) {
        $host = '127.0.0.1:8765';
    }
    return 'http://' . $host . '/screens';
}

function air_screen_notification_email(
    string $title,
    string $content,
    string $actionLabel,
    string $actionUrl): string
{
    return '<!doctype html>
        <html>
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' . air_screen_h($title) . '</title></head>
        <body style="margin:0;padding:0;background-color:#101418;font-family:Arial,Helvetica,sans-serif;color:#172027;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#101418" style="width:100%;border-collapse:collapse;background-color:#101418;">
                <tr><td align="center" style="padding:32px 12px;">
                    <table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0" bgcolor="#f7f2e9" style="width:100%;max-width:600px;border-collapse:separate;background-color:#f7f2e9;border-top:6px solid #0d8f7d;border-right:1px solid #3b4a52;border-bottom:1px solid #3b4a52;border-left:1px solid #3b4a52;">
                        <tr><td style="padding:32px 36px 18px;font-size:26px;line-height:1.25;font-weight:bold;color:#172027;">' . air_screen_h($title) . '</td></tr>
                        <tr><td style="padding:0 36px 28px;">' . $content . '</td></tr>
                        <tr><td style="padding:0 36px 32px;">
                            <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
                                <td bgcolor="#0d8f7d" style="padding:13px 22px;background-color:#0d8f7d;"><a href="' . air_screen_h($actionUrl) . '" style="display:block;color:#ffffff;text-decoration:none;font-size:16px;line-height:20px;font-weight:bold;">' . air_screen_h($actionLabel) . '</a></td>
                            </tr></table>
                        </td></tr>
                        <tr><td style="padding:20px 36px;border-top:1px solid #d7d0c5;color:#71818a;font-size:13px;line-height:19px;">
                            <a href="' . AIR_SCREEN_ORIGIN . '/" style="color:#0b776a;text-decoration:underline;">Voltura Air</a> custom-screen catalog<br>
                            This is an automated email from Voltura Air. Replies are not monitored.
                        </td></tr>
                    </table>
                </td></tr>
            </table>
        </body>
        </html>';
}

function air_screen_notification_subject(string $status, string $screenName): string
{
    $safeName = trim((string)preg_replace('/[\r\n]+/u', ' ', $screenName));
    $subject = $status . ': ' . ($safeName === '' ? 'Custom screen' : $safeName) . ' - Voltura Air';
    return preg_match('/[^\x20-\x7E]/', $subject)
        ? mb_encode_mimeheader($subject, 'UTF-8', 'B', "\r\n")
        : $subject;
}

function air_screen_notify_moderators(
    string $packageId,
    string $screenName,
    string $authorName,
    string $description,
    string $tags): void
{
    if (getenv('VOLTURA_AIR_SITE_DEV') === '1') {
        return;
    }

    try {
        $statement = air_screen_db()->prepare(
            "SELECT email FROM air_screen_users WHERE role = 'admin' ORDER BY id"
        );
        $statement->execute();
        $recipients = $statement->fetchAll(PDO::FETCH_COLUMN);
        if (!$recipients) {
            error_log('Voltura Air moderation notification has no administrator recipient for package ' . $packageId . '.');
            return;
        }

        $moderationUrl = AIR_SCREEN_ORIGIN . '/screens/admin.php';
        $subject = air_screen_notification_subject('Review needed', $screenName);
        $safeName = air_screen_h($screenName);
        $safeAuthor = air_screen_h($authorName);
        $safeDescription = nl2br(air_screen_h($description));
        $safeTags = air_screen_h($tags === '' ? 'None' : $tags);
        $content = '
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:100%;border-collapse:collapse;background-color:#ffffff;border:1px solid #ded7cc;">
                <tr><td style="padding:18px 20px;">
                    <div style="font-size:19px;line-height:26px;font-weight:bold;color:#172027;">' . $safeName . '</div>
                    <div style="padding-top:6px;color:#4c5d66;font-size:15px;line-height:22px;">Submitted by ' . $safeAuthor . '</div>
                </td></tr>
            </table>
            <div style="padding-top:24px;font-size:15px;line-height:23px;color:#172027;">
                <div style="padding-bottom:7px;font-weight:bold;">Author notes</div>
                <div>' . ($safeDescription === '' ? 'No notes supplied.' : $safeDescription) . '</div>
                <div style="padding-top:18px;"><strong>Tags:</strong> ' . $safeTags . '</div>
            </div>';
        $body = air_screen_notification_email(
            'Custom screen awaiting moderation',
            $content,
            'Review submission',
            $moderationUrl
        );
        $headers = air_screen_html_mail_headers();

        foreach ($recipients as $recipient) {
            if (!is_string($recipient) || !filter_var($recipient, FILTER_VALIDATE_EMAIL)) {
                continue;
            }
            if (!@mail($recipient, $subject, $body, $headers)) {
                error_log('Voltura Air moderation notification failed for package ' . $packageId . '.');
            }
        }
    } catch (Throwable $exception) {
        error_log('Voltura Air moderation notification failed for package ' . $packageId . '.');
    }
}

function air_screen_notify_screen_report(
    string $packageId,
    string $screenName,
    string $reporterEmail,
    string $reason): void
{
    if (getenv('VOLTURA_AIR_SITE_DEV') === '1') {
        return;
    }
    if ($packageId === '' || !filter_var($reporterEmail, FILTER_VALIDATE_EMAIL) || $reason === '') {
        error_log('Voltura Air screen report notification has invalid input.');
        return;
    }

    try {
        $content = '
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:100%;border-collapse:collapse;background-color:#ffffff;border:1px solid #ded7cc;">
                <tr><td style="padding:18px 20px;font-size:19px;line-height:26px;font-weight:bold;color:#172027;">' . air_screen_h($screenName) . '</td></tr>
            </table>
            <div style="padding-top:24px;font-size:15px;line-height:23px;color:#172027;">
                <div style="padding-bottom:7px;font-weight:bold;">Reporter email</div>
                <div>' . air_screen_h($reporterEmail) . '</div>
                <div style="padding-top:18px;padding-bottom:7px;font-weight:bold;">Reason for review</div>
                <div>' . nl2br(air_screen_h($reason)) . '</div>
            </div>';
        $body = air_screen_notification_email(
            'Custom screen report received',
            $content,
            'View reported screen',
            AIR_SCREEN_ORIGIN . '/screens/view.php?id=' . rawurlencode($packageId)
        );
        if (!@mail('air@voltura.se', air_screen_notification_subject('Screen reported', $screenName), $body, air_screen_html_mail_headers())) {
            error_log('Voltura Air screen report notification failed for package ' . $packageId . '.');
        }
    } catch (Throwable $exception) {
        error_log('Voltura Air screen report notification failed for package ' . $packageId . '.');
    }
}

function air_screen_notify_submitter_status(
    string $packageId,
    string $screenName,
    string $recipient,
    string $status,
    string $reviewFeedback): void
{
    if (getenv('VOLTURA_AIR_SITE_DEV') === '1') {
        return;
    }
    if (!in_array($status, ['approved', 'rejected'], true) ||
        !filter_var($recipient, FILTER_VALIDATE_EMAIL)) {
        error_log('Voltura Air submitter status notification has invalid input for package ' . $packageId . '.');
        return;
    }

    try {
        $approved = $status === 'approved';
        $statusLabel = $approved ? 'approved' : 'rejected';
        $title = 'Your custom screen was ' . $statusLabel;
        $subject = air_screen_notification_subject($approved ? 'Approved' : 'Rejected', $screenName);
        $destination = $approved
            ? AIR_SCREEN_ORIGIN . '/screens/view.php?id=' . rawurlencode($packageId)
            : AIR_SCREEN_ORIGIN . '/screens/upload.php#submissions';
        $actionLabel = $approved ? 'View published screen' : 'View my submissions';
        $feedback = $reviewFeedback !== ''
            ? '<div style="padding-top:24px;font-size:15px;line-height:23px;color:#172027;"><div style="padding-bottom:7px;font-weight:bold;">Reviewer feedback</div><div>' . nl2br(air_screen_h($reviewFeedback)) . '</div></div>'
            : '';
        $content = '
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#ffffff" style="width:100%;border-collapse:collapse;background-color:#ffffff;border:1px solid #ded7cc;">
                <tr><td style="padding:18px 20px;font-size:19px;line-height:26px;font-weight:bold;color:#172027;">' . air_screen_h($screenName) . '</td></tr>
            </table>
            ' . $feedback;
        $body = air_screen_notification_email(
            $title,
            $content,
            $actionLabel,
            $destination
        );
        if (!@mail($recipient, $subject, $body, air_screen_html_mail_headers())) {
            error_log('Voltura Air submitter status notification failed for package ' . $packageId . '.');
        }
    } catch (Throwable $exception) {
        error_log('Voltura Air submitter status notification failed for package ' . $packageId . '.');
    }
}

function air_screen_html_mail_headers(): string
{
    return "From: no-reply@voltura.se\r\n" .
        "Reply-To: no-reply@voltura.se\r\n" .
        "MIME-Version: 1.0\r\n" .
        "Content-Type: text/html; charset=UTF-8\r\n";
}

function air_screen_toast(string $message): string
{
    return '<div class="catalog-toast" role="status"><span class="catalog-toast-badge" aria-hidden="true">&#10003;</span><span class="catalog-toast-copy"><small>Custom screens</small><strong>' . air_screen_h($message) . '</strong></span></div>';
}

function air_screen_value(array $value, string $key): mixed
{
    return $value[$key] ?? null;
}

function air_screen_layout(
    string $title,
    string $body,
    bool $showCatalogBackLink = true): void
{
    $user = air_screen_user();
    echo '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">';
    echo '<title>' . air_screen_h($title) . ' - Voltura Air</title><meta name="theme-color" content="#101418">';
    echo '<link rel="icon" href="../assets/voltura-air-icon.svg" type="image/svg+xml"><link rel="icon" href="../favicon-32.png" sizes="32x32" type="image/png"><link rel="icon" href="../favicon-16.png" sizes="16x16" type="image/png"><link rel="icon" href="../favicon.ico" sizes="any"><link rel="apple-touch-icon" href="../apple-touch-icon.png"><link rel="stylesheet" href="../styles.css"><script src="preview.js" defer></script><script src="tag-editor.js" defer></script></head><body class="catalog-page">';
    echo '<header class="site-header"><a class="brand" href="../" aria-label="Voltura Air home"><img src="../assets/voltura-air-icon.svg" alt="" width="36" height="36"><span>Voltura Air</span></a><nav aria-label="Site navigation">';
    echo '<a href="../#features">Features</a><a href="../#compare">Compare</a><a href="../#screens">Screenshots</a><a href="./" aria-current="page">Custom screens</a><a href="../#setup">Setup</a><a href="../#trust">Privacy</a><a href="../#source">Develop</a><a href="../#download">Download</a>';
    if ($user) {
        echo '<a href="upload.php" aria-label="Upload a custom screen" title="Upload a custom screen">Upload screen</a>';
        echo '<a href="upload.php#submissions" aria-label="View my custom screen submissions" title="View my custom screen submissions">My submissions</a>';
        if (($user['role'] ?? '') === 'admin') {
            echo '<a href="admin.php" aria-label="Moderate custom screens" title="Moderate custom screens">Moderate screens</a>';
        }
        echo '<form class="catalog-signout" method="post" action="logout.php"><input type="hidden" name="csrf" value="' . air_screen_h(air_screen_csrf()) . '"><button type="submit" aria-label="Sign out from Voltura Air Custom Screens portal" title="Sign out from Voltura Air Custom Screens portal">Sign out</button></form>';
    } else {
        echo '<a href="login.php">Sign in</a><a href="register.php">Create account</a>';
    }
    $eyebrow = $showCatalogBackLink
        ? '<a class="eyebrow catalog-heading-back" href="./" aria-label="Browse community library of custom screens"><span aria-hidden="true">&larr;</span> Community library</a>'
        : '<p class="eyebrow">Community library</p>';
    echo '</nav></header><main class="catalog-main"><header class="catalog-heading">' . $eyebrow . '<h1>' . air_screen_h($title) . '</h1></header><div class="catalog-content">' . $body . '</div></main><footer class="catalog-footer"><a href="https://voltura.se/" aria-label="Voltura AB home">Voltura AB</a><a href="https://github.com/voltura/voltura-air">GitHub</a><a href="../stats.html">Code statistics</a><a href="../sitemap.php">Sitemap</a><a href="https://github.com/voltura/voltura-air/blob/main/PRIVACY.md">Privacy</a><a href="https://github.com/voltura/voltura-air/blob/main/LICENSE">MIT License</a><a href="https://github.com/voltura/voltura-air/blob/main/THIRD-PARTY-NOTICES.md">Third-party notices</a><a href="https://www.linkedin.com/in/joakim-voltura/">LinkedIn</a><a href="https://ko-fi.com/voltura">Ko-fi</a><a href="https://www.paypal.me/voltura">PayPal</a><a href="../">voltura.se/air</a></footer></body></html>';
}

function air_screen_preview(string $json, string $label, bool $compact = false, ?string $packageId = null): string
{
    $package = json_decode($json, true);
    $screen = is_array($package) ? air_screen_value($package, 'screen') : null;
    $sections = is_array($screen) ? air_screen_value($screen, 'sections') : null;
    if (!is_array($sections)) {
        return '<div class="screen-preview screen-preview-empty' . ($compact ? ' compact' : '') . '" aria-label="Preview unavailable"><span>Preview unavailable</span></div>';
    }

    if (!$compact && $packageId !== null) {
        return '<div class="screen-preview real-device-preview" data-orientation="portrait" aria-label="Preview of ' . air_screen_h($label) . '"><div class="screen-preview-toolbar"><label><span>Device</span><select class="screen-preview-device" aria-label="Preview device"><option value="generic-phone" data-width="360" data-height="640">Generic phone (360 &times; 640)</option><option value="generic-tablet" data-width="800" data-height="1180">Generic tablet (800 &times; 1180)</option><option value="compact-android" data-width="360" data-height="780">Compact Android (360 &times; 780)</option><option value="iphone-se" data-width="375" data-height="667">iPhone SE Small (375 &times; 667)</option><option value="common-iphone" data-width="390" data-height="844">Common iPhone (390 &times; 844)</option><option value="iphone-pro" data-width="393" data-height="852">iPhone Pro (393 &times; 852)</option><option value="large-android" data-width="412" data-height="915">Large Android (412 &times; 915)</option><option value="iphone-pro-max" data-width="430" data-height="932">iPhone Pro Max (430 &times; 932)</option><option value="small-tablet" data-width="768" data-height="1024">Small Tablet (768 &times; 1024)</option><option value="ipad-air" data-width="820" data-height="1180">iPad Air (820 &times; 1180)</option></select></label><span class="screen-preview-size" aria-live="polite">360 &times; 640</span><button class="screen-preview-rotate" type="button" aria-label="Rotate preview" title="Rotate preview">&#8635; <span>Rotate</span></button></div><div class="screen-preview-stage"><div class="screen-preview-frame"><iframe src="preview-frame.php?id=' . rawurlencode($packageId) . '" title="Interactive preview of ' . air_screen_h($label) . '" loading="lazy" sandbox="allow-scripts"></iframe></div></div></div>';
    }

    $html = '<div class="screen-preview' . ($compact ? ' compact' : '') . '" data-device="phone" data-orientation="portrait" aria-label="Preview of ' . air_screen_h($label) . '"><div class="screen-preview-toolbar"><label><span>Device</span><select class="screen-preview-device" aria-label="Preview device"><option value="phone">Generic phone</option><option value="tablet">Generic tablet</option></select></label><span class="screen-preview-size" aria-live="polite">360 &times; 640</span><button class="screen-preview-rotate" type="button" aria-label="Rotate preview" title="Rotate preview">&#8635; <span>Rotate</span></button></div><div class="screen-preview-stage"><div class="screen-preview-frame"><div class="screen-preview-bar"><i></i><i></i><i></i></div><div class="screen-preview-viewport" tabindex="0" aria-label="Scrollable screen preview"><div class="screen-preview-content">';
    foreach ($sections as $sectionIndex => $section) {
        if (!is_array($section)) { continue; }
        $title = (string)(air_screen_value($section, 'name') ?? '');
        $kind = strtolower((string)(air_screen_value($section, 'kind') ?? 'buttons'));
        $width = max(1, min(12, (int)(air_screen_value($section, 'widthColumns') ?? 6)));
        $portrait = air_screen_value($section, 'portrait');
        $landscape = air_screen_value($section, 'landscape');
        $portraitWidth = is_array($portrait) ? (air_screen_value($portrait, 'widthColumns') ?? $width) : $width;
        $landscapeWidth = is_array($landscape) ? (air_screen_value($landscape, 'widthColumns') ?? $width) : $width;
        $portraitOrder = is_array($portrait) ? (air_screen_value($portrait, 'order') ?? $sectionIndex) : $sectionIndex;
        $landscapeOrder = is_array($landscape) ? (air_screen_value($landscape, 'order') ?? $sectionIndex) : $sectionIndex;
        $hidden = (is_array($portrait) && air_screen_value($portrait, 'visible') === false ? ' preview-hidden-portrait' : '') . (is_array($landscape) && air_screen_value($landscape, 'visible') === false ? ' preview-hidden-landscape' : '');
        $buttons = air_screen_value($section, 'buttons');
        $html .= '<section class="screen-preview-section' . $hidden . '" style="--portrait-width:' . max(1, min(12, (int)$portraitWidth)) . ';--landscape-width:' . max(1, min(12, (int)$landscapeWidth)) . ';--portrait-order:' . (int)$portraitOrder . ';--landscape-order:' . (int)$landscapeOrder . '">';
        if ($title !== '') { $html .= '<strong>' . air_screen_h($title) . '</strong>'; }
        if (str_contains($kind, 'trackpad')) {
            $html .= '<div class="screen-preview-trackpad"><span>Trackpad</span></div>';
        } elseif ($kind === 'volume') {
            $html .= '<div class="screen-preview-volume"><span></span><i></i></div>';
        } elseif ($kind === 'navigationring' || $kind === 'dpad') {
            $html .= '<div class="screen-preview-dpad"><i>&uarr;</i><i>&larr;</i><i>&bull;</i><i>&rarr;</i><i>&darr;</i></div>';
        }
        if (is_array($buttons) && $buttons) {
            $html .= '<div class="screen-preview-buttons">';
            foreach ($buttons as $buttonIndex => $button) {
                if (!is_array($button)) { continue; }
                $buttonLabel = (string)(air_screen_value($button, 'label') ?? 'Button');
                $buttonPortrait = air_screen_value($button, 'portrait');
                $buttonLandscape = air_screen_value($button, 'landscape');
                $buttonPortraitOrder = is_array($buttonPortrait) ? (air_screen_value($buttonPortrait, 'order') ?? $buttonIndex) : $buttonIndex;
                $buttonLandscapeOrder = is_array($buttonLandscape) ? (air_screen_value($buttonLandscape, 'order') ?? $buttonIndex) : $buttonIndex;
                $buttonHidden = (is_array($buttonPortrait) && air_screen_value($buttonPortrait, 'visible') === false ? ' preview-hidden-portrait' : '') . (is_array($buttonLandscape) && air_screen_value($buttonLandscape, 'visible') === false ? ' preview-hidden-landscape' : '');
                $html .= '<span class="' . trim($buttonHidden) . '" style="--portrait-order:' . (int)$buttonPortraitOrder . ';--landscape-order:' . (int)$buttonLandscapeOrder . '">' . air_screen_h($buttonLabel) . '</span>';
            }
            $html .= '</div>';
        }
        $html .= '</section>';
    }
    return $html . '</div></div></div></div></div>';
}

function air_screen_stars(float $rating, int $votes): string
{
    $rounded = max(0, min(5, (int)round($rating)));
    $stars = str_repeat('&#9733;', $rounded) . str_repeat('&#9734;', 5 - $rounded);
    $summary = $votes === 0 ? 'No ratings yet' : number_format($rating, 1) . ' from ' . $votes . ($votes === 1 ? ' vote' : ' votes');
    return '<span class="catalog-rating" aria-label="' . air_screen_h($summary) . '"><span aria-hidden="true">' . $stars . '</span><small>' . air_screen_h($summary) . '</small></span>';
}

function air_screen_validate_package(string $json): array
{
    if (strlen($json) === 0 || strlen($json) > AIR_SCREEN_MAX_BYTES) {
        throw new InvalidArgumentException('The package is empty or too large.');
    }
    try {
        $package = json_decode($json, true, 32, JSON_THROW_ON_ERROR);
    } catch (JsonException) {
        throw new InvalidArgumentException('The package is not valid JSON.');
    }
    air_screen_require_exact_keys($package, ['packageVersion', 'format', 'screen'], ['packageVersion', 'format', 'screen']);
    if ($package['packageVersion'] !== 1 || $package['format'] !== 'voltura-air.custom-screen') {
        throw new InvalidArgumentException('The package version or format is unsupported.');
    }
    $screen = $package['screen'];
    if (!is_array($screen)) { throw new InvalidArgumentException('The package does not contain a valid screen.'); }
    air_screen_require_exact_keys($screen, ['id', 'name', 'revision', 'assignedClientIds', 'orientationLayoutsEnabled', 'showNavigationHeader', 'sections'], ['id', 'name', 'revision', 'assignedClientIds', 'orientationLayoutsEnabled', 'showNavigationHeader', 'sections']);
    if (!air_screen_valid_id($screen['id'] ?? null) ||
        !air_screen_valid_text($screen['name'] ?? null, 24) ||
        !air_screen_valid_id($screen['revision'] ?? null) ||
        !is_bool($screen['orientationLayoutsEnabled'] ?? null) ||
        !is_bool($screen['showNavigationHeader'] ?? null) ||
        !is_array($screen['assignedClientIds']) || count($screen['assignedClientIds']) !== 0) {
        throw new InvalidArgumentException('The package does not contain a valid screen.');
    }
    $sections = $screen['sections'];
    if (!is_array($sections) || count($sections) > 64) {
        throw new InvalidArgumentException('The package contains too many panels.');
    }
    $buttons = 0;
    $sectionIds = [];
    $buttonIds = [];
    foreach ($sections as $section) {
        if (is_array($section)) {
            air_screen_require_exact_keys($section, ['id', 'name', 'showHeader', 'widthColumns', 'heightMode', 'fillWeight', 'rowLimit', 'portrait', 'landscape', 'buttons', 'kind', 'trackpadLeftClick', 'trackpadRightClick', 'trackpadButtonSide', 'initiallyExpanded', 'trackpadFullscreenControl', 'trackpadGyroControl', 'buttonAlignment'], ['id', 'name', 'showHeader', 'widthColumns', 'heightMode', 'fillWeight', 'rowLimit', 'buttons', 'kind', 'trackpadLeftClick', 'trackpadRightClick', 'trackpadButtonSide', 'initiallyExpanded', 'trackpadFullscreenControl', 'buttonAlignment']);
        }
        $sectionButtons = is_array($section) ? ($section['buttons'] ?? null) : null;
        if (!is_array($section) || !is_array($sectionButtons)) {
            throw new InvalidArgumentException('The package contains an invalid panel.');
        }
        if (!air_screen_validate_section($section)) {
            throw new InvalidArgumentException('The package contains an invalid panel.');
        }
        if (isset($sectionIds[$section['id']])) { throw new InvalidArgumentException('The package contains duplicate panel IDs.'); }
        $sectionIds[$section['id']] = true;
        $buttons += count($sectionButtons);
        foreach ($sectionButtons as $button) {
            if (is_array($button)) {
                air_screen_require_exact_keys($button, ['id', 'name', 'label', 'icon', 'presentation', 'size', 'repeat', 'portrait', 'landscape', 'action', 'row'], ['id', 'name', 'label', 'icon', 'presentation', 'size', 'repeat', 'action', 'row']);
            }
            $action = is_array($button) ? ($button['action'] ?? null) : null;
            $kind = is_array($action) ? ($action['kind'] ?? null) : null;
            if (!is_array($button) || !is_array($action) || !in_array($kind, ['text', 'shortcut', 'builtIn', 'urlOpen', 'knownApp', 'hostAction'], true)) {
                throw new InvalidArgumentException('The package contains an unsupported action.');
            }
            if (!air_screen_validate_button($button, $section)) {
                throw new InvalidArgumentException('The package contains an invalid button.');
            }
            if (isset($buttonIds[$button['id']])) { throw new InvalidArgumentException('The package contains duplicate button IDs.'); }
            $buttonIds[$button['id']] = true;
            $actionKeys = match ($kind) {
                'text' => ['kind', 'text'],
                'shortcut' => ['kind', 'key', 'modifiers'],
                'builtIn' => ['kind', 'builtIn'],
                'urlOpen' => ['kind', 'url'],
                'knownApp', 'hostAction' => ['kind', 'actionId'],
            };
            air_screen_require_exact_keys($action, $actionKeys, $actionKeys);
            if (!air_screen_validate_action($action)) {
                throw new InvalidArgumentException('The package contains an invalid action.');
            }
        }
    }
    if ($buttons > 256) {
        throw new InvalidArgumentException('The package contains too many buttons.');
    }
    return [
        'packageVersion' => 1,
        'format' => 'voltura-air.custom-screen',
        'screen' => $screen,
    ];
}

function air_screen_validate_section(array $section): bool
{
    $widths = [3, 4, 6, 8, 9, 12];
    $kind = $section['kind'] ?? null;
    $width = $section['widthColumns'] ?? null;
    if (!air_screen_valid_id($section['id'] ?? null) ||
        !air_screen_valid_text($section['name'] ?? null, 20) ||
        !is_bool($section['showHeader'] ?? null) ||
        !is_int($width) || !in_array($width, $widths, true) ||
        !in_array($section['heightMode'] ?? null, ['content', 'fill'], true) ||
        !is_int($section['fillWeight'] ?? null) || $section['fillWeight'] < 1 || $section['fillWeight'] > 4 ||
        !is_int($section['rowLimit'] ?? null) || $section['rowLimit'] < 0 || $section['rowLimit'] > AIR_SCREEN_MAX_BUTTON_ROWS ||
        !in_array($kind, ['buttons', 'collapsible', 'trackpad', 'collapsibleTrackpad', 'volume', 'navigationRing', 'dpad'], true) ||
        !in_array($section['trackpadButtonSide'] ?? null, ['left', 'right'], true) ||
        !in_array($section['buttonAlignment'] ?? null, ['start', 'center', 'end', 'space-between', 'space-around', 'space-evenly'], true) ||
        !is_bool($section['trackpadLeftClick'] ?? null) || !is_bool($section['trackpadRightClick'] ?? null) ||
        !is_bool($section['initiallyExpanded'] ?? null) || !is_bool($section['trackpadFullscreenControl'] ?? null) ||
        (array_key_exists('trackpadGyroControl', $section) && !is_bool($section['trackpadGyroControl'])) ||
        !air_screen_validate_layout($section['portrait'] ?? null, true) ||
        !air_screen_validate_layout($section['landscape'] ?? null, true)) {
        return false;
    }
    if ($kind === 'volume' && !in_array($width, [3, 6, 9, 12], true)) { return false; }
    if (in_array($kind, ['navigationRing', 'dpad'], true) && !in_array($width, [6, 8, 9, 12], true)) { return false; }
    foreach ([$section['portrait'] ?? null, $section['landscape'] ?? null] as $layout) {
        if (is_array($layout) && isset($layout['widthColumns']) &&
            (($kind === 'volume' && !in_array($layout['widthColumns'], [3, 6, 9, 12], true)) ||
             (in_array($kind, ['navigationRing', 'dpad'], true) && !in_array($layout['widthColumns'], [6, 8, 9, 12], true)))) {
            return false;
        }
    }
    if (!in_array($kind, ['buttons', 'collapsible'], true) && count($section['buttons']) !== 0) { return false; }
    return !in_array($kind, ['collapsible', 'collapsibleTrackpad'], true) || $section['showHeader'] === true;
}

function air_screen_validate_button(array $button, array $section): bool
{
    $row = $button['row'] ?? null;
    $action = $button['action'] ?? null;
    return air_screen_valid_id($button['id'] ?? null) &&
        air_screen_valid_text($button['name'] ?? null, 24) &&
        is_string($button['label'] ?? null) && strlen($button['label']) <= 16 &&
        in_array($button['icon'] ?? null, ['play', 'pause', 'skip-back', 'skip-forward', 'volume-1', 'volume-2', 'volume-x', 'arrow-up', 'arrow-down', 'arrow-left', 'arrow-right', 'corner-down-left', 'escape', 'keyboard', 'clipboard', 'copy', 'app-window', 'monitor', 'minimize', 'square-x', 'search', 'refresh', 'maximize', 'command'], true) &&
        in_array($button['presentation'] ?? null, ['iconLabel', 'icon', 'label'], true) &&
        in_array($button['size'] ?? null, ['compact', 'standard', 'wide', 'fill'], true) &&
        is_bool($button['repeat'] ?? null) && is_int($row) && $row >= 0 && $row <= ($section['rowLimit'] ?? -1) &&
        air_screen_validate_layout($button['portrait'] ?? null, false) &&
        air_screen_validate_layout($button['landscape'] ?? null, false) &&
        is_array($action) &&
        (!in_array($action['kind'] ?? null, ['text', 'shortcut'], true) || $button['presentation'] === 'label');
}

function air_screen_validate_layout(mixed $layout, bool $section): bool
{
    if ($layout === null) { return true; }
    if (!is_array($layout)) { return false; }
    air_screen_require_exact_keys($layout, ['order', 'visible', 'widthColumns', 'size', 'row'], ['order', 'visible']);
    if (!is_int($layout['order']) || $layout['order'] < 0 || !is_bool($layout['visible'])) { return false; }
    if (isset($layout['widthColumns']) && (!$section || !is_int($layout['widthColumns']) || !in_array($layout['widthColumns'], [3, 4, 6, 8, 9, 12], true))) { return false; }
    if (isset($layout['size']) && ($section || !in_array($layout['size'], ['compact', 'standard', 'wide', 'fill'], true))) { return false; }
    return !isset($layout['row']) || (!$section && is_int($layout['row']) && $layout['row'] >= 0 && $layout['row'] <= AIR_SCREEN_MAX_BUTTON_ROWS);
}

function air_screen_validate_action(array $action): bool
{
    $kind = $action['kind'];
    if ($kind === 'text') { return air_screen_valid_text($action['text'], 256); }
    if ($kind === 'shortcut') {
        $modifiers = $action['modifiers'];
        return air_screen_valid_shortcut_key($action['key']) && is_array($modifiers) && count($modifiers) <= 5 &&
            count(array_unique($modifiers, SORT_STRING)) === count($modifiers) &&
            array_diff($modifiers, ['Control', 'Shift', 'Alt', 'AltGr', 'Win']) === [];
    }
    if ($kind === 'builtIn') { return in_array($action['builtIn'], ['media.previous', 'media.playPause', 'media.next', 'media.stop', 'media.seekBack', 'media.seekForward', 'volume.down', 'volume.mute', 'volume.up', 'navigation.up', 'navigation.down', 'navigation.left', 'navigation.right', 'navigation.enter', 'navigation.escape', 'browser.back', 'browser.forward', 'browser.reload', 'browser.fullscreen', 'windows.start', 'windows.previousApp', 'windows.taskView', 'windows.showDesktop', 'windows.minimize', 'windows.maximize', 'windows.snapLeft', 'windows.snapRight', 'windows.explorer', 'windows.run', 'windows.close'], true); }
    if ($kind === 'knownApp') { return in_array($action['actionId'], ['browser', 'spotify', 'vlc', 'zoom', 'plex', 'windowsPhotos', 'blender'], true); }
    if ($kind === 'hostAction') { return in_array($action['actionId'], ['power.lock', 'power.sleep', 'power.hibernate', 'power.restart', 'power.shutdown', 'display.off', 'display.duplicate', 'display.extend', 'display.pcOnly', 'display.secondOnly'], true); }
    if ($kind !== 'urlOpen' || !is_string($action['url']) || strlen(trim($action['url'])) < 1 || strlen(trim($action['url'])) > 2048 || preg_match('/[\x00-\x1F\x7F]/', $action['url'])) { return false; }
    $candidate = preg_match('/^[A-Za-z][A-Za-z0-9+.-]*:/', trim($action['url'])) ? trim($action['url']) : 'https://' . trim($action['url']);
    $parts = parse_url($candidate);
    return is_array($parts) && in_array(strtolower((string)($parts['scheme'] ?? '')), ['http', 'https'], true) && !empty($parts['host']);
}

function air_screen_valid_shortcut_key(mixed $key): bool
{
    if (!is_string($key)) { return false; }
    $key = trim($key);
    if (strlen($key) === 1 && preg_match('/^[A-Za-z0-9]$/D', $key)) { return true; }
    return in_array(strtolower($key), array_map('strtolower', ['BrowserBack', 'BrowserForward', '+', 'MediaStop', 'MediaPlayPause', 'MediaPreviousTrack', 'MediaNextTrack', 'VolumeUp', 'VolumeDown', 'VolumeMute', 'Numpad0', 'Numpad1', 'Numpad2', 'Numpad3', 'Numpad4', 'Numpad5', 'Numpad6', 'Numpad7', 'Numpad8', 'Numpad9', 'NumpadAdd', 'NumpadSubtract', 'NumpadMultiply', 'NumpadDivide', 'NumpadDecimal', '.', ',', ';', '/', '\\', "'", '`', '[', ']', '-', '=', 'Backspace', 'Delete', 'Enter', 'Insert', 'Tab', 'Escape', 'Space', 'PageUp', 'PageDown', 'Home', 'End', 'ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'F1', 'F2', 'F3', 'F4', 'F5', 'F6', 'F7', 'F8', 'F9', 'F10', 'F11', 'F12']), true);
}

function air_screen_valid_id(mixed $value): bool
{
    return is_string($value) && preg_match('/^[A-Za-z0-9._-]{1,64}$/D', $value) === 1;
}

function air_screen_valid_text(mixed $value, int $maximumLength): bool
{
    return is_string($value) && strlen($value) > 0 && strlen($value) <= $maximumLength && trim($value) !== '' && !preg_match('/[\x00-\x1F\x7F]/', $value);
}

function air_screen_require_exact_keys(array $value, array $allowed, array $required): void
{
    if (array_diff(array_keys($value), $allowed) !== [] ||
        array_diff($required, array_keys($value)) !== []) {
        throw new InvalidArgumentException('The package uses an unsupported JSON shape.');
    }
}

function air_screen_uuid(): string
{
    $bytes = random_bytes(16);
    $bytes[6] = chr((ord($bytes[6]) & 0x0f) | 0x40);
    $bytes[8] = chr((ord($bytes[8]) & 0x3f) | 0x80);
    return vsprintf('%s%s-%s-%s-%s-%s%s%s', str_split(bin2hex($bytes), 4));
}

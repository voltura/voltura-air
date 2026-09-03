ALTER TABLE air_screen_packages
    ADD COLUMN IF NOT EXISTS removed_at DATETIME NULL AFTER official_metadata;

UPDATE air_screen_packages
SET removed_at = updated_at
WHERE status = 'removed' AND removed_at IS NULL;

CREATE INDEX IF NOT EXISTS idx_air_screen_owner_capacity
    ON air_screen_packages (owner_id, status, created_at);
CREATE INDEX IF NOT EXISTS idx_air_screen_storage
    ON air_screen_packages (storage_basename);
CREATE INDEX IF NOT EXISTS idx_air_screen_removed
    ON air_screen_packages (status, removed_at);
CREATE INDEX IF NOT EXISTS idx_air_screen_token_expiry
    ON air_screen_verification_tokens (expires_at);
CREATE INDEX IF NOT EXISTS idx_air_screen_rate_window
    ON air_screen_rate_buckets (window_started);
CREATE INDEX IF NOT EXISTS idx_air_screen_report_email_time
    ON air_screen_reports (reporter_email, created_at);
CREATE INDEX IF NOT EXISTS idx_air_screen_report_package_email_time
    ON air_screen_reports (package_id, reporter_email, created_at);
CREATE INDEX IF NOT EXISTS idx_air_screen_report_retention
    ON air_screen_reports (created_at);

CREATE TABLE IF NOT EXISTS air_screen_maintenance (
    singleton_id TINYINT UNSIGNED NOT NULL PRIMARY KEY,
    next_run_at DATETIME NOT NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT IGNORE INTO air_screen_maintenance (singleton_id, next_run_at)
VALUES (1, CURRENT_TIMESTAMP);

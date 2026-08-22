CREATE TABLE IF NOT EXISTS air_telemetry_daily (
    activity_date DATE NOT NULL,
    installation_hash BINARY(32) NOT NULL,
    host_version VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    host_starts SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    connections_standard_local SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    connections_enhanced_direct SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    connections_relay SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_trackpad SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_keyboard SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_dictation SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_media_controls SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_presentation SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_custom_screens SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_files SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_screen_viewing SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_phone_webcam SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    features_gyro_mouse SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    first_received_at DATETIME(6) NOT NULL,
    last_received_at DATETIME(6) NOT NULL,
    PRIMARY KEY (activity_date, installation_hash, host_version),
    KEY air_telemetry_daily_version_date_installation (host_version, activity_date, installation_hash),
    KEY air_telemetry_daily_date_version (activity_date, host_version)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS air_telemetry_batches (
    installation_hash BINARY(32) NOT NULL,
    batch_id BINARY(16) NOT NULL,
    received_at DATETIME(6) NOT NULL,
    PRIMARY KEY (installation_hash, batch_id),
    KEY air_telemetry_batches_received (received_at)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS air_telemetry_rate_buckets (
    bucket_kind ENUM('installation_daily', 'source_hourly') CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
    bucket_hash BINARY(32) NOT NULL,
    window_start DATETIME NOT NULL,
    request_count SMALLINT UNSIGNED NOT NULL DEFAULT 0,
    PRIMARY KEY (bucket_kind, bucket_hash, window_start),
    KEY air_telemetry_rate_window (window_start)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS air_telemetry_ingest_daily (
    activity_date DATE NOT NULL,
    accepted BIGINT UNSIGNED NOT NULL DEFAULT 0,
    duplicate BIGINT UNSIGNED NOT NULL DEFAULT 0,
    invalid BIGINT UNSIGNED NOT NULL DEFAULT 0,
    rate_limited BIGINT UNSIGNED NOT NULL DEFAULT 0,
    server_failed BIGINT UNSIGNED NOT NULL DEFAULT 0,
    last_successful_ingest_at DATETIME(6) NULL,
    PRIMARY KEY (activity_date)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS air_telemetry_maintenance (
    singleton_id TINYINT UNSIGNED NOT NULL,
    next_cleanup_at DATETIME(6) NOT NULL,
    PRIMARY KEY (singleton_id)
) ENGINE=InnoDB;

INSERT INTO air_telemetry_maintenance (singleton_id, next_cleanup_at)
VALUES (1, UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE singleton_id = VALUES(singleton_id);

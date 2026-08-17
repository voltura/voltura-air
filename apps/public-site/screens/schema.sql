CREATE TABLE air_screen_users (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    email VARCHAR(320) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(80) NOT NULL,
    role ENUM('user', 'admin') NOT NULL DEFAULT 'user',
    verified_at DATETIME NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE air_screen_packages (
    id CHAR(36) NOT NULL PRIMARY KEY,
    owner_id BIGINT UNSIGNED NOT NULL,
    name VARCHAR(24) NOT NULL,
    description VARCHAR(1000) NOT NULL,
    tags VARCHAR(500) NOT NULL DEFAULT '',
    package_version INT NOT NULL,
    screen_json JSON NOT NULL,
    storage_basename VARCHAR(80) NOT NULL,
    status ENUM('pending', 'approved', 'rejected', 'hidden', 'removed') NOT NULL DEFAULT 'pending',
    rejection_feedback VARCHAR(1000) NULL,
    downloads INT UNSIGNED NOT NULL DEFAULT 0,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    approved_at DATETIME NULL,
    screen_id VARCHAR(64) NOT NULL,
    official_source VARCHAR(64) NULL,
    official_id VARCHAR(64) NULL,
    is_official BOOLEAN NOT NULL DEFAULT FALSE,
    official_metadata JSON NULL,
    FOREIGN KEY (owner_id) REFERENCES air_screen_users(id),
    INDEX idx_air_screen_search (status, name),
    INDEX idx_air_screen_popularity (status, downloads),
    UNIQUE KEY uq_air_screen_official (official_source, official_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE air_screen_verification_tokens (
    user_id BIGINT UNSIGNED NOT NULL PRIMARY KEY,
    token_hash CHAR(64) NOT NULL UNIQUE,
    expires_at DATETIME NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (user_id) REFERENCES air_screen_users(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE air_screen_rate_buckets (
    scope VARCHAR(40) NOT NULL,
    bucket_key CHAR(64) NOT NULL,
    window_started DATETIME NOT NULL,
    attempts INT UNSIGNED NOT NULL,
    blocked_until DATETIME NULL,
    PRIMARY KEY (scope, bucket_key)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE air_screen_cleanup_jobs (
    storage_basename VARCHAR(80) NOT NULL PRIMARY KEY,
    expected_sha256 CHAR(64) NOT NULL,
    retry_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    attempts INT UNSIGNED NOT NULL DEFAULT 0,
    last_error_code VARCHAR(40) NULL
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE air_screen_reports (
    id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT PRIMARY KEY,
    package_id CHAR(36) NOT NULL,
    reporter_email VARCHAR(320) NOT NULL,
    reason VARCHAR(1000) NOT NULL,
    status ENUM('open', 'resolved') NOT NULL DEFAULT 'open',
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (package_id) REFERENCES air_screen_packages(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE air_screen_ratings (
    package_id CHAR(36) NOT NULL,
    user_id BIGINT UNSIGNED NOT NULL,
    rating TINYINT UNSIGNED NOT NULL,
    created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (package_id, user_id),
    FOREIGN KEY (package_id) REFERENCES air_screen_packages(id) ON DELETE CASCADE,
    FOREIGN KEY (user_id) REFERENCES air_screen_users(id) ON DELETE CASCADE,
    INDEX idx_air_screen_rating (package_id, rating),
    CONSTRAINT chk_air_screen_rating CHECK (rating BETWEEN 1 AND 5)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

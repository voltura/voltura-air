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

ALTER TABLE air_screen_packages
    ADD COLUMN official_id VARCHAR(64) NULL AFTER approved_at,
    ADD COLUMN is_official BOOLEAN NOT NULL DEFAULT FALSE AFTER official_id,
    ADD COLUMN official_metadata JSON NULL AFTER is_official,
    ADD UNIQUE INDEX idx_air_screen_official_id (official_id);

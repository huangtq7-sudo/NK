-- NARAKA P0 identity schema, compatible with MySQL 5.7.26.
-- This migration is intentionally non-destructive and does not remove legacy tables.

CREATE TABLE IF NOT EXISTS schema_migrations (
    version VARCHAR(32) NOT NULL,
    description VARCHAR(255) NOT NULL,
    applied_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (version)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS accounts (
    account_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    username VARCHAR(64) NOT NULL,
    password_hash VARBINARY(128) NOT NULL,
    password_salt VARBINARY(32) NOT NULL,
    password_parameters VARCHAR(128) NOT NULL,
    status TINYINT UNSIGNED NOT NULL DEFAULT 0,
    created_utc DATETIME(6) NOT NULL,
    last_login_utc DATETIME(6) NULL,
    PRIMARY KEY (account_id),
    UNIQUE KEY uq_accounts_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS player_profiles (
    player_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id BIGINT UNSIGNED NOT NULL,
    display_name VARCHAR(64) NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (player_id),
    UNIQUE KEY uq_player_profiles_account (account_id),
    UNIQUE KEY uq_player_profiles_display_name (display_name),
    CONSTRAINT fk_player_profiles_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0001', 'P0 identity and empty lobby profile', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

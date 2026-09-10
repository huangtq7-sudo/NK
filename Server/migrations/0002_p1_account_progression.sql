-- NARAKA P1.0 account progression, compatible with MySQL 5.7.26.
-- Non-destructive: this migration only adds a new table and backfills it. The P0 accounts and
-- player_profiles tables are left exactly as migration 0001 created them, and migration 0001
-- itself is never edited.
--
-- MySQL 5.7 parses but silently ignores CHECK constraints, so non-negative balances are
-- enforced here by UNSIGNED column types and again by the server Model and Application layers.
-- Account level defaults to 1. The >= 1 rule itself is enforced in application code.

CREATE TABLE IF NOT EXISTS account_progression (
    account_id BIGINT UNSIGNED NOT NULL,
    account_level INT UNSIGNED NOT NULL DEFAULT 1,
    copper BIGINT UNSIGNED NOT NULL DEFAULT 0,
    silk BIGINT UNSIGNED NOT NULL DEFAULT 0,
    gold BIGINT UNSIGNED NOT NULL DEFAULT 0,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id),
    CONSTRAINT fk_account_progression_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO account_progression
    (account_id, account_level, copper, silk, gold, created_utc, updated_utc)
SELECT existing.account_id, 1, 0, 0, 0, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
FROM accounts AS existing
LEFT JOIN account_progression AS progression
    ON progression.account_id = existing.account_id
WHERE progression.account_id IS NULL;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0002', 'P1 account progression level and currencies', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

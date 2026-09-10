-- NARAKA P1.5 per-account weapons, compatible with MySQL 5.7.26.
--
-- Additive only: one new table, no ALTER, no rewrite of any existing row.
--
-- Each weapon type exists exactly once per account, which is why the primary key is
-- (account_id, weapon_id) with no quantity column: a weapon is not a stackable warehouse item and
-- never appears in account_inventory. Level, proficiency and kill count belong to that single
-- instance, so upgrading the long sword can never affect the tachi.
--
-- Rows are created by the server when an account is provisioned, using the weapon ids from the
-- generated configuration. No weapon id is hardcoded here, so adding a weapon is a configuration
-- change rather than a migration.

CREATE TABLE IF NOT EXISTS account_weapons (
    account_id BIGINT UNSIGNED NOT NULL,
    weapon_id VARCHAR(64) NOT NULL,
    level INT UNSIGNED NOT NULL,
    proficiency BIGINT UNSIGNED NOT NULL,
    kill_count BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, weapon_id),
    CONSTRAINT fk_account_weapons_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0006', 'P1 unique per account weapons and upgrade progress', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

-- NARAKA P1 account foundation, compatible with MySQL 5.7.26.
--
-- Non-destructive and additive only. Migrations 0001 and 0002 are never edited, no existing row is
-- rewritten, and no ALTER TABLE is used: MySQL 5.7 has no ADD COLUMN IF NOT EXISTS, so a repeated
-- ALTER would fail the second time and break the idempotency the migrator relies on.
--
-- The per account singleton state that P1 needs (avatar, avatar frame, selected hero/weapon/pet,
-- account experience, inventory tier) therefore lives in a new table instead of extending
-- account_progression. account_progression keeps owning the three currency balances.
--
-- No default value here comes from the configuration: SQL cannot read Config/Source. New rows are
-- provisioned by the server in a transaction that uses the generated configuration, so changing the
-- default hero or the starter grant never requires a schema change.
--
-- MySQL 5.7 parses but silently ignores CHECK, so non-negative amounts are guarded by UNSIGNED
-- column types and again by the server Application layer.

CREATE TABLE IF NOT EXISTS account_profile (
    account_id BIGINT UNSIGNED NOT NULL,
    avatar_id VARCHAR(64) NOT NULL,
    avatar_frame_id VARCHAR(64) NOT NULL,
    selected_hero_id VARCHAR(64) NOT NULL,
    selected_weapon_id VARCHAR(64) NOT NULL,
    selected_pet_id VARCHAR(64) NOT NULL,
    account_xp BIGINT UNSIGNED NOT NULL DEFAULT 0,
    inventory_tier INT UNSIGNED NOT NULL DEFAULT 0,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id),
    CONSTRAINT fk_account_profile_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_grants (
    account_id BIGINT UNSIGNED NOT NULL,
    grant_key VARCHAR(64) NOT NULL,
    granted_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, grant_key),
    CONSTRAINT fk_account_grants_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS currency_ledger (
    ledger_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id BIGINT UNSIGNED NOT NULL,
    currency_id VARCHAR(32) NOT NULL,
    delta BIGINT NOT NULL,
    balance_after BIGINT UNSIGNED NOT NULL,
    reason VARCHAR(64) NOT NULL,
    reference_id VARCHAR(64) NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (ledger_id),
    KEY ix_currency_ledger_account (account_id, ledger_id),
    UNIQUE KEY uq_currency_ledger_reference (account_id, currency_id, reason, reference_id),
    CONSTRAINT fk_currency_ledger_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS idempotency_records (
    account_id BIGINT UNSIGNED NOT NULL,
    request_id VARCHAR(64) NOT NULL,
    operation VARCHAR(64) NOT NULL,
    status_code INT NOT NULL,
    response_payload MEDIUMTEXT NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, request_id),
    CONSTRAINT fk_idempotency_records_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0003', 'P1 account profile grants ledger and idempotency', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

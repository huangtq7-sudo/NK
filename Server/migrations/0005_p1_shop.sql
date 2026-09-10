-- NARAKA P1.4 shop purchases, compatible with MySQL 5.7.26.
--
-- Additive only: one new table, no ALTER, no rewrite of any existing row.
--
-- purchased_total is the account's lifetime purchase count for one product. Per-product purchase
-- limits come from the generated configuration, never from this table, so raising a limit is a
-- configuration change rather than a migration.
--
-- Idempotency for a purchase lives on currency_ledger's (account_id, currency_id, reason,
-- reference_id) unique key created by migration 0003: the whole purchase - charge, ledger row,
-- item grant and counter increment - commits in one transaction, so a repeated RequestId cannot
-- charge twice or grant twice.

CREATE TABLE IF NOT EXISTS account_shop_purchases (
    account_id BIGINT UNSIGNED NOT NULL,
    product_id VARCHAR(64) NOT NULL,
    purchased_total BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, product_id),
    CONSTRAINT fk_account_shop_purchases_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0005', 'P1 shop purchase counters', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

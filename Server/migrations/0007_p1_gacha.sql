-- NARAKA P1.6 gacha orders, results and pity, compatible with MySQL 5.7.26.
--
-- Additive only: three new tables, no ALTER, no rewrite of any existing row.
--
-- The order is what makes an interrupted animation safe. The server charges, rolls, grants and
-- persists the result inside one transaction BEFORE the client plays anything, so the client only
-- ever animates a result that already exists in the database. If the player skips the animation,
-- the process crashes or the connection drops, the next login reads back every order whose
-- shown_utc is still NULL and replays it. Nothing is ever rolled twice and nothing is ever lost.
--
-- pity_counter counts pulls since the last PityQuality reward. It lives on the account row rather
-- than being recomputed from history, so a pull only has to read one row. The counter and the
-- results are written in the same transaction, so they cannot disagree.
--
-- The migrator splits this file on the semicolon character, so comment text must never contain
-- one: a stray semicolon inside a comment silently cuts a CREATE TABLE in half.

CREATE TABLE IF NOT EXISTS account_gacha (
    account_id BIGINT UNSIGNED NOT NULL,
    pool_id VARCHAR(64) NOT NULL,
    pity_counter INT UNSIGNED NOT NULL,
    total_pulls BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, pool_id),
    CONSTRAINT fk_account_gacha_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- order_id is the client supplied OrderId. It is the primary key, so a repeated submission of the
-- same order can never create a second charge or a second roll.
CREATE TABLE IF NOT EXISTS gacha_orders (
    account_id BIGINT UNSIGNED NOT NULL,
    order_id VARCHAR(64) NOT NULL,
    pool_id VARCHAR(64) NOT NULL,
    pull_count INT UNSIGNED NOT NULL,
    currency_id VARCHAR(32) NOT NULL,
    price BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    shown_utc DATETIME(6) NULL,
    PRIMARY KEY (account_id, order_id),
    KEY ix_gacha_orders_unshown (account_id, shown_utc),
    CONSTRAINT fk_gacha_orders_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS gacha_order_results (
    account_id BIGINT UNSIGNED NOT NULL,
    order_id VARCHAR(64) NOT NULL,
    sequence INT UNSIGNED NOT NULL,
    reward_id VARCHAR(64) NOT NULL,
    item_id VARCHAR(64) NOT NULL,
    amount INT UNSIGNED NOT NULL,
    quality VARCHAR(16) NOT NULL,
    PRIMARY KEY (account_id, order_id, sequence),
    CONSTRAINT fk_gacha_order_results_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0007', 'P1 gacha orders results and pity', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

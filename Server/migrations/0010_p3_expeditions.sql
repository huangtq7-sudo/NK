-- NARAKA P3 authoritative expeditions, compatible with MySQL 5.7.26.
--
-- Additive only. Migrations 0001-0009 are immutable and this migration does not rewrite existing
-- account data. The nullable active_account_id has a unique key. MySQL permits multiple NULL values
-- in a unique key, so every account can have history but can own at most one active expedition.
--
-- Temporary assets are separate from settlement assets. Death deletes current monster drops while
-- keeping the expedition active. Settlement copies the immutable summary, grants currencies and
-- inventory or mailbox items, then releases active_account_id in one transaction.
--
-- The migrator splits this file on the semicolon character, so comments contain no semicolons.

CREATE TABLE IF NOT EXISTS expeditions (
    expedition_id VARCHAR(64) NOT NULL,
    account_id BIGINT UNSIGNED NOT NULL,
    active_account_id BIGINT UNSIGNED NULL,
    start_request_id VARCHAR(64) NOT NULL,
    entry_map_id VARCHAR(64) NOT NULL,
    status VARCHAR(16) NOT NULL,
    death_count INT UNSIGNED NOT NULL DEFAULT 0,
    started_utc DATETIME(6) NOT NULL,
    settled_utc DATETIME(6) NULL,
    PRIMARY KEY (expedition_id),
    UNIQUE KEY uq_expeditions_start_request (account_id, start_request_id),
    UNIQUE KEY uq_expeditions_one_active (active_account_id),
    KEY ix_expeditions_account_history (account_id, started_utc),
    CONSTRAINT fk_expeditions_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS expedition_assets (
    expedition_id VARCHAR(64) NOT NULL,
    asset_kind VARCHAR(16) NOT NULL,
    asset_id VARCHAR(64) NOT NULL,
    asset_source VARCHAR(32) NOT NULL,
    quantity BIGINT UNSIGNED NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (expedition_id, asset_kind, asset_id, asset_source),
    CONSTRAINT fk_expedition_assets_expedition
        FOREIGN KEY (expedition_id) REFERENCES expeditions (expedition_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS expedition_events (
    expedition_id VARCHAR(64) NOT NULL,
    event_kind VARCHAR(24) NOT NULL,
    event_id VARCHAR(64) NOT NULL,
    result_payload MEDIUMTEXT NOT NULL,
    occurred_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (expedition_id, event_kind, event_id),
    CONSTRAINT fk_expedition_events_expedition
        FOREIGN KEY (expedition_id) REFERENCES expeditions (expedition_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS expedition_settlements (
    expedition_id VARCHAR(64) NOT NULL,
    account_id BIGINT UNSIGNED NOT NULL,
    request_id VARCHAR(64) NOT NULL,
    reason VARCHAR(32) NOT NULL,
    death_count INT UNSIGNED NOT NULL,
    settled_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (expedition_id),
    KEY ix_expedition_settlements_account (account_id, settled_utc),
    CONSTRAINT fk_expedition_settlements_expedition
        FOREIGN KEY (expedition_id) REFERENCES expeditions (expedition_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_expedition_settlements_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS expedition_settlement_assets (
    expedition_id VARCHAR(64) NOT NULL,
    asset_kind VARCHAR(16) NOT NULL,
    asset_id VARCHAR(64) NOT NULL,
    asset_source VARCHAR(32) NOT NULL,
    quantity BIGINT UNSIGNED NOT NULL,
    PRIMARY KEY (expedition_id, asset_kind, asset_id, asset_source),
    CONSTRAINT fk_expedition_settlement_assets_settlement
        FOREIGN KEY (expedition_id) REFERENCES expedition_settlements (expedition_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_mail_items (
    mail_item_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id BIGINT UNSIGNED NOT NULL,
    source_kind VARCHAR(32) NOT NULL,
    source_id VARCHAR(64) NOT NULL,
    item_id VARCHAR(64) NOT NULL,
    quantity BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    claimed_utc DATETIME(6) NULL,
    PRIMARY KEY (mail_item_id),
    UNIQUE KEY uq_account_mail_source_item (account_id, source_kind, source_id, item_id),
    KEY ix_account_mail_unclaimed (account_id, claimed_utc),
    CONSTRAINT fk_account_mail_items_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0010', 'P3 authoritative expeditions settlement and mailbox overflow', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

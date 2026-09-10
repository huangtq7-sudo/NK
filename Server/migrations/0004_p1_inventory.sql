-- NARAKA P1.3 inventory and equipment, compatible with MySQL 5.7.26.
--
-- Additive only: new tables, no ALTER, no rewrite of any existing row. Migrations 0001-0003 are
-- never edited.
--
-- Stacking model: every item that can enter the warehouse is stored as one row per item type with
-- an integer quantity. One item type therefore occupies exactly one slot, and the configured
-- StackLimit caps that slot. Soulstones and armor stack by ItemId like everything else - there are
-- no random affixes and no per-instance rows, so a player's soulstone is interchangeable with any
-- other copy of the same ItemId.
--
-- Weapons are deliberately absent: each weapon type exists once per account with its own level and
-- upgrade progress, so it lives in the account tables rather than in the warehouse.
--
-- Quantities are UNSIGNED so a negative stock cannot be written even by a faulty query, and the
-- server Application layer checks the same invariant before every write.

CREATE TABLE IF NOT EXISTS account_inventory (
    account_id BIGINT UNSIGNED NOT NULL,
    item_id VARCHAR(64) NOT NULL,
    quantity BIGINT UNSIGNED NOT NULL,
    slot_index INT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, item_id),
    KEY ix_account_inventory_slot (account_id, slot_index),
    CONSTRAINT fk_account_inventory_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- Battle load. slot_kind is 'Soulstone' (slot_index 0-5) or 'Armor' (slot_index 0).
-- The unique key on (account_id, slot_kind, item_id) is what enforces "no duplicate soulstone in
-- the battle load" at the storage level, not only in application code.
CREATE TABLE IF NOT EXISTS account_equipment (
    account_id BIGINT UNSIGNED NOT NULL,
    slot_kind VARCHAR(16) NOT NULL,
    slot_index INT UNSIGNED NOT NULL,
    item_id VARCHAR(64) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, slot_kind, slot_index),
    UNIQUE KEY uq_account_equipment_item (account_id, slot_kind, item_id),
    CONSTRAINT fk_account_equipment_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0004', 'P1 stacked inventory and equipment slots', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

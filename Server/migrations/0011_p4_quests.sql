-- NARAKA P4.1 authoritative quests, compatible with MySQL 5.7.26.
--
-- Additive only. Migrations 0001-0010 are immutable and this migration does not rewrite existing
-- account data. Locked and Available are derived from configuration and are never stored. A quest
-- instance row appears when the server accepts the quest and only moves forward through
-- Accepted, InProgress, ReadyToSubmit, RewardPending and Claimed.
--
-- Claiming reuses account_reward_claims with reward_kind Quest as the final duplicate guard, the
-- immutable currency_ledger with reason QuestReward and idempotency_records for RequestId replay.
-- account_xp_ledger records every account experience grant. Account experience only comes from
-- quests and achievements never write it.
--
-- The migrator splits this file on the semicolon character, so comments contain no semicolons.

CREATE TABLE IF NOT EXISTS account_quests (
    account_id BIGINT UNSIGNED NOT NULL,
    quest_instance_key VARCHAR(96) NOT NULL,
    quest_id VARCHAR(64) NOT NULL,
    status VARCHAR(16) NOT NULL,
    accepted_utc DATETIME(6) NOT NULL,
    step_started_utc DATETIME(6) NOT NULL,
    submitted_utc DATETIME(6) NULL,
    claimed_utc DATETIME(6) NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, quest_instance_key),
    KEY ix_account_quests_status (account_id, status),
    CONSTRAINT fk_account_quests_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_quest_objectives (
    account_id BIGINT UNSIGNED NOT NULL,
    quest_instance_key VARCHAR(96) NOT NULL,
    objective_id VARCHAR(64) NOT NULL,
    progress INT UNSIGNED NOT NULL,
    completed_utc DATETIME(6) NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, quest_instance_key, objective_id),
    CONSTRAINT fk_account_quest_objectives_quest
        FOREIGN KEY (account_id, quest_instance_key)
        REFERENCES account_quests (account_id, quest_instance_key)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_xp_ledger (
    ledger_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    account_id BIGINT UNSIGNED NOT NULL,
    source VARCHAR(32) NOT NULL,
    reference_id VARCHAR(96) NOT NULL,
    delta BIGINT UNSIGNED NOT NULL,
    xp_after BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (ledger_id),
    KEY ix_account_xp_ledger_account (account_id, ledger_id),
    UNIQUE KEY uq_account_xp_ledger_reference (account_id, source, reference_id),
    CONSTRAINT fk_account_xp_ledger_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0011', 'P4 authoritative quests and account experience ledger', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

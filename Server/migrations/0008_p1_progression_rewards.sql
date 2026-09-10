-- NARAKA P1.7 sign-in, reward claims, achievements and red dots, compatible with MySQL 5.7.26.
--
-- Additive only: six new tables, no ALTER, no rewrite of any existing row.
--
-- Sign-in uses the server day boundary at 05-00. cycle_start_day is the server day number the
-- current seven day cycle began on, so a cycle is identified without storing seven separate rows.
-- Each claimed day is one row in account_signin_claims, which is what makes "claim once" and
-- "at most one make-up per cycle" enforceable by primary keys rather than by application code
-- remembering to check.
--
-- account_reward_claims is deliberately generic: sign-in milestones, account level rewards and
-- achievement rewards all claim exactly once, so one table with a kind plus a key expresses all
-- three and gives each of them the same idempotency guarantee.
--
-- Red dots store version and seen_version rather than a single boolean. A boolean is overwritten
-- by the next read and loses any content that arrived in between, which is exactly the bug where
-- a player never sees that a second reward became claimable.
--
-- The migrator splits this file on the semicolon character, so comment text must never contain
-- one: a stray semicolon inside a comment silently cuts a CREATE TABLE in half.

CREATE TABLE IF NOT EXISTS account_signin (
    account_id BIGINT UNSIGNED NOT NULL,
    cycle_start_day BIGINT NOT NULL,
    consecutive_days INT UNSIGNED NOT NULL,
    last_claim_day BIGINT NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id),
    CONSTRAINT fk_account_signin_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_signin_claims (
    account_id BIGINT UNSIGNED NOT NULL,
    cycle_start_day BIGINT NOT NULL,
    day_index INT UNSIGNED NOT NULL,
    is_makeup TINYINT UNSIGNED NOT NULL,
    claimed_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, cycle_start_day, day_index),
    CONSTRAINT fk_account_signin_claims_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_reward_claims (
    account_id BIGINT UNSIGNED NOT NULL,
    reward_kind VARCHAR(32) NOT NULL,
    reward_key VARCHAR(96) NOT NULL,
    claimed_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, reward_kind, reward_key),
    CONSTRAINT fk_account_reward_claims_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_achievements (
    account_id BIGINT UNSIGNED NOT NULL,
    achievement_id VARCHAR(64) NOT NULL,
    progress BIGINT UNSIGNED NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, achievement_id),
    CONSTRAINT fk_account_achievements_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

-- achievement_xp is deliberately a separate column in a separate table from account_profile's
-- account_xp. Account level comes only from quests, achievements never touch it.
CREATE TABLE IF NOT EXISTS account_achievement_state (
    account_id BIGINT UNSIGNED NOT NULL,
    achievement_xp BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id),
    CONSTRAINT fk_account_achievement_state_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_reddot (
    account_id BIGINT UNSIGNED NOT NULL,
    node_path VARCHAR(160) NOT NULL,
    version BIGINT UNSIGNED NOT NULL,
    seen_version BIGINT UNSIGNED NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, node_path),
    CONSTRAINT fk_account_reddot_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0008', 'P1 sign-in reward claims achievements and red dots', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

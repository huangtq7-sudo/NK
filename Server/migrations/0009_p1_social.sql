-- NARAKA P1.8 friends, friend requests, blocks and one to one text chat, compatible with MySQL 5.7.26.
--
-- Additive only: six new tables, no ALTER, no rewrite of any existing row.
--
-- Friendship is stored once per ordered pair, twice per relationship. Storing it once with a
-- canonical low-high ordering makes "list my friends" a query that has to check both columns and
-- union them, which is both slower and easy to get wrong. Two rows cost one extra insert inside a
-- transaction that already exists, and make every read a plain equality lookup.
--
-- account_friend_requests holds only pending requests. Accept and reject both delete the row, so
-- the table cannot accumulate a history that the state machine would then have to filter out. The
-- primary key on requester plus target is what makes "send twice" impossible rather than something
-- application code has to remember to check.
--
-- Conversations use a canonical low-high pair so a one to one conversation has exactly one row no
-- matter who opened it first. Read positions are stored per account, so each side tracks its own
-- unread count without either side being able to change the other's.
--
-- The migrator splits this file on the semicolon character, so comment text must never contain
-- one: a stray semicolon inside a comment silently cuts a CREATE TABLE in half.

CREATE TABLE IF NOT EXISTS account_friends (
    account_id BIGINT UNSIGNED NOT NULL,
    friend_account_id BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, friend_account_id),
    CONSTRAINT fk_account_friends_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_account_friends_friend
        FOREIGN KEY (friend_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_friend_requests (
    requester_account_id BIGINT UNSIGNED NOT NULL,
    target_account_id BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (requester_account_id, target_account_id),
    KEY ix_account_friend_requests_target (target_account_id),
    CONSTRAINT fk_account_friend_requests_requester
        FOREIGN KEY (requester_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_account_friend_requests_target
        FOREIGN KEY (target_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS account_blocks (
    account_id BIGINT UNSIGNED NOT NULL,
    blocked_account_id BIGINT UNSIGNED NOT NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (account_id, blocked_account_id),
    CONSTRAINT fk_account_blocks_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_account_blocks_blocked
        FOREIGN KEY (blocked_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS chat_conversations (
    conversation_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    low_account_id BIGINT UNSIGNED NOT NULL,
    high_account_id BIGINT UNSIGNED NOT NULL,
    last_message_utc DATETIME(6) NULL,
    created_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (conversation_id),
    UNIQUE KEY uq_chat_conversations_pair (low_account_id, high_account_id),
    CONSTRAINT fk_chat_conversations_low
        FOREIGN KEY (low_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_chat_conversations_high
        FOREIGN KEY (high_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS chat_messages (
    message_id BIGINT UNSIGNED NOT NULL AUTO_INCREMENT,
    conversation_id BIGINT UNSIGNED NOT NULL,
    sender_account_id BIGINT UNSIGNED NOT NULL,
    body VARCHAR(512) NOT NULL,
    sent_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (message_id),
    KEY ix_chat_messages_conversation (conversation_id, message_id),
    CONSTRAINT fk_chat_messages_conversation
        FOREIGN KEY (conversation_id) REFERENCES chat_conversations (conversation_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_chat_messages_sender
        FOREIGN KEY (sender_account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

CREATE TABLE IF NOT EXISTS chat_read_positions (
    conversation_id BIGINT UNSIGNED NOT NULL,
    account_id BIGINT UNSIGNED NOT NULL,
    last_read_message_id BIGINT UNSIGNED NOT NULL,
    updated_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (conversation_id, account_id),
    CONSTRAINT fk_chat_read_positions_conversation
        FOREIGN KEY (conversation_id) REFERENCES chat_conversations (conversation_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT,
    CONSTRAINT fk_chat_read_positions_account
        FOREIGN KEY (account_id) REFERENCES accounts (account_id)
        ON UPDATE RESTRICT ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_bin;

INSERT INTO schema_migrations (version, description, applied_utc)
VALUES ('0009', 'P1 friends requests blocks and one to one chat', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE description = VALUES(description);

CREATE TABLE IF NOT EXISTS narrative_ledger_events
(
    player_id           text        NOT NULL,
    entry_id            text        NOT NULL,
    schema_version      text        NOT NULL,
    event_type          text        NOT NULL,
    actors              jsonb       NOT NULL,
    severity            smallint    NOT NULL,
    chapter             text        NOT NULL,
    world_clock         bigint      NOT NULL,
    source_node_id      text        NOT NULL,
    fact_refs           jsonb       NOT NULL,
    payload             jsonb       NOT NULL,
    content_fingerprint text        NOT NULL,
    received_at_utc     timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,

    PRIMARY KEY (player_id, entry_id),
    CONSTRAINT ck_ledger_player_id CHECK
        (player_id ~ '^p\.[a-z0-9]+([._-][a-z0-9]+)*$' AND length(player_id) <= 96),
    CONSTRAINT ck_ledger_entry_id CHECK
        (entry_id ~ '^led\.[a-z0-9]+([._-][a-z0-9]+)*$' AND length(entry_id) <= 96),
    CONSTRAINT ck_ledger_schema_version CHECK (schema_version = '1.0.0'),
    CONSTRAINT ck_ledger_event_type CHECK
        (event_type IN
        (
            'debt_incurred', 'debt_repaid', 'secret_exposed', 'promise_made',
            'promise_broken', 'npc_rescued', 'npc_abandoned', 'enemy_spared',
            'item_gained', 'quest_expired', 'trump_card_revealed'
        )),
    CONSTRAINT ck_ledger_actors CHECK
        (jsonb_typeof(actors) = 'array' AND jsonb_array_length(actors) BETWEEN 1 AND 8),
    CONSTRAINT ck_ledger_severity CHECK (severity BETWEEN 1 AND 5),
    CONSTRAINT ck_ledger_chapter CHECK
        (chapter ~ '^[A-Za-z0-9][A-Za-z0-9._:-]*$' AND length(chapter) <= 64),
    CONSTRAINT ck_ledger_world_clock CHECK (world_clock BETWEEN 0 AND 2147483647),
    CONSTRAINT ck_ledger_source_node CHECK
        (source_node_id ~ '^[A-Za-z0-9][A-Za-z0-9._:-]*$' AND length(source_node_id) <= 160),
    CONSTRAINT ck_ledger_fact_refs CHECK
        (jsonb_typeof(fact_refs) = 'array' AND jsonb_array_length(fact_refs) <= 16),
    CONSTRAINT ck_ledger_payload CHECK
        (jsonb_typeof(payload) = 'object' AND octet_length(payload::text) <= 8192),
    CONSTRAINT ck_ledger_fingerprint CHECK
        (content_fingerprint ~ '^sha256:[0-9a-f]{64}$')
);

CREATE INDEX IF NOT EXISTS ix_ledger_player_chapter_clock_entry
    ON narrative_ledger_events (player_id, chapter, world_clock, entry_id);

ALTER TABLE narrative_ledger_events
    ADD COLUMN IF NOT EXISTS ingest_sequence bigint GENERATED ALWAYS AS IDENTITY;

CREATE UNIQUE INDEX IF NOT EXISTS ux_ledger_ingest_sequence
    ON narrative_ledger_events (ingest_sequence);

CREATE INDEX IF NOT EXISTS ix_ledger_player_chapter_ingest
    ON narrative_ledger_events (player_id, chapter, ingest_sequence);

DROP INDEX IF EXISTS ix_ledger_player_chapter_clock_entry;

CREATE TABLE IF NOT EXISTS ledger_player_tombstones
(
    player_hash    text        PRIMARY KEY,
    deleted_at_utc timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_ledger_tombstone_hash CHECK
        (player_hash ~ '^sha256:[0-9a-f]{64}$')
);

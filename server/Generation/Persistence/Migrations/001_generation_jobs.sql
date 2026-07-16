CREATE TABLE generation_jobs
(
    id                   uuid        PRIMARY KEY,
    idempotency_key      text        NOT NULL,
    input_hash           text        NOT NULL,
    status               text        NOT NULL DEFAULT 'created',
    version              bigint      NOT NULL DEFAULT 0,
    attempt_count        integer     NOT NULL DEFAULT 0,
    lease_owner          text        NULL,
    lease_expires_at_utc timestamptz NULL,
    created_at_utc       timestamptz NOT NULL,
    updated_at_utc       timestamptz NOT NULL,

    CONSTRAINT uq_generation_jobs_idempotency_key UNIQUE (idempotency_key),
    CONSTRAINT ck_generation_jobs_idempotency_key_not_blank
        CHECK (length(btrim(idempotency_key)) > 0),
    CONSTRAINT ck_generation_jobs_input_hash_not_blank
        CHECK (length(btrim(input_hash)) > 0),
    CONSTRAINT ck_generation_jobs_status CHECK
    (
        status IN
        (
            'created',
            'queued',
            'generating',
            'moderating',
            'transcoding',
            'ready',
            'failed',
            'expired'
        )
    ),
    CONSTRAINT ck_generation_jobs_version_nonnegative CHECK (version >= 0),
    CONSTRAINT ck_generation_jobs_attempt_count_nonnegative CHECK (attempt_count >= 0),
    CONSTRAINT ck_generation_jobs_lease_pair CHECK
    (
        (lease_owner IS NULL AND lease_expires_at_utc IS NULL)
        OR
        (length(btrim(lease_owner)) > 0 AND lease_expires_at_utc IS NOT NULL)
    )
);

CREATE OR REPLACE FUNCTION enforce_generation_job_initial_status()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF NEW.status <> 'created' THEN
        RAISE EXCEPTION 'Generation jobs must be inserted with created status, not %', NEW.status
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_generation_jobs_initial_status';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_generation_jobs_initial_status
BEFORE INSERT ON generation_jobs
FOR EACH ROW
EXECUTE FUNCTION enforce_generation_job_initial_status();

CREATE INDEX ix_generation_jobs_status_lease_expires_at_utc
    ON generation_jobs (status, lease_expires_at_utc, created_at_utc, id);

CREATE OR REPLACE FUNCTION enforce_generation_job_status_transition()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.status IS NOT DISTINCT FROM NEW.status THEN
        RETURN NEW;
    END IF;

    IF NOT
    (
        (OLD.status = 'created'     AND NEW.status IN ('queued', 'failed', 'expired'))
        OR (OLD.status = 'queued'       AND NEW.status IN ('generating', 'failed', 'expired'))
        OR (OLD.status = 'generating'   AND NEW.status IN ('moderating', 'failed', 'expired'))
        OR (OLD.status = 'moderating'   AND NEW.status IN ('transcoding', 'failed', 'expired'))
        OR (OLD.status = 'transcoding'  AND NEW.status IN ('ready', 'failed', 'expired'))
    ) THEN
        RAISE EXCEPTION 'Illegal generation job status transition: % -> %', OLD.status, NEW.status
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_generation_jobs_legal_status_transition';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_generation_jobs_status_transition
BEFORE UPDATE OF status ON generation_jobs
FOR EACH ROW
EXECUTE FUNCTION enforce_generation_job_status_transition();

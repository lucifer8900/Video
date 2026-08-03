ALTER TABLE generation_jobs
    ADD COLUMN workload_kind text NOT NULL DEFAULT 'media_generation';

ALTER TABLE generation_jobs
    ADD CONSTRAINT ck_generation_jobs_workload_kind CHECK
    (
        workload_kind IN ('media_generation', 'association_decision')
    );

CREATE INDEX ix_generation_jobs_workload_status_lease
    ON generation_jobs (workload_kind, status, lease_expires_at_utc, created_at_utc, id);

CREATE TABLE association_audit_player_tombstones
(
    player_ref_hash text        PRIMARY KEY,
    deleted_at_utc  timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    CONSTRAINT ck_association_audit_tombstone_hash CHECK
        (player_ref_hash ~ '^sha256:[0-9a-f]{64}$')
);

CREATE TABLE generation_job_association_audits
(
    generation_job_id uuid        PRIMARY KEY
        REFERENCES generation_jobs(id) ON DELETE CASCADE,
    schema_version    text        NOT NULL,
    audit_ref         text        NOT NULL UNIQUE,
    audit_fingerprint text       NOT NULL,
    thread_id         text        NOT NULL UNIQUE,
    player_ref_hash   text        NOT NULL,
    chapter_id        text        NOT NULL,
    template_id       text        NOT NULL,
    resolved_params   jsonb       NOT NULL,
    providers         jsonb       NOT NULL,
    guard_results     jsonb       NOT NULL,
    candidate_count   integer     NOT NULL,
    fallback_used     boolean     NOT NULL,
    outcome_reason    text        NULL,
    latency_ms        bigint      NOT NULL,

    CONSTRAINT ck_association_schema_version_not_blank
        CHECK (schema_version = '1.0.0'),
    CONSTRAINT ck_association_audit_ref_not_blank
        CHECK (length(btrim(audit_ref)) > 0),
    CONSTRAINT ck_association_audit_fingerprint CHECK
        (audit_fingerprint ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_association_thread_id_not_blank
        CHECK (length(btrim(thread_id)) > 0),
    CONSTRAINT ck_association_player_ref_hash CHECK
        (player_ref_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_association_chapter_id_not_blank
        CHECK (length(btrim(chapter_id)) > 0),
    CONSTRAINT ck_association_template_id_not_blank
        CHECK (length(btrim(template_id)) > 0),
    CONSTRAINT ck_association_resolved_params_object CHECK
        (jsonb_typeof(resolved_params) = 'object'),
    CONSTRAINT ck_association_providers_array CHECK
        (jsonb_typeof(providers) = 'array' AND jsonb_array_length(providers) BETWEEN 1 AND 2),
    CONSTRAINT ck_association_guard_results_array CHECK
        (jsonb_typeof(guard_results) = 'array' AND jsonb_array_length(guard_results) BETWEEN 1 AND 128),
    CONSTRAINT ck_association_audit_json_size CHECK
    (
        octet_length(resolved_params::text)
        + octet_length(providers::text)
        + octet_length(guard_results::text) <= 65536
    ),
    CONSTRAINT ck_association_candidate_count_nonnegative
        CHECK (candidate_count BETWEEN 0 AND 32),
    CONSTRAINT ck_association_latency_ms_nonnegative
        CHECK (latency_ms BETWEEN 0 AND 600000),
    CONSTRAINT ck_association_fallback_reason_shape CHECK
    (
        (fallback_used AND outcome_reason IS NOT NULL AND length(btrim(outcome_reason)) > 0)
        OR
        (NOT fallback_used AND outcome_reason IS NULL)
    )
);

-- Media jobs retain the original created-only insertion rule. Association
-- decisions are synchronous audit workloads and are inserted in their terminal
-- ready state together with their one-to-one detail row in a single transaction.
CREATE OR REPLACE FUNCTION enforce_generation_job_initial_status()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF
    (
        NEW.workload_kind = 'media_generation'
        AND NEW.status <> 'created'
    )
    OR
    (
        NEW.workload_kind = 'association_decision'
        AND NEW.status <> 'ready'
    )
    THEN
        RAISE EXCEPTION
            'Generation job workload % cannot be inserted with status %',
            NEW.workload_kind,
            NEW.status
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_generation_jobs_initial_status';
    END IF;

    RETURN NEW;
END;
$$;

CREATE OR REPLACE FUNCTION assert_association_generation_audit_shape(p_job_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_workload_kind text;
    v_audit_count   bigint;
BEGIN
    SELECT workload_kind
    INTO v_workload_kind
    FROM generation_jobs
    WHERE id = p_job_id;

    -- A cascading base-row delete can leave a deferred child trigger queued.
    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT count(*)
    INTO v_audit_count
    FROM generation_job_association_audits
    WHERE generation_job_id = p_job_id;

    IF v_workload_kind = 'association_decision' AND v_audit_count <> 1 THEN
        RAISE EXCEPTION 'An association decision job must have exactly one audit detail row.'
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_association_job_has_exactly_one_audit';
    ELSIF v_workload_kind <> 'association_decision' AND v_audit_count <> 0 THEN
        RAISE EXCEPTION 'A non-association job cannot have an association audit detail row.'
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_nonassociation_job_has_no_association_audit';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION enforce_association_generation_audit_shape()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_TABLE_NAME = 'generation_jobs' THEN
        PERFORM assert_association_generation_audit_shape(NEW.id);
    ELSIF TG_OP = 'INSERT' THEN
        PERFORM assert_association_generation_audit_shape(NEW.generation_job_id);
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM assert_association_generation_audit_shape(OLD.generation_job_id);
    ELSE
        PERFORM assert_association_generation_audit_shape(OLD.generation_job_id);
        IF NEW.generation_job_id IS DISTINCT FROM OLD.generation_job_id THEN
            PERFORM assert_association_generation_audit_shape(NEW.generation_job_id);
        END IF;
    END IF;

    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_association_audit_shape_on_job
AFTER INSERT OR UPDATE OF workload_kind ON generation_jobs
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION enforce_association_generation_audit_shape();

CREATE CONSTRAINT TRIGGER trg_association_audit_shape_on_detail
AFTER INSERT OR UPDATE OR DELETE ON generation_job_association_audits
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION enforce_association_generation_audit_shape();

CREATE TABLE generation_batches
(
    id                       uuid        PRIMARY KEY,
    idempotency_key          text        NOT NULL,
    request_fingerprint      text        NOT NULL,
    manifest_id              text        NOT NULL,
    manifest_content_hash    text        NOT NULL,
    target_tier              text        NOT NULL,
    source_preview_batch_id  uuid        NULL
        REFERENCES generation_batches(id) ON DELETE RESTRICT,
    currency                 text        NOT NULL,
    status                   text        NOT NULL,
    item_count               integer     NOT NULL
        CHECK (item_count BETWEEN 1 AND 20),
    run_lease_owner          text        NULL,
    run_lease_expires_at_utc timestamptz NULL,
    run_lease_fencing_token  bigint      NOT NULL DEFAULT 0,
    version                  bigint      NOT NULL DEFAULT 0,
    created_at_utc           timestamptz NOT NULL,
    updated_at_utc           timestamptz NOT NULL,

    CONSTRAINT uq_generation_batches_idempotency_key UNIQUE (idempotency_key),
    CONSTRAINT ck_generation_batches_idempotency_key_not_blank
        CHECK (length(btrim(idempotency_key)) > 0),
    CONSTRAINT ck_generation_batches_request_fingerprint
        CHECK (request_fingerprint ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_generation_batches_manifest_id_not_blank
        CHECK (length(btrim(manifest_id)) > 0),
    CONSTRAINT ck_generation_batches_manifest_content_hash
        CHECK (manifest_content_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_generation_batches_target_tier
        CHECK (target_tier IN ('preview_fast', 'final_quality')),
    CONSTRAINT ck_generation_batches_source_preview_shape CHECK
    (
        (target_tier = 'preview_fast' AND source_preview_batch_id IS NULL)
        OR
        (target_tier = 'final_quality' AND source_preview_batch_id IS NOT NULL)
    ),
    CONSTRAINT ck_generation_batches_currency
        CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_generation_batches_status
        CHECK (status IN ('running', 'awaiting_review', 'reviewed', 'failed')),
    CONSTRAINT ck_generation_batches_version_nonnegative CHECK (version >= 0),
    CONSTRAINT ck_generation_batches_fencing_token_nonnegative
        CHECK (run_lease_fencing_token >= 0),
    CONSTRAINT ck_generation_batches_run_lease_pair CHECK
    (
        (run_lease_owner IS NULL AND run_lease_expires_at_utc IS NULL)
        OR
        (length(btrim(run_lease_owner)) > 0 AND run_lease_expires_at_utc IS NOT NULL)
    )
);

CREATE UNIQUE INDEX uq_generation_batches_one_open_manifest
    ON generation_batches (manifest_id)
    WHERE status IN ('running', 'awaiting_review');

CREATE INDEX ix_generation_batches_run_lease
    ON generation_batches (status, run_lease_expires_at_utc, created_at_utc, id);

CREATE OR REPLACE FUNCTION enforce_generation_batch_promotion_source()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_source_tier     text;
    v_source_status   text;
    v_source_manifest text;
    v_source_hash     text;
BEGIN
    IF NEW.target_tier = 'final_quality' THEN
        SELECT target_tier, status, manifest_id, manifest_content_hash
        INTO v_source_tier, v_source_status, v_source_manifest, v_source_hash
        FROM generation_batches
        WHERE id = NEW.source_preview_batch_id;

        IF NOT FOUND
           OR v_source_tier <> 'preview_fast'
           OR v_source_status <> 'reviewed'
           OR v_source_manifest <> NEW.manifest_id
           OR v_source_hash <> NEW.manifest_content_hash
        THEN
            RAISE EXCEPTION 'Final-quality batch source is not an adopted reviewed preview lane.'
                USING ERRCODE = '23514',
                      CONSTRAINT = 'ck_generation_batch_promotion_source';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_generation_batch_promotion_source
BEFORE INSERT OR UPDATE OF
    target_tier, source_preview_batch_id, manifest_id, manifest_content_hash
ON generation_batches
FOR EACH ROW
EXECUTE FUNCTION enforce_generation_batch_promotion_source();

CREATE TABLE generation_batch_items
(
    id                           bigserial   PRIMARY KEY,
    batch_id                     uuid        NOT NULL
        REFERENCES generation_batches(id) ON DELETE CASCADE,
    item_order                   integer     NOT NULL,
    shot_id                      text        NOT NULL,
    input_hash                   text        NOT NULL,
    requested_tier               text        NOT NULL,
    target_tier                  text        NOT NULL,
    source_preview_batch_id      uuid        NULL,
    source_preview_shot_id       text        NULL,
    target_duration_milliseconds integer     NOT NULL,
    aspect_ratio                 text        NOT NULL,
    maximum_cost_micros          bigint      NOT NULL,
    dialogue_binding             text        NOT NULL,
    first_frame                  jsonb       NOT NULL,
    last_frame                   jsonb       NOT NULL,
    primary_media                jsonb       NOT NULL,
    fallback_media               jsonb       NOT NULL,
    status                       text        NOT NULL,
    failure_code                 text        NULL,

    CONSTRAINT uq_generation_batch_items_batch_shot UNIQUE (batch_id, shot_id),
    CONSTRAINT uq_generation_batch_items_batch_order UNIQUE (batch_id, item_order),
    CONSTRAINT uq_generation_batch_items_single_promotion
        UNIQUE (source_preview_batch_id, source_preview_shot_id),
    CONSTRAINT ck_generation_batch_items_order_nonnegative CHECK (item_order >= 0),
    CONSTRAINT ck_generation_batch_items_shot_id_not_blank
        CHECK (length(btrim(shot_id)) > 0),
    CONSTRAINT ck_generation_batch_items_input_hash
        CHECK (input_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_generation_batch_items_requested_tier
        CHECK (requested_tier IN ('preview_fast', 'final_quality')),
    CONSTRAINT ck_generation_batch_items_target_tier
        CHECK (target_tier IN ('preview_fast', 'final_quality')),
    CONSTRAINT ck_generation_batch_items_promotion_shape CHECK
    (
        (target_tier = 'preview_fast'
            AND source_preview_batch_id IS NULL
            AND source_preview_shot_id IS NULL)
        OR
        (target_tier = 'final_quality'
            AND source_preview_batch_id IS NOT NULL
            AND source_preview_shot_id IS NOT NULL)
    ),
    CONSTRAINT ck_generation_batch_items_duration_positive
        CHECK (target_duration_milliseconds > 0),
    CONSTRAINT ck_generation_batch_items_aspect_ratio_not_blank
        CHECK (length(btrim(aspect_ratio)) > 0),
    CONSTRAINT ck_generation_batch_items_maximum_cost_positive
        CHECK (maximum_cost_micros > 0),
    CONSTRAINT ck_generation_batch_items_dialogue_binding
        CHECK (dialogue_binding = 'bound_to_primary_media'),
    CONSTRAINT ck_generation_batch_items_pin_objects CHECK
    (
        jsonb_typeof(first_frame) = 'object'
        AND COALESCE(btrim(first_frame ->> 'MediaRef'), '') <> ''
        AND COALESCE(btrim(first_frame ->> 'AssetVersion'), '') <> ''
        AND COALESCE(first_frame ->> 'ContentHash', '') ~ '^sha256:[0-9a-f]{64}$'
        AND COALESCE(first_frame ->> 'MediaType', '') = 'image'
        AND COALESCE(first_frame ->> 'Origin', '') = 'approved_frame'
        AND jsonb_typeof(last_frame) = 'object'
        AND COALESCE(btrim(last_frame ->> 'MediaRef'), '') <> ''
        AND COALESCE(btrim(last_frame ->> 'AssetVersion'), '') <> ''
        AND COALESCE(last_frame ->> 'ContentHash', '') ~ '^sha256:[0-9a-f]{64}$'
        AND COALESCE(last_frame ->> 'MediaType', '') = 'image'
        AND COALESCE(last_frame ->> 'Origin', '') = 'approved_frame'
        AND jsonb_typeof(primary_media) = 'object'
        AND COALESCE(btrim(primary_media ->> 'MediaRef'), '') <> ''
        AND COALESCE(btrim(primary_media ->> 'AssetVersion'), '') <> ''
        AND COALESCE(primary_media ->> 'ContentHash', '') ~ '^sha256:[0-9a-f]{64}$'
        AND COALESCE(primary_media ->> 'MediaType', '') IN ('video', 'animation')
        AND jsonb_typeof(fallback_media) = 'object'
        AND COALESCE(btrim(fallback_media ->> 'MediaRef'), '') <> ''
        AND COALESCE(btrim(fallback_media ->> 'AssetVersion'), '') <> ''
        AND COALESCE(fallback_media ->> 'ContentHash', '') ~ '^sha256:[0-9a-f]{64}$'
        AND COALESCE(fallback_media ->> 'MediaType', '') IN ('video', 'animation', 'image')
    ),
    CONSTRAINT ck_generation_batch_items_status CHECK
    (
        status IN ('pending', 'awaiting_review', 'adopted', 'rejected', 'failed')
    ),
    CONSTRAINT ck_generation_batch_items_failure_shape CHECK
    (
        (status = 'failed' AND failure_code IS NOT NULL AND length(btrim(failure_code)) > 0)
        OR
        (status <> 'failed' AND failure_code IS NULL)
    ),
    CONSTRAINT fk_generation_batch_items_source_preview
        FOREIGN KEY (source_preview_batch_id, source_preview_shot_id)
        REFERENCES generation_batch_items(batch_id, shot_id) ON DELETE RESTRICT
);

CREATE TABLE generation_batch_attempts
(
    id                           bigserial   PRIMARY KEY,
    batch_id                     uuid        NOT NULL,
    shot_id                      text        NOT NULL,
    attempt_number               integer     NOT NULL,
    generation_job_id            uuid        NOT NULL
        REFERENCES generation_jobs(id) ON DELETE RESTRICT,
    artifact_id                  text        NOT NULL,
    artifact_media_type          text        NOT NULL,
    artifact_content_hash        text        NOT NULL,
    actual_duration_milliseconds integer     NOT NULL,
    actual_cost_micros           bigint      NOT NULL,
    cost_settled                 boolean     NOT NULL,
    completed_at_utc             timestamptz NOT NULL,

    CONSTRAINT fk_generation_batch_attempts_item
        FOREIGN KEY (batch_id, shot_id)
        REFERENCES generation_batch_items(batch_id, shot_id) ON DELETE CASCADE,
    CONSTRAINT uq_generation_batch_attempt_number
        UNIQUE (batch_id, shot_id, attempt_number),
    CONSTRAINT uq_generation_batch_attempt_review_pin
        UNIQUE (batch_id, shot_id, attempt_number, artifact_content_hash),
    CONSTRAINT uq_generation_batch_attempt_job UNIQUE (generation_job_id),
    CONSTRAINT ck_generation_batch_attempt_number_positive CHECK (attempt_number > 0),
    CONSTRAINT ck_generation_batch_attempt_artifact_id_not_blank
        CHECK (length(btrim(artifact_id)) > 0),
    CONSTRAINT ck_generation_batch_attempt_media_type
        CHECK (artifact_media_type = 'video'),
    CONSTRAINT ck_generation_batch_attempt_content_hash
        CHECK (artifact_content_hash ~ '^sha256:[0-9a-f]{64}$'),
    CONSTRAINT ck_generation_batch_attempt_duration_positive
        CHECK (actual_duration_milliseconds > 0),
    CONSTRAINT ck_generation_batch_attempt_cost_nonnegative CHECK (actual_cost_micros >= 0)
);

CREATE TABLE generation_batch_reviews
(
    id                 bigserial   PRIMARY KEY,
    batch_id           uuid        NOT NULL
        REFERENCES generation_batches(id) ON DELETE CASCADE,
    idempotency_key    text        NOT NULL,
    review_fingerprint text        NOT NULL,
    reviewed_at_utc    timestamptz NOT NULL,

    CONSTRAINT uq_generation_batch_reviews_idempotency_key UNIQUE (idempotency_key),
    CONSTRAINT uq_generation_batch_reviews_id_batch UNIQUE (id, batch_id),
    CONSTRAINT ck_generation_batch_reviews_idempotency_key_not_blank
        CHECK (length(btrim(idempotency_key)) > 0),
    CONSTRAINT ck_generation_batch_reviews_fingerprint
        CHECK (review_fingerprint ~ '^sha256:[0-9a-f]{64}$')
);

CREATE OR REPLACE FUNCTION enforce_generation_batch_promotion_item_source()
RETURNS trigger
LANGUAGE plpgsql
AS $$
DECLARE
    v_source_status text;
    v_source_tier   text;
BEGIN
    IF NEW.target_tier = 'final_quality' THEN
        SELECT status, target_tier
        INTO v_source_status, v_source_tier
        FROM generation_batch_items
        WHERE batch_id = NEW.source_preview_batch_id
          AND shot_id = NEW.source_preview_shot_id;

        IF NOT FOUND OR v_source_status <> 'adopted' OR v_source_tier <> 'preview_fast' THEN
            RAISE EXCEPTION 'Final-quality item source is not an adopted preview item.'
                USING ERRCODE = '23514',
                      CONSTRAINT = 'ck_generation_batch_promotion_item_source';
        END IF;
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_generation_batch_promotion_item_source
BEFORE INSERT OR UPDATE OF
    target_tier, source_preview_batch_id, source_preview_shot_id
ON generation_batch_items
FOR EACH ROW
EXECUTE FUNCTION enforce_generation_batch_promotion_item_source();

CREATE TABLE generation_batch_review_items
(
    review_id bigint NOT NULL,
    batch_id  uuid   NOT NULL,
    shot_id   text   NOT NULL,
    decision  text   NOT NULL,
    expected_attempt_number integer NOT NULL,
    expected_artifact_hash text NOT NULL,

    PRIMARY KEY (review_id, shot_id),
    CONSTRAINT fk_generation_batch_review_items_review
        FOREIGN KEY (review_id, batch_id)
        REFERENCES generation_batch_reviews(id, batch_id) ON DELETE CASCADE,
    CONSTRAINT fk_generation_batch_review_items_item
        FOREIGN KEY (batch_id, shot_id)
        REFERENCES generation_batch_items(batch_id, shot_id) ON DELETE CASCADE,
    CONSTRAINT fk_generation_batch_review_items_attempt
        FOREIGN KEY
            (batch_id, shot_id, expected_attempt_number, expected_artifact_hash)
        REFERENCES generation_batch_attempts
            (batch_id, shot_id, attempt_number, artifact_content_hash)
        ON DELETE RESTRICT,
    CONSTRAINT ck_generation_batch_review_items_decision
        CHECK (decision IN ('adopt', 'reject', 'retry')),
    CONSTRAINT ck_generation_batch_review_items_attempt_positive
        CHECK (expected_attempt_number > 0),
    CONSTRAINT ck_generation_batch_review_items_artifact_hash
        CHECK (expected_artifact_hash ~ '^sha256:[0-9a-f]{64}$')
);

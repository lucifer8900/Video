CREATE TABLE budget_limits
(
    id                   bigserial   PRIMARY KEY,
    scope_type           text        NOT NULL,
    scope_key            text        NOT NULL,
    period_start_utc     timestamptz NOT NULL,
    period_end_utc       timestamptz NOT NULL,
    time_zone_id         text        NULL,
    currency             text        NOT NULL,
    limit_micros         bigint      NOT NULL,
    reserved_micros      bigint      NOT NULL DEFAULT 0,
    consumed_micros      bigint      NOT NULL DEFAULT 0,
    circuit_open         boolean     NOT NULL DEFAULT FALSE,
    version              bigint      NOT NULL DEFAULT 0,

    CONSTRAINT uq_budget_limits_scope_currency
        UNIQUE (scope_type, scope_key, currency),
    CONSTRAINT ck_budget_limits_scope_type CHECK
        (scope_type IN ('account', 'device', 'chapter', 'daily', 'project')),
    CONSTRAINT ck_budget_limits_period CHECK (period_end_utc > period_start_utc),
    CONSTRAINT ck_budget_limits_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_budget_limits_limit_nonnegative CHECK (limit_micros >= 0),
    CONSTRAINT ck_budget_limits_reserved_nonnegative CHECK (reserved_micros >= 0),
    CONSTRAINT ck_budget_limits_consumed_nonnegative CHECK (consumed_micros >= 0),
    CONSTRAINT ck_budget_limits_version_nonnegative CHECK (version >= 0)
);

CREATE TABLE budget_reservations
(
    id                    uuid        PRIMARY KEY,
    idempotency_key       text        NOT NULL,
    request_fingerprint   text        NOT NULL,
    account_scope_key     text        NOT NULL,
    device_scope_key      text        NOT NULL,
    chapter_scope_key     text        NOT NULL,
    daily_scope_key       text        NOT NULL,
    project_scope_key     text        NOT NULL,
    requested_at_utc      timestamptz NOT NULL,
    estimated_micros      bigint      NOT NULL,
    maximum_micros        bigint      NOT NULL,
    actual_micros         bigint      NULL,
    currency              text        NOT NULL,
    price_version         text        NOT NULL,
    provider_key          text        NOT NULL,
    model_snapshot        text        NOT NULL,
    status                text        NOT NULL,
    denied_scope          text        NULL,
    reason_code           text        NULL,
    estimator_exceeded    boolean     NOT NULL DEFAULT FALSE,
    circuit_opened        boolean     NOT NULL DEFAULT FALSE,
    created_at_utc        timestamptz NOT NULL,
    updated_at_utc        timestamptz NOT NULL,

    CONSTRAINT uq_budget_reservations_idempotency UNIQUE (idempotency_key),
    CONSTRAINT ck_budget_reservations_idempotency_not_blank
        CHECK (length(btrim(idempotency_key)) > 0),
    CONSTRAINT ck_budget_reservations_status CHECK
        (status IN
            ('reserved', 'dispatching', 'reconciliation_pending', 'settled', 'released', 'denied')),
    CONSTRAINT ck_budget_reservations_estimated_nonnegative CHECK (estimated_micros >= 0),
    CONSTRAINT ck_budget_reservations_maximum_nonnegative CHECK (maximum_micros >= 0),
    CONSTRAINT ck_budget_reservations_estimate_within_maximum CHECK
        (estimated_micros <= maximum_micros),
    CONSTRAINT ck_budget_reservations_actual_nonnegative CHECK
        (actual_micros IS NULL OR actual_micros >= 0),
    CONSTRAINT ck_budget_reservations_currency CHECK (currency ~ '^[A-Z]{3}$'),
    CONSTRAINT ck_budget_reservations_audit_metadata_not_blank CHECK
    (
        length(btrim(price_version)) > 0
        AND length(btrim(provider_key)) > 0
        AND length(btrim(model_snapshot)) > 0
    ),
    CONSTRAINT ck_budget_reservations_actual_shape CHECK
    (
        (status = 'settled' AND actual_micros IS NOT NULL)
        OR
        (status IN ('released', 'denied') AND actual_micros = 0)
        OR
        (status IN ('reserved', 'dispatching', 'reconciliation_pending') AND actual_micros IS NULL)
    ),
    CONSTRAINT ck_budget_reservations_denied_shape CHECK
    (
        (status = 'denied' AND reason_code IS NOT NULL AND denied_scope IS NOT NULL)
        OR
        (status <> 'denied' AND reason_code IS NULL AND denied_scope IS NULL)
    )
);

CREATE TABLE budget_reservation_scopes
(
    reservation_id  uuid   NOT NULL REFERENCES budget_reservations(id) ON DELETE CASCADE,
    budget_limit_id bigint NOT NULL REFERENCES budget_limits(id),
    reserved_micros bigint NOT NULL,

    PRIMARY KEY (reservation_id, budget_limit_id),
    CONSTRAINT ck_budget_reservation_scopes_reserved_nonnegative CHECK (reserved_micros >= 0)
);

CREATE TABLE budget_events
(
    id                bigserial   PRIMARY KEY,
    event_id          text        NOT NULL,
    reservation_id    uuid        NOT NULL REFERENCES budget_reservations(id) ON DELETE CASCADE,
    event_type        text        NOT NULL,
    actual_micros     bigint      NULL,
    occurred_at_utc   timestamptz NOT NULL,

    CONSTRAINT uq_budget_events_event UNIQUE (event_id),
    CONSTRAINT ck_budget_events_type CHECK
        (event_type IN ('settled', 'released', 'reconciliation_pending')),
    CONSTRAINT ck_budget_events_actual_nonnegative CHECK
        (actual_micros IS NULL OR actual_micros >= 0),
    CONSTRAINT ck_budget_events_actual_shape CHECK
    (
        (event_type = 'settled' AND actual_micros IS NOT NULL)
        OR
        (event_type = 'released' AND actual_micros = 0)
        OR
        (event_type = 'reconciliation_pending' AND actual_micros IS NULL)
    )
);

CREATE INDEX ix_budget_limits_stable_lock
    ON budget_limits (scope_type, scope_key, period_start_utc, currency);

CREATE INDEX ix_budget_reservation_scopes_reservation
    ON budget_reservation_scopes (reservation_id, budget_limit_id);

CREATE OR REPLACE FUNCTION assert_budget_reservation_scope_shape(p_reservation_id uuid)
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_status      text;
    v_scope_count bigint;
    v_kind_count  bigint;
BEGIN
    SELECT reservations.status
    INTO v_status
    FROM budget_reservations AS reservations
    WHERE reservations.id = p_reservation_id;

    -- Cascading deletes may leave a deferred scope trigger after its reservation is gone.
    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT count(*), count(DISTINCT limits.scope_type)
    INTO v_scope_count, v_kind_count
    FROM budget_reservation_scopes AS scopes
    JOIN budget_limits AS limits ON limits.id = scopes.budget_limit_id
    WHERE scopes.reservation_id = p_reservation_id;

    IF v_status = 'denied' THEN
        IF v_scope_count <> 0 THEN
            RAISE EXCEPTION 'A denied budget reservation cannot hold budget scopes.'
                USING ERRCODE = '23514',
                      CONSTRAINT = 'ck_budget_denied_reservation_has_zero_scopes';
        END IF;
    ELSIF v_scope_count <> 5 OR v_kind_count <> 5 THEN
        RAISE EXCEPTION 'An admitted budget reservation must hold all five scope kinds.'
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_budget_reservation_has_exactly_five_scope_kinds';
    END IF;
END;
$$;

CREATE OR REPLACE FUNCTION enforce_budget_reservation_scope_shape()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF TG_TABLE_NAME = 'budget_reservations' THEN
        PERFORM assert_budget_reservation_scope_shape(NEW.id);
    ELSIF TG_OP = 'INSERT' THEN
        PERFORM assert_budget_reservation_scope_shape(NEW.reservation_id);
    ELSIF TG_OP = 'DELETE' THEN
        PERFORM assert_budget_reservation_scope_shape(OLD.reservation_id);
    ELSE
        PERFORM assert_budget_reservation_scope_shape(OLD.reservation_id);
        IF NEW.reservation_id IS DISTINCT FROM OLD.reservation_id THEN
            PERFORM assert_budget_reservation_scope_shape(NEW.reservation_id);
        END IF;
    END IF;

    RETURN NULL;
END;
$$;

CREATE CONSTRAINT TRIGGER trg_budget_reservation_scope_shape_on_reservation
AFTER INSERT OR UPDATE ON budget_reservations
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION enforce_budget_reservation_scope_shape();

CREATE CONSTRAINT TRIGGER trg_budget_reservation_scope_shape_on_scope
AFTER INSERT OR UPDATE OR DELETE ON budget_reservation_scopes
DEFERRABLE INITIALLY DEFERRED
FOR EACH ROW
EXECUTE FUNCTION enforce_budget_reservation_scope_shape();

CREATE OR REPLACE FUNCTION enforce_budget_reservation_status_transition()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
    IF OLD.status IS NOT DISTINCT FROM NEW.status THEN
        RETURN NEW;
    END IF;

    IF NOT
    (
        (OLD.status = 'reserved' AND NEW.status IN
            ('dispatching', 'reconciliation_pending', 'settled', 'released'))
        OR
        (OLD.status = 'dispatching' AND NEW.status IN
            ('reconciliation_pending', 'settled', 'released'))
        OR
        (OLD.status = 'reconciliation_pending' AND NEW.status IN ('settled', 'released'))
    ) THEN
        RAISE EXCEPTION 'Illegal budget reservation status transition: % -> %',
            OLD.status,
            NEW.status
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_budget_reservation_legal_status_transition';
    END IF;

    RETURN NEW;
END;
$$;

CREATE TRIGGER trg_budget_reservation_status_transition
BEFORE UPDATE OF status ON budget_reservations
FOR EACH ROW
EXECUTE FUNCTION enforce_budget_reservation_status_transition();

CREATE FUNCTION lock_generation_budget_scopes(p_limit_ids bigint[])
RETURNS void
LANGUAGE plpgsql
AS $$
DECLARE
    v_count integer;
BEGIN
    SELECT count(*) INTO v_count
    FROM budget_limits
    WHERE id = ANY (p_limit_ids);

    IF v_count <> 5 THEN
        RAISE EXCEPTION 'Exactly five budget scopes are required.'
            USING ERRCODE = '23514',
                  CONSTRAINT = 'ck_budget_reservation_exactly_five_scopes';
    END IF;

    PERFORM id
    FROM budget_limits
    WHERE id = ANY (p_limit_ids)
    ORDER BY CASE scope_type
                 WHEN 'account' THEN 1
                 WHEN 'device' THEN 2
                 WHEN 'chapter' THEN 3
                 WHEN 'daily' THEN 4
                 WHEN 'project' THEN 5
             END,
             scope_key
    FOR UPDATE;
END;
$$;

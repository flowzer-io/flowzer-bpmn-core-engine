-- Dauerhafte KI-Läufe. Der JSON-Rumpf enthält ausschließlich den unveränderlichen
-- Ausführungssnapshot; query- und konkurrenzrelevante Zustände liegen in eigenen Spalten.
-- Secret-Werte und Secret-Referenzen gehören ausdrücklich weder in body noch in Spalten.
CREATE TABLE IF NOT EXISTS {schema}.ai_runs (
    id                       uuid PRIMARY KEY,
    process_instance_id      uuid NOT NULL,
    token_id                 uuid NOT NULL,
    status                   smallint NOT NULL CHECK (status BETWEEN 0 AND 7),
    attempt                  integer NOT NULL CHECK (attempt >= 0),
    maximum_attempts         integer NOT NULL CHECK (maximum_attempts BETWEEN 1 AND 100),
    revision                 bigint NOT NULL CHECK (revision > 0),
    created_at               timestamptz NOT NULL,
    updated_at               timestamptz NOT NULL,
    next_attempt_at          timestamptz NULL,
    lease_owner              text NULL,
    lease_expires_at         timestamptz NULL,
    provider_call_started_at timestamptz NULL,
    output_json              text NULL,
    result_model             text NULL,
    input_tokens             integer NULL CHECK (input_tokens IS NULL OR input_tokens >= 0),
    output_tokens            integer NULL CHECK (output_tokens IS NULL OR output_tokens >= 0),
    total_tokens             integer NULL CHECK (total_tokens IS NULL OR total_tokens >= 0),
    failure_code             text NULL,
    body                     text NOT NULL,
    CONSTRAINT ai_runs_instance_token_unique UNIQUE (process_instance_id, token_id),
    CONSTRAINT ai_runs_lease_complete CHECK (
        (lease_owner IS NULL AND lease_expires_at IS NULL)
        OR (lease_owner IS NOT NULL AND lease_expires_at IS NOT NULL)
    )
);

CREATE INDEX IF NOT EXISTS ai_runs_claim_idx
    ON {schema}.ai_runs (status, next_attempt_at, created_at)
    WHERE status IN (0, 2, 3);

CREATE INDEX IF NOT EXISTS ai_runs_expired_lease_idx
    ON {schema}.ai_runs (lease_expires_at)
    WHERE status IN (1, 4);

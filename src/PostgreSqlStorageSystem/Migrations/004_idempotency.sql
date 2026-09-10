-- Persistente, akteurs- und ressourcengebundene HTTP-Idempotenz. Nur Hashes, keine
-- Clientschlüssel, Principals oder Requestinhalte im Klartext.
CREATE TABLE IF NOT EXISTS {schema}.idempotency_records (
    scope_hash           text PRIMARY KEY,
    request_hash         text NOT NULL,
    operation            text NOT NULL,
    created_at           timestamptz NOT NULL,
    expires_at           timestamptz NOT NULL,
    is_completed         boolean NOT NULL,
    process_instance_id  uuid NULL
);
CREATE INDEX IF NOT EXISTS idempotency_records_expires_at_idx
    ON {schema}.idempotency_records (expires_at);

-- Auftraege fuer externe Worker und deren Webhook-Anmeldungen.
--
-- Wie bei den uebrigen Tabellen steht der vollstaendige Datensatz als JSON-Text in `body`;
-- ausgelagert sind nur die Spalten, nach denen abgefragt wird.

CREATE TABLE IF NOT EXISTS {schema}.service_task_jobs (
    id                   uuid PRIMARY KEY,
    type                 text NOT NULL,
    process_instance_id  uuid NOT NULL,
    -- Vergabe: Ein Auftrag ist frei, wenn keine Sperre mehr laeuft und keine Wartezeit ansteht.
    locked_until         timestamptz NULL,
    retry_at             timestamptz NULL,
    retries              integer NOT NULL,
    body                 text NOT NULL
);
CREATE INDEX IF NOT EXISTS service_task_jobs_type_idx ON {schema}.service_task_jobs (type);
CREATE INDEX IF NOT EXISTS service_task_jobs_instance_idx ON {schema}.service_task_jobs (process_instance_id);

CREATE TABLE IF NOT EXISTS {schema}.service_task_webhooks (
    id    uuid PRIMARY KEY,
    type  text NOT NULL,
    body  text NOT NULL
);
CREATE INDEX IF NOT EXISTS service_task_webhooks_type_idx ON {schema}.service_task_webhooks (type);

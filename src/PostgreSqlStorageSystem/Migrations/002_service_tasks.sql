-- Auftraege fuer externe Worker und deren Webhook-Anmeldungen.
--
-- Der unveraenderliche Teil des Auftrags steht als JSON-Text in `body`. Der Vergabezustand
-- (Sperre, Versuche, Wartezeit, letzter Fehler) liegt bewusst ausserhalb davon in eigenen
-- Spalten: Nur so laesst sich ein Auftrag in einem einzigen Statement uebernehmen. Eine
-- Vergabe aus Lesen und anschliessendem Schreiben wuerde zwei Aufrufern denselben Auftrag
-- geben, und ein Service-Task mit Seiteneffekt liefe doppelt.

CREATE TABLE IF NOT EXISTS {schema}.service_task_jobs (
    id                   uuid PRIMARY KEY,
    type                 text NOT NULL,
    process_instance_id  uuid NOT NULL,
    token_id             uuid NOT NULL,
    created_at           timestamptz NOT NULL,
    locked_until         timestamptz NULL,
    locked_by            text NULL,
    retry_at             timestamptz NULL,
    retries              integer NOT NULL,
    last_error           text NULL,
    body                 text NOT NULL
);

-- Ein Token wartet einmal; zwei Auftraege dafuer waeren zwei Ausfuehrungen desselben Schritts.
CREATE UNIQUE INDEX IF NOT EXISTS service_task_jobs_token_uidx ON {schema}.service_task_jobs (token_id);
-- Deckt die Suche nach freien Auftraegen eines Typs ab.
CREATE INDEX IF NOT EXISTS service_task_jobs_available_idx
    ON {schema}.service_task_jobs (type, locked_until, retry_at, created_at);
CREATE INDEX IF NOT EXISTS service_task_jobs_instance_idx ON {schema}.service_task_jobs (process_instance_id);

CREATE TABLE IF NOT EXISTS {schema}.service_task_webhooks (
    id    uuid PRIMARY KEY,
    type  text NOT NULL,
    body  text NOT NULL
);
CREATE INDEX IF NOT EXISTS service_task_webhooks_type_idx ON {schema}.service_task_webhooks (type);

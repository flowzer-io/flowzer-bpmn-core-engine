-- Flowzer: Dokumentenablage in PostgreSQL.
-- Jede Tabelle traegt die fuer Abfragen noetigen Schluesselspalten und den vollstaendigen
-- Datensatz als JSON-Text (`body`), serialisiert wie in der Dateiablage. `text` statt `jsonb`,
-- weil Newtonsoft die Typinformation `$type` als erste Eigenschaft erwartet und jsonb die
-- Reihenfolge der Schluessel nicht erhaelt.

CREATE TABLE IF NOT EXISTS {schema}.definitions (
    id              uuid PRIMARY KEY,
    definition_id   text NOT NULL,
    is_active       boolean NOT NULL,
    version_major   integer NOT NULL,
    version_minor   integer NOT NULL,
    saved_on        timestamptz NOT NULL,
    body            text NOT NULL
);
CREATE INDEX IF NOT EXISTS definitions_definition_id_idx ON {schema}.definitions (definition_id);
-- Zwei parallele Uploads duerfen nicht dieselbe Versionsnummer erhalten; die Anwendung
-- meldet den Verstoss als Konflikt (409), statt stillschweigend zwei "1.0" zu fuehren.
CREATE UNIQUE INDEX IF NOT EXISTS definitions_definition_version_uidx ON {schema}.definitions (definition_id, version_major, version_minor);

CREATE TABLE IF NOT EXISTS {schema}.definition_binaries (
    id   uuid PRIMARY KEY,
    xml  text NOT NULL
);

CREATE TABLE IF NOT EXISTS {schema}.meta_definitions (
    definition_id  text PRIMARY KEY,
    body           text NOT NULL
);

CREATE TABLE IF NOT EXISTS {schema}.instances (
    instance_id         uuid PRIMARY KEY,
    meta_definition_id  text NOT NULL,
    is_finished         boolean NOT NULL,
    body                text NOT NULL
);
CREATE INDEX IF NOT EXISTS instances_is_finished_idx ON {schema}.instances (is_finished);

CREATE TABLE IF NOT EXISTS {schema}.message_subscriptions (
    id                     uuid PRIMARY KEY,
    related_definition_id  text NOT NULL,
    process_instance_id    uuid NULL,
    message_name           text NOT NULL,
    correlation_key        text NULL,
    body                   text NOT NULL
);
CREATE INDEX IF NOT EXISTS message_subscriptions_instance_idx ON {schema}.message_subscriptions (process_instance_id);
CREATE INDEX IF NOT EXISTS message_subscriptions_name_idx ON {schema}.message_subscriptions (message_name);

CREATE TABLE IF NOT EXISTS {schema}.signal_subscriptions (
    id                     uuid PRIMARY KEY,
    related_definition_id  text NOT NULL,
    process_instance_id    uuid NULL,
    signal_name            text NOT NULL,
    body                   text NOT NULL
);
CREATE INDEX IF NOT EXISTS signal_subscriptions_instance_idx ON {schema}.signal_subscriptions (process_instance_id);

CREATE TABLE IF NOT EXISTS {schema}.user_task_subscriptions (
    id                     uuid PRIMARY KEY,
    related_definition_id  text NOT NULL,
    process_instance_id    uuid NULL,
    body                   text NOT NULL
);
CREATE INDEX IF NOT EXISTS user_task_subscriptions_instance_idx ON {schema}.user_task_subscriptions (process_instance_id);

CREATE TABLE IF NOT EXISTS {schema}.timer_subscriptions (
    id                     uuid PRIMARY KEY,
    related_definition_id  text NOT NULL,
    process_instance_id    uuid NULL,
    due_at                 timestamptz NOT NULL,
    body                   text NOT NULL
);
CREATE INDEX IF NOT EXISTS timer_subscriptions_due_at_idx ON {schema}.timer_subscriptions (due_at);

CREATE TABLE IF NOT EXISTS {schema}.forms (
    id             uuid PRIMARY KEY,
    form_id        uuid NOT NULL,
    version_major  integer NOT NULL,
    version_minor  integer NOT NULL,
    body           text NOT NULL
);
CREATE INDEX IF NOT EXISTS forms_form_id_idx ON {schema}.forms (form_id);
CREATE UNIQUE INDEX IF NOT EXISTS forms_form_version_uidx ON {schema}.forms (form_id, version_major, version_minor);

CREATE TABLE IF NOT EXISTS {schema}.form_metadata (
    form_id  uuid PRIMARY KEY,
    body     text NOT NULL
);

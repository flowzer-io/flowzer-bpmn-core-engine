-- Datensparsame, append-only Engine-Ereignisse für die Laufzeitdiagramm-Projektion. Es gibt
-- absichtlich keinen Fremdschlüssel auf instances: Die Spur muss auch dann lesbar bleiben,
-- wenn ein späterer administrativer Bereinigungspfad die laufende Instanz entfernt.
CREATE TABLE IF NOT EXISTS {schema}.runtime_node_events (
    id                  uuid PRIMARY KEY,
    process_instance_id uuid NOT NULL,
    definition_id       uuid NOT NULL,
    token_id            uuid NOT NULL,
    flow_node_id        text NOT NULL,
    node_state          smallint NOT NULL,
    correlation_id      uuid NOT NULL,
    occurred_at         timestamptz NOT NULL,
    body                text NOT NULL
);

CREATE INDEX IF NOT EXISTS runtime_node_events_instance_time_idx
    ON {schema}.runtime_node_events (process_instance_id, occurred_at, token_id, id);

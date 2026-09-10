-- Unveraenderliche, nicht geheime KI-Verbindungsrevisionen fuer deployte Workflows.
-- Bestehende Installationen starten mit der zum Upgradezeitpunkt aktuellen Revision.
CREATE TABLE IF NOT EXISTS {schema}.ai_connection_revisions (
    id       uuid   NOT NULL,
    revision bigint NOT NULL CHECK (revision > 0),
    body     text   NOT NULL,
    PRIMARY KEY (id, revision)
);

INSERT INTO {schema}.ai_connection_revisions (id, revision, body)
SELECT id, revision, body
FROM {schema}.ai_connections
ON CONFLICT (id, revision) DO NOTHING;

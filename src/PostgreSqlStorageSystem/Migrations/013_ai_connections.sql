-- Nicht geheime KI-Verbindungsmetadaten. `secret_reference` ist nur ein opaker Verweis auf
-- den serverseitigen Secret-Store; geheime Werte duerfen diese Tabelle nie erreichen.
CREATE TABLE IF NOT EXISTS {schema}.ai_connections (
    id               uuid PRIMARY KEY,
    name             text NOT NULL,
    revision         bigint NOT NULL CHECK (revision > 0),
    updated_at       timestamptz NOT NULL,
    secret_reference text NOT NULL,
    body             text NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ai_connections_name_unique_idx
    ON {schema}.ai_connections (lower(name));

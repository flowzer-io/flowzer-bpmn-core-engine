-- Additive leere Tabelle: keine Aufgabe/Instanz, kein Secret/Token/Formularinhalt.
CREATE TABLE {schema}.start_form_embed_grants (
    secret_hash text PRIMARY KEY CHECK (length(secret_hash) = 64),
    owner_key text NOT NULL CHECK (length(owner_key) = 64),
    definition_id uuid NOT NULL,
    expires_at timestamptz NOT NULL,
    body text NOT NULL
);
CREATE INDEX start_form_embed_grants_expiry_idx ON {schema}.start_form_embed_grants (expires_at);
CREATE INDEX start_form_embed_grants_owner_version_idx ON {schema}.start_form_embed_grants (owner_key, definition_id);

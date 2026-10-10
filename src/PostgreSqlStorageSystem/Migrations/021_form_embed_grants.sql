-- Ausschließlich kurzlebige Anzeige-Einstiege: kein Secret, kein Token und kein Formularinhalt.
-- Aufgabenabbruch/Abschluss entfernt die Freigaben in derselben Transaktion.
CREATE TABLE {schema}.form_embed_grants (
    secret_hash text PRIMARY KEY CHECK (length(secret_hash) = 64),
    user_task_id uuid NOT NULL REFERENCES {schema}.user_task_subscriptions (id) ON DELETE CASCADE,
    expires_at timestamptz NOT NULL,
    body text NOT NULL
);
CREATE INDEX form_embed_grants_expiry_idx ON {schema}.form_embed_grants (expires_at);
CREATE INDEX form_embed_grants_task_idx ON {schema}.form_embed_grants (user_task_id);

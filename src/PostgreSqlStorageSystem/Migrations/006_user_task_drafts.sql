-- Private Bearbeitungsstaende offener User-Tasks. Der Fremdschluessel sorgt dafuer,
-- dass Abschluss, Abbruch und sonstige Subscription-Bereinigung keine Entwuerfe hinterlassen.
CREATE TABLE IF NOT EXISTS {schema}.user_task_drafts (
    user_task_id        uuid NOT NULL REFERENCES {schema}.user_task_subscriptions (id) ON DELETE CASCADE,
    owner_key           text NOT NULL,
    owner_user_id       uuid NOT NULL,
    token_id            uuid NOT NULL,
    process_instance_id uuid NOT NULL,
    definition_id       uuid NOT NULL,
    revision            bigint NOT NULL CHECK (revision > 0),
    updated_at          timestamptz NOT NULL,
    body                text NOT NULL,
    PRIMARY KEY (user_task_id, owner_key),
    CHECK (length(owner_key) = 64)
);

CREATE INDEX IF NOT EXISTS user_task_drafts_updated_idx
    ON {schema}.user_task_drafts (updated_at);

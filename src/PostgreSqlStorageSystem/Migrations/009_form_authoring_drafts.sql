-- Gemeinsamer Autorenentwurf je Katalogformular. Der Entwurf ist bewusst nicht Teil der
-- veroeffentlichten, unveraenderlichen Versionshistorie.
CREATE TABLE IF NOT EXISTS {schema}.form_authoring_drafts (
    form_id     uuid PRIMARY KEY REFERENCES {schema}.form_metadata (form_id) ON DELETE CASCADE,
    revision    bigint NOT NULL CHECK (revision > 0),
    updated_at  timestamptz NOT NULL,
    body        text NOT NULL
);

CREATE INDEX IF NOT EXISTS form_authoring_drafts_updated_idx
    ON {schema}.form_authoring_drafts (updated_at);

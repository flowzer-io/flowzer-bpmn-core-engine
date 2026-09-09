-- Wiederverwendbare Formularabschnitte. Katalogeintrag, unveränderliche Fassungen und
-- Autorenentwurf sind absichtlich getrennt: Nur der Entwurf ist revisionsgeschützt änderbar.
-- Die FK-Kette verhindert verwaiste Fassungen; ein DELETE-Weg für veröffentlichte Fassungen
-- wird bewusst nicht angeboten, damit die Versionshistorie append-only bleibt.
CREATE TABLE IF NOT EXISTS {schema}.form_section_metadata (
    section_id uuid PRIMARY KEY,
    name       text NOT NULL,
    body       text NOT NULL
);

CREATE TABLE IF NOT EXISTS {schema}.form_section_versions (
    id            uuid PRIMARY KEY,
    section_id    uuid NOT NULL REFERENCES {schema}.form_section_metadata (section_id) ON DELETE RESTRICT,
    version_major integer NOT NULL,
    version_minor integer NOT NULL,
    body          text NOT NULL,
    UNIQUE (section_id, version_major, version_minor)
);
CREATE INDEX IF NOT EXISTS form_section_versions_section_idx
    ON {schema}.form_section_versions (section_id, version_major, version_minor);

CREATE TABLE IF NOT EXISTS {schema}.form_section_authoring_drafts (
    section_id uuid PRIMARY KEY REFERENCES {schema}.form_section_metadata (section_id) ON DELETE CASCADE,
    revision   bigint NOT NULL CHECK (revision > 0),
    updated_at timestamptz NOT NULL,
    body       text NOT NULL
);
CREATE INDEX IF NOT EXISTS form_section_authoring_drafts_updated_idx
    ON {schema}.form_section_authoring_drafts (updated_at);

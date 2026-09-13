-- Gemeinsame Formularbibliothek: hierarchische Ordner strukturieren nur den Katalog.
CREATE TABLE IF NOT EXISTS {schema}.form_folders (
    id         uuid PRIMARY KEY,
    parent_id  uuid NULL REFERENCES {schema}.form_folders (id) ON DELETE RESTRICT,
    name       text NOT NULL,
    body       text NOT NULL
);
CREATE INDEX IF NOT EXISTS form_folders_parent_idx ON {schema}.form_folders (parent_id);

-- Vorwaertsmigration der bisherigen Abschnittsbibliothek. IDs und Versionen bleiben stabil,
-- die alten Tabellen bleiben fuer bereits gespeicherte flowzerSection-Referenzen lesbar.
-- Neue Oberflaechen und neue Referenzen arbeiten danach ausschliesslich mit Formularen.
CREATE TEMP TABLE flowzer_migrated_form_sections (
    section_id uuid PRIMARY KEY
) ON COMMIT DROP;

WITH migrated AS (
    INSERT INTO {schema}.form_metadata (form_id, body)
    SELECT section_id,
           json_build_object('FormId', section_id, 'Name', name, 'FolderId', NULL)::text
    FROM {schema}.form_section_metadata
    ON CONFLICT (form_id) DO NOTHING
    RETURNING form_id
)
INSERT INTO flowzer_migrated_form_sections (section_id)
SELECT form_id FROM migrated;

INSERT INTO {schema}.forms (id, form_id, version_major, version_minor, body)
SELECT legacy.id,
       legacy.section_id,
       legacy.version_major,
       legacy.version_minor,
       json_build_object(
           'Id', legacy.id,
           'FormId', legacy.section_id,
           'Version', json_build_object('Major', legacy.version_major, 'Minor', legacy.version_minor),
           'FormData', legacy.body::json ->> 'SectionData')::text
FROM {schema}.form_section_versions AS legacy
JOIN flowzer_migrated_form_sections AS migrated ON migrated.section_id = legacy.section_id
ON CONFLICT DO NOTHING;

INSERT INTO {schema}.form_authoring_drafts (form_id, revision, updated_at, body)
SELECT legacy.section_id,
       legacy.revision,
       legacy.updated_at,
       json_build_object(
           'FormId', legacy.section_id,
           'Revision', legacy.revision,
           'UpdatedByUserId', legacy.body::json ->> 'UpdatedByUserId',
           'UpdatedAtUtc', legacy.body::json ->> 'UpdatedAtUtc',
           'BasedOnPublishedFormId', legacy.body::json ->> 'BasedOnPublishedSectionId',
           'BasedOnVersion', legacy.body::json -> 'BasedOnVersion',
           'FormData', legacy.body::json ->> 'SectionData')::text
FROM {schema}.form_section_authoring_drafts AS legacy
JOIN flowzer_migrated_form_sections AS migrated ON migrated.section_id = legacy.section_id
ON CONFLICT (form_id) DO NOTHING;

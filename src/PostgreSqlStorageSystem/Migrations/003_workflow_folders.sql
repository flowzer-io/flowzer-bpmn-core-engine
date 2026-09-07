-- Ordner des Workflow-Katalogs.
--
-- Wie ueberall in dieser Ablage steht der vollstaendige Datensatz als JSON-Text in `body`; nur
-- die Spalten, nach denen wirklich gefragt wird, liegen daneben. `parent_id` ist eine echte
-- Fremdschluesselspalte auf dieselbe Tabelle: Ein Ordner, dessen Elternordner nicht existiert,
-- waere im Baum unerreichbar und in der Oberflaeche unsichtbar.
--
-- ON DELETE RESTRICT und nicht CASCADE: Das Loeschen eines Ordners mit Inhalt ist eine Frage an
-- die Person, nicht etwas, das die Datenbank still miterledigt — an den Unterordnern haengen
-- Workflows, die die Datenbank hier gar nicht sieht.

CREATE TABLE IF NOT EXISTS {schema}.workflow_folders (
    id         uuid PRIMARY KEY,
    parent_id  uuid NULL REFERENCES {schema}.workflow_folders (id) ON DELETE RESTRICT,
    name       text NOT NULL,
    body       text NOT NULL
);
CREATE INDEX IF NOT EXISTS workflow_folders_parent_idx ON {schema}.workflow_folders (parent_id);

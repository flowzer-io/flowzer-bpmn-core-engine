-- Versionierte DMN-Entscheidungsdateien. Der Katalogkopf traegt die Kennung, unter der eine
-- Datei angesprochen wird; die Staende haengen unveraenderlich daran. Deployt ist immer der
-- juengste Stand, Entwuerfe gibt es in dieser Stufe nicht.
--
-- `body text` als JSON wie im uebrigen Bestand: Newtonsoft erwartet die Typinformation als
-- erste Eigenschaft, und jsonb erhaelt die Schluesselreihenfolge nicht.
CREATE TABLE IF NOT EXISTS {schema}.decision_definitions (
    decision_definition_id  text PRIMARY KEY,
    name                    text NOT NULL,
    body                    text NOT NULL
);

-- Der Fremdschluessel steht bewusst auf RESTRICT: Ein Katalogeintrag wird gemeinsam mit
-- seinen Staenden geloescht, und zwar ausdruecklich in derselben Transaktion. Ein stilles
-- CASCADE loeschte auch dort mit, wo nur der Kopf gemeint war.
CREATE TABLE IF NOT EXISTS {schema}.decision_definition_versions (
    decision_definition_id  text NOT NULL
        REFERENCES {schema}.decision_definitions (decision_definition_id) ON DELETE RESTRICT,
    version                 integer NOT NULL CHECK (version > 0),
    deployed_at             timestamptz NOT NULL,
    body                    text NOT NULL,
    -- Zwei gleichzeitige Uploads duerfen nicht dieselbe Versionsnummer erhalten; die
    -- Anwendung meldet den Verstoss als Konflikt (409), statt zweimal "3" zu fuehren.
    PRIMARY KEY (decision_definition_id, version)
);

-- Der haeufigste Zugriff ist "der juengste Stand dieser Datei".
CREATE INDEX IF NOT EXISTS decision_definition_versions_latest_idx
    ON {schema}.decision_definition_versions (decision_definition_id, version DESC);

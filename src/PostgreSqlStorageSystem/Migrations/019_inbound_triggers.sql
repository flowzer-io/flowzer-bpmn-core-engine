-- Von aussen aufrufbare Ausloeser.
--
-- Die verwalteten Angaben (Name, Art, Ziel, Variablenmodus, abgeleitetes Geheimnis) stehen als
-- JSON-Text in `body`. Zaehlerstand und letzter Fehler liegen bewusst ausserhalb davon in
-- eigenen Spalten: Sie werden bei jedem Aufruf geschrieben, waehrend die Verwaltung gleichzeitig
-- den Namen aendern kann. Ein Lesen-Aendern-Schreiben ueber den ganzen Koerper wuerde die eine
-- Aenderung mit der anderen ueberschreiben, und zwei gleichzeitige Aufrufe zaehlten nur einmal.
--
-- Das Geheimnis selbst erreicht diese Tabelle nie; `body` traegt nur seine Ableitung.

CREATE TABLE IF NOT EXISTS {schema}.inbound_triggers (
    id                  uuid PRIMARY KEY,
    trigger_key         text NOT NULL,
    enabled             boolean NOT NULL,
    created_at          timestamptz NOT NULL,
    last_used_at        timestamptz NULL,
    use_count           bigint NOT NULL DEFAULT 0,
    last_failure_at     timestamptz NULL,
    last_failure_reason text NULL,
    body                text NOT NULL
);

-- Die Adresse muss eindeutig sein: Zwei Ausloeser unter demselben Schluessel liessen nicht mehr
-- entscheiden, welcher Workflow gemeint ist.
CREATE UNIQUE INDEX IF NOT EXISTS inbound_triggers_key_unique_idx
    ON {schema}.inbound_triggers (trigger_key);

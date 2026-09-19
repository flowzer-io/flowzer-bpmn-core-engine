-- Auswertungen fragen die Ereignisspur nicht je Instanz, sondern je gebundener Version und
-- Zeitraum ab. Der vorhandene Index aus Migration 012 beginnt mit process_instance_id und
-- traegt diese Abfrage nicht; ohne einen eigenen Index bliebe nur der sequentielle Scan.
--
-- Rein additiv: Die Tabelle, ihre Spalten und der bestehende Index bleiben unveraendert.
CREATE INDEX IF NOT EXISTS runtime_node_events_definition_time_idx
    ON {schema}.runtime_node_events (definition_id, occurred_at, token_id, id);

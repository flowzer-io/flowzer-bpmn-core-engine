-- Indexierte Vorgangszuordnung der bereits append-only gespeicherten Human-Task-Auditspur.
-- Der fehlende Fremdschluessel ist beabsichtigt: Die Ereignisse müssen nach dem Ende der
-- Subscription und auch nach einer administrativen Instanzbereinigung erhalten bleiben.
ALTER TABLE {schema}.user_task_assignment_events
    ADD COLUMN IF NOT EXISTS process_instance_id uuid NULL;

-- Die Bodies stammen aus der kontrollierten Newtonsoft-Serialisierung. Die Regex statt eines
-- `body::jsonb`-Casts hält das Upgrade dennoch robust gegenüber beschädigten Altdateien: Nur
-- ein vollständig valider UUID-Treffer wird gecastet; fehlende oder defekte Bodies bleiben
-- ohne Indexwert, statt die gesamte Migration zu blockieren.
WITH candidates AS (
    SELECT id,
           (regexp_match(body,
               '"ProcessInstanceId"[[:space:]]*:[[:space:]]*"([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})"'))[1]
               AS process_instance_id
    FROM {schema}.user_task_assignment_events
    WHERE process_instance_id IS NULL
)
UPDATE {schema}.user_task_assignment_events AS events
SET process_instance_id = candidates.process_instance_id::uuid
FROM candidates
WHERE events.id = candidates.id
  AND candidates.process_instance_id IS NOT NULL;

CREATE INDEX IF NOT EXISTS user_task_assignment_events_instance_time_idx
    ON {schema}.user_task_assignment_events (process_instance_id, occurred_at, user_task_id, revision)
    WHERE process_instance_id IS NOT NULL;

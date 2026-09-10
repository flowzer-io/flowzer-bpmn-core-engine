-- Tatsächliche Bearbeiter offener Human Tasks. Der Zustand endet mit der Subscription;
-- die Auditspur bleibt dagegen bewusst auch nach Abschluss oder Abbruch erhalten.
CREATE TABLE IF NOT EXISTS {schema}.user_task_work_states (
    user_task_id uuid PRIMARY KEY REFERENCES {schema}.user_task_subscriptions (id) ON DELETE CASCADE,
    revision     bigint NOT NULL CHECK (revision > 0),
    body         text NOT NULL
);

CREATE TABLE IF NOT EXISTS {schema}.user_task_assignment_events (
    id           uuid PRIMARY KEY,
    user_task_id uuid NOT NULL,
    revision     bigint NOT NULL CHECK (revision > 0),
    occurred_at  timestamptz NOT NULL,
    body         text NOT NULL,
    UNIQUE (user_task_id, revision)
);

CREATE INDEX IF NOT EXISTS user_task_assignment_events_task_idx
    ON {schema}.user_task_assignment_events (user_task_id, revision);

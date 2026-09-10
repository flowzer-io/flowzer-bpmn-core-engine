-- Einmalig gebundene Human-Task-Termine und persistente, deduplizierte Meldungen.
CREATE TABLE IF NOT EXISTS {schema}.user_task_deadlines (
    user_task_id  uuid PRIMARY KEY REFERENCES {schema}.user_task_subscriptions (id) ON DELETE CASCADE,
    revision      bigint NOT NULL CHECK (revision > 0),
    next_check_at timestamptz NULL,
    body          text NOT NULL
);

CREATE INDEX IF NOT EXISTS user_task_deadlines_next_check_idx
    ON {schema}.user_task_deadlines (next_check_at)
    WHERE next_check_at IS NOT NULL;

CREATE TABLE IF NOT EXISTS {schema}.user_task_notifications (
    id                uuid PRIMARY KEY,
    user_task_id      uuid NOT NULL REFERENCES {schema}.user_task_subscriptions (id) ON DELETE CASCADE,
    kind              text NOT NULL,
    occurred_at       timestamptz NOT NULL,
    deduplication_key text NOT NULL UNIQUE,
    body              text NOT NULL
);

CREATE INDEX IF NOT EXISTS user_task_notifications_task_time_idx
    ON {schema}.user_task_notifications (user_task_id, occurred_at DESC);

CREATE TABLE IF NOT EXISTS {schema}.user_task_notification_reads (
    notification_id uuid NOT NULL REFERENCES {schema}.user_task_notifications (id) ON DELETE CASCADE,
    owner_key       char(64) NOT NULL,
    read_at         timestamptz NOT NULL,
    PRIMARY KEY (notification_id, owner_key)
);


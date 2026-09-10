-- Der aktive Identitaetsverzeichnisstand wird als ein Dokument gespeichert. Dadurch ist die
-- Umschaltung von Snapshot und Sync-Status dieselbe atomare Datenbanktransaktion. Die lokale
-- Benutzer-/Gruppen-ID und ihre historische Deaktivierung sind Teil dieses Dokuments; keine
-- fehlgeschlagene oder unvollstaendige Quelle kann den aktiven Stand halb ersetzen.
CREATE TABLE IF NOT EXISTS {schema}.identity_directory_state (
    singleton        boolean PRIMARY KEY DEFAULT true CHECK (singleton),
    active_snapshot  text NULL,
    sync_status      text NULL
);

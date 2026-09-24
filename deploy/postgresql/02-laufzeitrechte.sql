-- Flowzer: Rechte der Laufzeitrolle im Flowzer-Schema (idempotent).
--
-- Vergibt genau die Rechte, die die Laufzeit braucht, und nimmt ihr DDL:
--   * USAGE auf dem Schema, kein CREATE
--   * SELECT, INSERT, UPDATE, DELETE auf allen Tabellen, USAGE und SELECT auf allen Sequenzen
--     - fuer vorhandene Objekte sofort, fuer kuenftige Objekte der Migrationsrolle ueber
--     Default-Privileges
--   * auf schema_migrations ausschliesslich SELECT: GET /health/ready liest den Migrationsstand
--     mit der Laufzeitverbindung, schreiben darf die Historie nur die Migrationsrolle
--
-- Wird von 01-datenbank-und-rollen.sql eingebunden und laeuft zusaetzlich nach jedem Restore
-- (scripts/runtime/restore.sh). Grund: Default-Privileges und Schema-Rechte haengen am Schema
-- selbst. Ein `DROP SCHEMA ... CASCADE` (restore.sh --force) nimmt sie mit, und ein Restore in ein
-- vorbereitetes Schema gibt schema_migrations ueber die Default-Privileges mehr als SELECT.
-- Mehrfaches Ausfuehren aendert nichts.
--
-- Ausfuehren als Superuser oder als Migrationsrolle (Eigentuemerin von Schema und Tabellen),
-- verbunden mit der Flowzer-Datenbank:
--   psql -v migrationsrolle=flowzer_maass_it_migration \
--        -v laufzeitrolle=flowzer_maass_it \
--        -v schema=flowzer \
--        -f 02-laufzeitrechte.sql

\set ON_ERROR_STOP on

SELECT format('GRANT USAGE ON SCHEMA %I TO %I', :'schema', :'laufzeitrolle') \gexec
SELECT format('REVOKE CREATE ON SCHEMA %I FROM %I', :'schema', :'laufzeitrolle') \gexec

-- Rechte fuer kuenftige Tabellen und Sequenzen der Migrationsrolle ...
SELECT format(
  'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
  :'migrationsrolle', :'schema', :'laufzeitrolle') \gexec
SELECT format(
  'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I',
  :'migrationsrolle', :'schema', :'laufzeitrolle') \gexec

-- ... und fuer bereits vorhandene (nach einer Migration oder einem Restore).
SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', :'schema', :'laufzeitrolle') \gexec
SELECT format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', :'schema', :'laufzeitrolle') \gexec

-- Die Migrationshistorie darf die Laufzeit nur lesen. Die Default-Privileges oben gelten nur fuer
-- Tabellen, die spaeter entstehen; auf die vorhandene Tabelle wirken REVOKE und GRANT dauerhaft.
-- Fehlt die Tabelle noch (Schema ohne Historie), gibt es nichts zu korrigieren.
SELECT format('REVOKE ALL ON TABLE %I.schema_migrations FROM %I', :'schema', :'laufzeitrolle'),
       format('GRANT SELECT ON TABLE %I.schema_migrations TO %I', :'schema', :'laufzeitrolle')
WHERE to_regclass(format('%I.schema_migrations', :'schema')) IS NOT NULL \gexec

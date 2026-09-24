-- Flowzer: Datenbank und Rollen auf einem gemeinsamen PostgreSQL-Cluster.
--
-- Wird EINMALIG als Superuser ausgefuehrt, bevor das erste Deployment laeuft. Danach legt der
-- Migrationsschritt (`WebApiEngine --migrate`) Schema und Tabellen mit der Migrationsrolle an.
--
-- Zwei getrennte Identitaeten:
--   *_migration  besitzt die Datenbank, darf DDL und fuehrt die Migrationen aus
--   (Laufzeit)   darf im Schema lesen, einfuegen, aendern und loeschen, aber kein DDL
--
-- Anders als bei fachlichen Datenbestaenden braucht die Laufzeit hier DELETE: Subscriptions und
-- Definitionsversionen sind Laufzeitzustand der Engine und werden physisch entfernt.
--
-- Passwoerter werden hier NICHT gesetzt. Sie werden getrennt vergeben (ALTER ROLE ... PASSWORD)
-- und liegen ausschliesslich im Secret-Store bzw. in den Deployment-Secrets.
--
-- Die Rechte der Laufzeitrolle im Schema stehen in 02-laufzeitrechte.sql. Dieses Skript bindet
-- die Datei mit \ir ein (Pfad relativ zu diesem Skript); beide Dateien muessen deshalb im selben
-- Verzeichnis liegen. Nach einem Restore laeuft 02 allein erneut (scripts/runtime/restore.sh).
--
-- Aufruf:
--   psql -v datenbank=flowzer_maass_it \
--        -v migrationsrolle=flowzer_maass_it_migration \
--        -v laufzeitrolle=flowzer_maass_it \
--        -v schema=flowzer \
--        -f 01-datenbank-und-rollen.sql

\set ON_ERROR_STOP on

-- 1. Rollen ------------------------------------------------------------------

SELECT format(
  'CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 5',
  :'migrationsrolle')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'migrationsrolle') \gexec

SELECT format(
  'CREATE ROLE %I LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS CONNECTION LIMIT 30',
  :'laufzeitrolle')
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = :'laufzeitrolle') \gexec

-- 2. Datenbank ---------------------------------------------------------------

SELECT format('CREATE DATABASE %I OWNER %I ENCODING ''UTF8''', :'datenbank', :'migrationsrolle')
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = :'datenbank') \gexec

SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'datenbank') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'datenbank', :'migrationsrolle') \gexec
SELECT format('GRANT CONNECT ON DATABASE %I TO %I', :'datenbank', :'laufzeitrolle') \gexec

-- 3. Schema und Rechte -------------------------------------------------------

\connect :datenbank

REVOKE ALL ON SCHEMA public FROM PUBLIC;

SELECT format('CREATE SCHEMA IF NOT EXISTS %I AUTHORIZATION %I', :'schema', :'migrationsrolle') \gexec
-- Die Migrationshistorie wird hier angelegt (Migrator: CREATE TABLE IF NOT EXISTS wird zum No-op),
-- damit 02 ihr schon vor der ersten Migration das eingeschraenkte Recht geben kann: Schreiben
-- darf sie nur die Migrationsrolle; die Laufzeit darf sie lesen, denn GET /health/ready meldet
-- den Migrationsstand mit der Laufzeitverbindung (sonst dauerhaft "Unknown").
SELECT format('CREATE TABLE IF NOT EXISTS %I.schema_migrations (version integer PRIMARY KEY, name text NOT NULL, applied_at timestamptz NOT NULL DEFAULT now())', :'schema') \gexec
SELECT format('ALTER TABLE %I.schema_migrations OWNER TO %I', :'schema', :'migrationsrolle') \gexec

-- Schema-USAGE, kein CREATE, Default-Privileges, Rechte auf vorhandene Tabellen und Sequenzen,
-- schema_migrations nur lesend.
\ir 02-laufzeitrechte.sql

-- 4. Nachweis ----------------------------------------------------------------

SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolconnlimit
FROM pg_roles
WHERE rolname IN (:'migrationsrolle', :'laufzeitrolle')
ORDER BY rolname;

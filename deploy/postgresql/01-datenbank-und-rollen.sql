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
SELECT format('GRANT USAGE ON SCHEMA %I TO %I', :'schema', :'laufzeitrolle') \gexec
SELECT format('REVOKE CREATE ON SCHEMA %I FROM %I', :'schema', :'laufzeitrolle') \gexec

-- Rechte fuer kuenftige Tabellen der Migrationsrolle ...
SELECT format(
  'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO %I',
  :'migrationsrolle', :'schema', :'laufzeitrolle') \gexec
SELECT format(
  'ALTER DEFAULT PRIVILEGES FOR ROLE %I IN SCHEMA %I GRANT USAGE, SELECT ON SEQUENCES TO %I',
  :'migrationsrolle', :'schema', :'laufzeitrolle') \gexec

-- ... und fuer bereits vorhandene, falls das Skript nach einer Migration erneut laeuft.
SELECT format('GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA %I TO %I', :'schema', :'laufzeitrolle') \gexec
SELECT format('GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA %I TO %I', :'schema', :'laufzeitrolle') \gexec

-- Die Migrationshistorie bleibt der Laufzeit verborgen.
SELECT format('REVOKE ALL ON TABLE %I.schema_migrations FROM %I', :'schema', :'laufzeitrolle')
WHERE EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = :'schema' AND tablename = 'schema_migrations') \gexec

-- 4. Nachweis ----------------------------------------------------------------

SELECT rolname, rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolconnlimit
FROM pg_roles
WHERE rolname IN (:'migrationsrolle', :'laufzeitrolle')
ORDER BY rolname;

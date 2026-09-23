#!/bin/sh
# Legt Datenbank, Schema und die getrennten Rollen mit dem ausgelieferten Installationsskript
# an und vergibt danach die Testpasswörter. Läuft nur beim ersten Start des leeren Volumes.
set -eu

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v datenbank="$FLOWZER_TEST_DATABASE" \
  -v migrationsrolle="$FLOWZER_TEST_MIGRATION_ROLE" \
  -v laufzeitrolle="$FLOWZER_TEST_RUNTIME_ROLE" \
  -v schema="$FLOWZER_TEST_SCHEMA" \
  -f /flowzer-sql/01-datenbank-und-rollen.sql

# Variablen werden nur im Skriptmodus ersetzt, nicht mit -c; deshalb über stdin.
psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v migrationsrolle="$FLOWZER_TEST_MIGRATION_ROLE" \
  -v migrationspasswort="$FLOWZER_TEST_MIGRATION_PASSWORD" \
  -v laufzeitrolle="$FLOWZER_TEST_RUNTIME_ROLE" \
  -v laufzeitpasswort="$FLOWZER_TEST_RUNTIME_PASSWORD" <<'SQL'
ALTER ROLE :"migrationsrolle" PASSWORD :'migrationspasswort';
ALTER ROLE :"laufzeitrolle" PASSWORD :'laufzeitpasswort';
SQL

#!/usr/bin/env bash
set -euo pipefail

# Schreibt den OpenAPI-Schnappschuss neu, den der Vertragstest vergleicht.
# Nach jeder gewollten Aenderung an der Aussenansicht der API ausfuehren und das
# Ergebnis mit einchecken.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repo_root"

FLOWZER_UPDATE_OPENAPI_SNAPSHOT=1 dotnet test src/WebApiEngine.Tests/WebApiEngine.Tests.csproj \
  --configuration Release \
  --filter "FullyQualifiedName~GeneratedOpenApiDocument_ShouldMatchTheCommittedSnapshot"

printf 'Der Schnappschuss liegt jetzt in docs/openapi.json.\n'

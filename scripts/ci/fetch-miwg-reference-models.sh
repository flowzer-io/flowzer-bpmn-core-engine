#!/usr/bin/env bash
#
# Laedt die Referenzmodelle der BPMN Model Interchange Working Group (MIWG) nach
# src/core-engine-tests/embeddings/miwg/ und schreibt dorthin eine MANIFEST.md mit
# Quelle, Commit-SHA der Suite und Lizenzhinweis.
#
# Die heruntergeladenen Dateien werden eingecheckt: Der Konformitaetstest laeuft
# damit offline und reproduzierbar. Dieses Skript dient nur dem Aktualisieren.
# Nach einem Lauf gehoert `git diff` geprueft — aendern sich Modelle, aendert sich
# in der Regel auch das erwartete Verhalten in miwg-expectations.json.
#
# Aufruf:
#   scripts/ci/fetch-miwg-reference-models.sh            # aktueller Stand von master
#   scripts/ci/fetch-miwg-reference-models.sh <commit>   # ein bestimmter Commit
#
set -euo pipefail

REPO_SLUG="bpmn-miwg/bpmn-miwg-test-suite"
REPO_URL="https://github.com/bpmn-miwg/bpmn-miwg-test-suite"
SOURCE_DIRECTORY="Reference"
LICENSE_NAME="Creative Commons Attribution 3.0 Unported (CC BY 3.0)"
LICENSE_URL="http://creativecommons.org/licenses/by/3.0/"

# Obergrenze fuer den eingecheckten Satz. Die Suite liegt derzeit bei rund 1,3 MB
# reinen BPMN-Dateien; Bilder, Visio- und PDF-Beiwerk werden nie geladen. Wird die
# Grenze ueberschritten, bricht das Skript ab, statt das Repository stillschweigend
# um mehrere Megabyte wachsen zu lassen.
MAXIMUM_TOTAL_BYTES=$((8 * 1024 * 1024))

repository_root="$(cd "$(dirname "$0")/../.." && pwd)"
target_directory="${repository_root}/src/core-engine-tests/embeddings/miwg"
requested_ref="${1:-master}"

for required in curl python3; do
    if ! command -v "${required}" >/dev/null 2>&1; then
        echo "Fehlendes Werkzeug: ${required}" >&2
        exit 1
    fi
done

curl_arguments=(--fail --silent --show-error --location --header "Accept: application/vnd.github+json")
if [ -n "${GITHUB_TOKEN:-}" ]; then
    curl_arguments+=(--header "Authorization: Bearer ${GITHUB_TOKEN}")
fi

work_directory="$(mktemp -d)"
trap 'rm -rf "${work_directory}"' EXIT

echo "Ermittle Commit fuer ${REPO_SLUG}@${requested_ref} ..."
curl "${curl_arguments[@]}" \
    "https://api.github.com/repos/${REPO_SLUG}/commits/${requested_ref}" \
    >"${work_directory}/commit.json"
commit_sha="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["sha"])' "${work_directory}/commit.json")"
commit_date="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["commit"]["committer"]["date"])' "${work_directory}/commit.json")"
echo "Commit ${commit_sha} vom ${commit_date}"

curl "${curl_arguments[@]}" \
    "https://api.github.com/repos/${REPO_SLUG}/contents/${SOURCE_DIRECTORY}?ref=${commit_sha}" \
    >"${work_directory}/listing.json"

# Eine Zeile je Modell: Name, Groesse, Download-URL — getrennt durch Tabulator.
python3 - "${work_directory}/listing.json" >"${work_directory}/models.tsv" <<'PYTHON'
import json
import sys

with open(sys.argv[1], encoding="utf-8") as handle:
    entries = json.load(handle)

models = sorted(
    (entry for entry in entries if entry["type"] == "file" and entry["name"].endswith(".bpmn")),
    key=lambda entry: entry["name"],
)
for model in models:
    print(f"{model['name']}\t{model['size']}\t{model['download_url']}")
PYTHON

model_count="$(wc -l <"${work_directory}/models.tsv" | tr -d ' ')"
total_bytes="$(awk -F'\t' '{sum += $2} END {print sum + 0}' "${work_directory}/models.tsv")"

if [ "${model_count}" -eq 0 ]; then
    echo "Im Verzeichnis ${SOURCE_DIRECTORY} wurden keine *.bpmn-Dateien gefunden." >&2
    exit 1
fi

if [ "${total_bytes}" -gt "${MAXIMUM_TOTAL_BYTES}" ]; then
    echo "Die Referenzmodelle sind mit ${total_bytes} Bytes groesser als die Obergrenze ${MAXIMUM_TOTAL_BYTES}." >&2
    echo "Bitte den Satz bewusst einschraenken (etwa auf die Reihen A und B) statt die Grenze still anzuheben." >&2
    exit 1
fi

rm -rf "${target_directory}"
mkdir -p "${target_directory}"

while IFS=$'\t' read -r name size download_url; do
    echo "  ${name} (${size} Bytes)"
    curl "${curl_arguments[@]}" "${download_url}" --output "${target_directory}/${name}"
done <"${work_directory}/models.tsv"

{
    printf '# MIWG-Referenzmodelle\n\n'
    printf 'Unveraenderte Kopie der Referenzmodelle der BPMN Model Interchange Working Group.\n'
    printf 'Diese Dateien werden **nicht von Hand bearbeitet**; aktualisiert werden sie\n'
    printf 'ausschliesslich ueber `scripts/ci/fetch-miwg-reference-models.sh`.\n\n'
    printf '| Feld | Wert |\n'
    printf '| --- | --- |\n'
    printf '| Quelle | %s |\n' "${REPO_URL}"
    printf '| Verzeichnis | `%s/` (nur `*.bpmn`) |\n' "${SOURCE_DIRECTORY}"
    printf '| Commit | `%s` |\n' "${commit_sha}"
    printf '| Commit-Datum | %s |\n' "${commit_date}"
    printf '| Geladen am | %s |\n' "$(date -u '+%Y-%m-%d')"
    printf '| Dateien | %s |\n' "${model_count}"
    printf '| Gesamtgroesse | %s Bytes |\n' "${total_bytes}"
    printf '| Lizenz | %s |\n' "${LICENSE_NAME}"
    printf '| Lizenztext | %s |\n\n' "${LICENSE_URL}"
    printf '## Lizenz und Namensnennung\n\n'
    printf 'Die BPMN MIWG Test Suite steht unter der %s.\n' "${LICENSE_NAME}"
    printf 'Namensnennung: BPMN Model Interchange Working Group (bpmn-miwg), %s.\n' "${REPO_URL}"
    printf 'Die Modelle werden hier unveraendert als Testdaten verwendet.\n\n'
    printf '## Enthaltene Modelle\n\n'
    printf '| Datei | Bytes |\n'
    printf '| --- | --- |\n'
    while IFS=$'\t' read -r name size _; do
        printf '| `%s` | %s |\n' "${name}" "${size}"
    done <"${work_directory}/models.tsv"
} >"${target_directory}/MANIFEST.md"

echo "${model_count} Modelle nach ${target_directory} geschrieben (${total_bytes} Bytes)."
echo "MANIFEST.md aktualisiert."

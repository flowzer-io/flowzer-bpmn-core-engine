#!/bin/sh
# Schreibt die Laufzeitkonfiguration der Blazor-Oberflaeche aus Umgebungsvariablen.
# Die Datei muss beim Publish bereits existieren (Blazor laedt nur Konfigurationsdateien
# aus dem Boot-Manifest); hier wird nur ihr Inhalt ersetzt.
set -eu
# Kein Globbing: Scopes wie "api://x/*" duerfen nicht gegen Dateinamen expandiert werden.
set -f

TARGET="/usr/share/nginx/html/appsettings.Production.json"
API_BASE_URL="${FLOWZER_API_BASE_URL:-/}"
OIDC_AUTHORITY="${FLOWZER_OIDC_AUTHORITY:-}"
OIDC_CLIENT_ID="${FLOWZER_OIDC_CLIENT_ID:-}"
OIDC_SCOPES="${FLOWZER_OIDC_SCOPES:-}"

# Steuerzeichen (Zeilenumbruch, Tab) haben in diesen Werten nichts verloren und wuerden
# das JSON unbrauchbar machen; danach Backslash und Anfuehrungszeichen maskieren.
json_escape() {
  printf '%s' "$1" | tr -d '\000-\037' | sed 's/\\/\\\\/g; s/"/\\"/g'
}

SCOPES_JSON=""
for scope in $OIDC_SCOPES; do
  if [ -n "$SCOPES_JSON" ]; then SCOPES_JSON="$SCOPES_JSON, "; fi
  SCOPES_JSON="$SCOPES_JSON\"$(json_escape "$scope")\""
done

cat > "$TARGET" <<EOF
{
  "FlowzerApi": { "BaseUrl": "$(json_escape "$API_BASE_URL")" },
  "Oidc": {
    "Authority": "$(json_escape "$OIDC_AUTHORITY")",
    "ClientId": "$(json_escape "$OIDC_CLIENT_ID")",
    "Scopes": [$SCOPES_JSON]
  }
}
EOF

exec nginx -g 'daemon off;'

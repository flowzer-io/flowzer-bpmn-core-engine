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

# Optional als Gateway: Ist FLOWZER_API_UPSTREAM gesetzt (z. B. api:8080), leitet nginx alle
# API-Pfade dorthin und bedient nur den Rest als statische Oberflaeche. So braucht ein
# Deployment hinter Traefik/Coolify keinen zusaetzlichen Gateway-Container.
API_UPSTREAM="${FLOWZER_API_UPSTREAM:-}"
if [ -n "$API_UPSTREAM" ]; then
  cat > /etc/nginx/conf.d/default.conf <<NGINX
server {
  listen 8080;
  server_name _;

  root /usr/share/nginx/html;
  index index.html;

  proxy_http_version 1.1;
  proxy_set_header Host \$host;
  proxy_set_header X-Real-IP \$remote_addr;
  proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
  proxy_set_header X-Forwarded-Proto \$http_x_forwarded_proto;
  proxy_read_timeout 120s;
  client_max_body_size 8m;

  location ~ ^/(health|definition|instance|message|usertask|form|timer|operations|swagger)(/|\$) {
    proxy_pass http://${API_UPSTREAM};
  }

  location / {
    try_files \$uri \$uri/ /index.html;
  }
}
NGINX
fi

exec nginx -g 'daemon off;'

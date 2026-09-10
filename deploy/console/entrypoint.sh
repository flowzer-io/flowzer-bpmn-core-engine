#!/bin/sh
# Schreibt die Laufzeitkonfiguration der React-Konsole und richtet nginx ein.
#
# Das gebaute Bundle ist unveraenderlich. Unkritische Bereitstellungswerte kommen
# deshalb aus Umgebungsvariablen in eine config.json. OIDC-Clientdaten und Tokens
# verbleiben vollständig im serverseitigen BFF.
set -eu
TARGET="/usr/share/nginx/html/config.json"
API_BASE_URL="${FLOWZER_API_BASE_URL:-/}"
case "${FLOWZER_BFF_ENABLED:-true}" in
  true|TRUE|1|yes|YES) BFF_ENABLED=true ;;
  *) BFF_ENABLED=false ;;
esac
# Akzentfarbe der Oberflaeche, damit sie zum Erscheinungsbild des Unternehmens passt.
# Erlaubt: iris, teal, emerald, amber, rose. Ein unbekannter Wert faellt auf iris zurueck.
ACCENT="${FLOWZER_ACCENT:-iris}"

# Steuerzeichen haben in diesen Werten nichts verloren und wuerden das JSON unbrauchbar
# machen; danach Backslash und Anfuehrungszeichen maskieren.
json_escape() {
  printf '%s' "$1" | tr -d '\000-\037' | sed 's/\\/\\\\/g; s/"/\\"/g'
}

cat > "$TARGET" <<EOF
{
  "apiBaseUrl": "$(json_escape "$API_BASE_URL")",
  "accent": "$(json_escape "$ACCENT")",
  "bffEnabled": $BFF_ENABLED
}
EOF

API_UPSTREAM="${FLOWZER_API_UPSTREAM:-}"
if [ -n "$API_UPSTREAM" ]; then
  # Nur host:port zulassen; alles andere koennte die nginx-Konfiguration erweitern.
  case "$API_UPSTREAM" in
    *[!A-Za-z0-9._:-]*|*:*:*|*[!0-9]) echo "FLOWZER_API_UPSTREAM muss die Form host:port haben" >&2; exit 1 ;;
  esac
  case "$API_UPSTREAM" in *:*) ;; *) echo "FLOWZER_API_UPSTREAM muss die Form host:port haben" >&2; exit 1 ;; esac

  cat > /etc/nginx/conf.d/default.conf <<NGINX
# Der Runtime-Stack besitzt zwei Proxy-Stufen. Das externe Schema muss die zweite
# Stufe unveraendert passieren; beim direkten Aufruf faellt sie auf ihr lokales
# Schema zurueck. Ein vorgeschalteter Proxy muss eingehende Client-Header ersetzen.
map \$http_x_forwarded_proto \$flowzer_forwarded_proto {
  ""      \$scheme;
  default \$http_x_forwarded_proto;
}

server {
  listen 8080;
  server_name _;

  root /usr/share/nginx/html;
  index index.html;

  # Docker-DNS zur Laufzeit: Der API-Container bekommt nach einem Neustart eine neue
  # Adresse; ein beim Start eingefrorener Upstream wuerde danach 502 liefern.
  resolver 127.0.0.11 valid=10s ipv6=off;
  set \$flowzer_api http://${API_UPSTREAM};

  proxy_http_version 1.1;
  # Der rohe Host-Header behaelt einen expliziten Port fuer die Origin-Pruefung.
  proxy_set_header Host \$http_host;
  proxy_set_header X-Real-IP \$remote_addr;
  proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
  proxy_set_header X-Forwarded-Proto \$flowzer_forwarded_proto;
  proxy_read_timeout 120s;
  client_max_body_size 8m;

  # Die Oberflaeche gehoert in kein fremdes Rahmenfenster, und der Browser soll den
  # Inhaltstyp nicht raten.
  add_header X-Frame-Options DENY always;
  add_header X-Content-Type-Options nosniff always;
  add_header Referrer-Policy strict-origin-when-cross-origin always;

  # Diese Liste muss alle API-Routen enthalten. Fehlt eine, beantwortet die Konsole sie
  # mit ihrer eigenen Startseite: Der Aufruf bekommt 200 und niemals die erwartete Antwort.
  #
  # Der Pfad operations traegt bewusst einen Schraegstrich: Die Konsole hat selbst eine Seite
  # unter /operations, die API nur den Unterpfad /operations/diagnostics. Ohne den
  # Schraegstrich liefe ein Neuladen der Betriebsseite gegen die API statt gegen den Router.
  #
  # Swagger fehlt bewusst: Die Beschreibung der API gehoert nicht unter die oeffentliche
  # Adresse der Oberflaeche.
  # Bewusst ~* und nicht ~: ASP.NET Core routet unabhaengig von Gross- und Kleinschreibung,
  # und die OpenAPI-Beschreibung nennt die Pfade mit grossem Anfangsbuchstaben
  # (/Definition/meta). Ein daraus erzeugter Client traefe eine Regel mit ~ nicht und bekaeme
  # die Startseite der Oberflaeche mit Status 200 statt der Antwort der API.
  # bff umfasst Login, Session und CSRF unter derselben Origin.
  location ~* ^/(bff|health|definition|folder|identity-directory|instance|job|message|usertask|form|timer)(/|\$) {
    proxy_pass \$flowzer_api;
  }

  location ~* ^/operations/ {
    proxy_pass \$flowzer_api;
  }

  # Die Konsole ist eine Einzelseitenanwendung: Jede unbekannte Adresse gehoert an ihren
  # Router, sonst waere ein Neuladen auf einer Unterseite ein 404.
  location / {
    try_files \$uri \$uri/ /index.html;
  }

  location = /config.json {
    add_header Cache-Control "no-store";
  }
}
NGINX
else
  cat > /etc/nginx/conf.d/default.conf <<'NGINX'
server {
  listen 8080;
  server_name _;

  root /usr/share/nginx/html;
  index index.html;

  add_header X-Frame-Options DENY always;
  add_header X-Content-Type-Options nosniff always;
  add_header Referrer-Policy strict-origin-when-cross-origin always;

  location / {
    try_files $uri $uri/ /index.html;
  }

  location = /config.json {
    add_header Cache-Control "no-store";
  }
}
NGINX
fi

exec nginx -g 'daemon off;'

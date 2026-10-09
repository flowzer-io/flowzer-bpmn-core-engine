#!/bin/sh
# Erzeugt ausschließlich die isolierten Embed-Locations. Kein Token oder Secret
# gehört in diese öffentliche Konfiguration; die API autorisiert alle Aktionen.
set -eu
API_ORIGIN="${FLOWZER_EMBED_API_ORIGIN:-}"
HOST_ORIGINS="${FLOWZER_EMBED_HOST_ORIGINS:-}"
fail() { echo 'Ungültige oder unvollständige Flowzer-Einbettungskonfiguration.' >&2; exit 1; }
if [ -z "$API_ORIGIN" ] && [ -z "$HOST_ORIGINS" ]; then
  cat <<'NGINX'
location = /embed.html { return 404; }
location ^~ /embed-assets/ { return 404; }
NGINX
  exit 0
fi
[ -n "$API_ORIGIN" ] && [ -n "$HOST_ORIGINS" ] || fail
# Exakte HTTPS-DNS-Origins; kein Userinfo, Pfad, Wildcard oder nginx-Metazeichen.
# DNS-Labels/Port werden zusätzlich zur Gesamtform begrenzt und geprüft.
valid_origin() {
  # grep/awk prüfen zeilenweise. Zuerst muss wirklich eine einzelne ASCII-Origin
  # feststehen, auch für den nicht als Liste behandelten API-Wert.
  case "$1" in *[!A-Za-z0-9.:/-]*) return 1 ;; esac
  [ ${#1} -le 260 ] || return 1
  printf '%s\n' "$1" | LC_ALL=C grep -Eq '^https://[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?(:[0-9]{1,5})?$' || return 1
  authority="${1#https://}"; host="${authority%%:*}"
  [ ${#host} -le 253 ] || return 1
  printf '%s\n' "$host" | LC_ALL=C awk -F. '{for(i=1;i<=NF;i++) if(length($i)<1 || length($i)>63 || $i !~ /^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?$/) exit 1}' || return 1
  case "$authority" in *:*) port="${authority##*:}"; [ "$port" -ge 1 ] && [ "$port" -le 65535 ] || return 1 ;; esac
}
valid_origin "$API_ORIGIN" || fail
[ ${#HOST_ORIGINS} -le 2080 ] || fail
# Nur ASCII-Leerzeichen trennen Host-Origins; Tabs/Newlines werden nicht in
# Richtlinien eingeschleust. Jede Origin bleibt ein exakter CSP-Ausdruck.
case "$HOST_ORIGINS" in *[!A-Za-z0-9.:/' '-]*) fail ;; esac
set -f
count=0
for host_origin in $HOST_ORIGINS; do
  valid_origin "$host_origin" || fail
  count=$((count + 1)); [ "$count" -le 8 ] || fail
done
[ "$count" -gt 0 ] || fail
cat <<NGINX
# Kein X-Frame-Options DENY hier: eigene vollständige Header verhindern dessen
# Vererbung. Alle anderen Console-Routen behalten ihren bisherigen Framing-Schutz.
location = /embed.html {
  add_header X-Content-Type-Options nosniff always;
  add_header Referrer-Policy no-referrer always;
  add_header Cache-Control "no-store" always;
  add_header Content-Security-Policy "default-src 'none'; script-src ${API_ORIGIN}/embed-assets/; style-src ${API_ORIGIN}/embed-assets/ 'unsafe-inline'; font-src ${API_ORIGIN}/embed-assets/ data:; img-src data:; connect-src ${API_ORIGIN}/form-embed/redeem ${API_ORIGIN}/form-embed/start/redeem; form-action 'none'; base-uri 'none'; object-src 'none'; frame-ancestors ${HOST_ORIGINS}; sandbox allow-scripts;" always;
  try_files \$uri =404;
}
location ^~ /embed-assets/ {
  add_header X-Content-Type-Options nosniff always;
  add_header Referrer-Policy no-referrer always;
  add_header Cache-Control "no-cache" always;
  # Nur öffentliche statische Assets. Der opaque Kalender liest das Stylesheet
  # über CSSOM; CORS erlaubt dabei weder Cookies noch eine API-Session.
  add_header Access-Control-Allow-Origin "null" always;
  try_files \$uri =404;
}
NGINX

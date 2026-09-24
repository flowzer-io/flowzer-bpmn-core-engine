#!/bin/sh
# Erzeugt einmalig eine Test-CA und ein Serverzertifikat für flowzer.test und
# auth.flowzer.test. Nur für den isolierten Abnahme-Stack; nichts davon wird eingecheckt.
#
# /out/ca  – nur das öffentliche CA-Zertifikat (für API und Testläufer)
# /out/tls – Serverzertifikat und -schlüssel für den TLS-Proxy (plus CA-Zertifikat)
# Der CA-Schlüssel verlässt den Container nicht und wird am Ende verworfen.
set -eu

CA_DIR=/out/ca
TLS_DIR=/out/tls

if [ -s "$CA_DIR/ca.crt" ] && [ -s "$TLS_DIR/server.crt" ] && [ -s "$TLS_DIR/server.key" ]; then
  echo "Testzertifikate bereits vorhanden."
  exit 0
fi

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT
cd "$WORK_DIR"

openssl req -x509 -newkey rsa:3072 -nodes -sha256 -days 30 \
  -subj "/CN=Flowzer Installation-Auth Test CA" \
  -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
  -addext "keyUsage=critical,keyCertSign,cRLSign" \
  -keyout ca.key -out ca.crt 2>/dev/null

openssl req -newkey rsa:2048 -nodes -sha256 \
  -subj "/CN=flowzer.test" \
  -keyout server.key -out server.csr 2>/dev/null

cat > server.ext <<'EXT'
basicConstraints=critical,CA:FALSE
keyUsage=critical,digitalSignature,keyEncipherment
extendedKeyUsage=serverAuth
subjectAltName=DNS:flowzer.test,DNS:auth.flowzer.test
EXT

openssl x509 -req -in server.csr -CA ca.crt -CAkey ca.key -CAcreateserial \
  -days 30 -sha256 -extfile server.ext -out server.crt 2>/dev/null

openssl verify -CAfile ca.crt server.crt

cp ca.crt "$CA_DIR/ca.crt"
cp ca.crt "$TLS_DIR/ca.crt"
cp server.crt "$TLS_DIR/server.crt"
cp server.key "$TLS_DIR/server.key"
chmod 0644 "$CA_DIR/ca.crt" "$TLS_DIR/ca.crt" "$TLS_DIR/server.crt"
chmod 0600 "$TLS_DIR/server.key"
echo "Testzertifikate erzeugt (SAN: flowzer.test, auth.flowzer.test)."

#!/usr/bin/env bash
set -euo pipefail

# Baut den BPMN-Modeler aus bpmn.io/ und legt das Ergebnis an beiden Orten ab, an denen
# es im Repository liegt: als Beispielseite unter bpmn.io/public/ und als Bundle, das die
# Blazor-Oberflaeche ausliefert. Beide Kopien muessen identisch bleiben, sonst laeuft in
# der Oberflaeche eine andere Version als die, die hier gebaut und geprueft wird.
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root/bpmn.io"

npm ci
npm run build
cp public/app.js "$repo_root/src/FlowzerFrontend/wwwroot/app.js"

printf 'Bundle gebaut und nach src/FlowzerFrontend/wwwroot/app.js uebernommen.\n'

# Kleine stdin-Vertragsprüfung im bestehenden CI-Job

Der zusätzliche Schritt in `.github/workflows/ci.yml` bleibt im bestehenden
`dotnet`-Job. Er startet keine API, Datenbank oder weiteren Services:

```bash
python3 -I -B scripts/ci/test_startup_secret_ci.py
python3 -I -B scripts/ci/run_startup_secret_contracts.py \
  --dotnet "$(command -v dotnet)" --temp-root "$RUNNER_TEMP"
```

Lokal sind explizite absolute SDK- und Temp-Basispfade zu übergeben. Der Runner
erzeugt darunter ausschließlich einen eigenen Wegwerfbereich. HOME, CLI, TMP und
NuGetcache sind getrennt; nur feste Konfigurationswerte werden an Kinder gegeben.
Der paketfreie Restore verwendet eine eigene leere Quellenliste, deaktivierte
implizite SDK-Feeds und keine Targeting-/Runtimepackdownloads. Fehlende installierte
Frameworks sind Fehler, keine Download- oder Rollforward-Ausnahme.

`TargetLatestRuntimePatch=true` bindet den Build an die zum eingerichteten SDK
gehörenden Frameworks. Danach wird geprüft, dass beide **exakt** deklarierten
Runtimes installiert sind: die vorhandenen Kindtests behalten
`DOTNET_ROLL_FORWARD=Disable`. Der DLL-/SDK-Pfad wird ausdrücklich gesetzt.

Unverändert laufen die 17 reinen Tests unter `tests/startup-secret-input/` gegen
die echte verlinkte Bootstrapquelle. Testanzahl, Rückgabestatus, Skips, erwartete
Fehler sowie die vorhandenen Leakassertions bleiben harte Gates; beide Kanäle der
Testkinder werden weiterhin von diesen Tests geprüft. Ein neuer Test benötigt eine
bewusste Anpassung der Zählkonstante und des Vertrags, keine stille Nulltest-Abnahme.

SDK-Aufrufe erhalten eine eigene Prozessgruppe und höchstens 180 Sekunden;
notwendige TERM-/KILL-Schritte sind ausschließlich bei noch lebendem eigenen
Leader zulässig und jeweils auf fünf Sekunden begrenzt. Nach dessen Abschluss
bestätigt nur `ESRCH` die leere Gruppe. Bei unbekanntem Gruppenabschluss bleibt
der eigene Tempbereich erhalten; keine fremden oder verwaisten Prozesse werden
übernommen. Die Runtime-Liste ist zusätzlich auf 64 KiB begrenzt und wird ohne
Hintergrundreader gelesen. Normale Restore-/Buildausgabe bleibt ungefiltert sichtbar.

Dies ist ein Source-/CI-Anschluss, noch kein ausgeführter Hosted-CI-, Positivstart-,
PostgreSQL-/Keycloak- oder Demo-Nachweis. Die frühere Bootstrap-/Operatorprovenienz
und ihre eingefrorenen Quell-/Artefaktpins werden nicht überschrieben. Solution-,
Image-, Staging- und Releasejobs bleiben unverändert.

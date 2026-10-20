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

## Zusätzliche Feeder-Fakes, keine Runtimequalifikation

Ein weiterer unbedingter Schritt im **gleichen** `dotnet`-Job ergänzt den alten
13-/17-Vertrag, ohne ihn zu ersetzen:

```bash
python3 -I -B scripts/ci/test_runtime_smoke_ci.py
python3 -I -B scripts/ci/run_runtime_smoke_contracts.py \
  --dotnet "$(command -v dotnet)" --temp-root "$RUNNER_TEMP"
```

Der kleine Helper verwendet die vorhandenen geprüften Funktionen für eigene
Arbeitsbereiche, Umgebungen und begrenzte CLI-Prozessgruppen wieder. Er führt
genau die **19** Python-Fakes aus und restored/baut danach einmal ausschließlich
`tests/runtime-bootstrap-smoke/SmokeFeeder.csproj`. Dieser Runner benötigt nur
denselben öffentlichen Npgsql-Treiber wie der bestehende Storageprovider.
Eine eigene NuGet-Konfiguration enthält ausschließlich nuget.org; NuGet-Audit
bleibt unverändert aktiv. Keine privaten Feeds, Workloads, Solutionbuilds,
Container, Images, Datenbanken oder API-Starts werden dafür angestoßen.

Anders als der alte Runner mit zwei Frameworks verwendet der neue Runner nur
NETCore und dessen bestehenden **Standard-Minor-Auflösungsvertrag**: eine
installierte `10.0`-Runtime mindestens auf dem deklarierten Patchlevel muss
vorhanden sein. Der höchste passende installierte Patch ist zulässig; eine
Gleichheit mit dem SDK-Patch wird weder verlangt noch behauptet. Runtimeconfig
und installierte Runtimes werden vor dem Testaufruf geprüft. Weder Projekt noch
`rollForward` oder `DOTNET_ROLL_FORWARD` werden dafür verändert.

Nur `--contract-test` wird ausgeführt: Exit0, jede der **15** festen PASS-IDs
genau einmal und die terminale Zusammenfassung `total=15 passed=15 failed=0
skipped=0` sind gemeinsam notwendig. Bei Countabweichung, Skip, ExpectedFailure,
unerwarteter Ausgabe oder Toolfehler ist der Anschluss rot. Die sieben neuen
Helper-/Verdrahtungstests verwenden ausschließlich Mocks und negative Fälle.

Dieser Anschluss beweist weder tatsächlich ausgeführte Hosted-CI noch sichere
Liveadapter oder Demoabnahme. Das offene P3 der numerischen Kind-PID-Signalisierung
im eigenen exklusiven APIcontainer bleibt unverändert und wird hier nicht
weggetestet. Linux-/Image-/Keycloak-/HTTPS-/45-Minuten- und reale Cleanupabnahme
benötigen weiterhin ihre getrennten tatsächlichen Belege und Freigaben.

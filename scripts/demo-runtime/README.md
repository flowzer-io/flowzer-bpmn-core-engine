# Temporärer Ressourcenpilot — isolierter Abnahmevorschlag

## Bestehende Discovery-Anfrage beobachten – Diagnose nach Run 38064007895

Der tatsächliche Erstlauf `38064007895/a1` auf
`5393481635b99774a8f9034e68148ef6fae06353` bleibt **FAILURE** mit Auth-Exit1,
zehn Samples und bestätigtem eigenen Cleanup. Die tatsächliche Fehltestposition
`2` ist im unveränderten CF-Harness der zweite Check-config-Test: Discovery mit
konfiguriertem Issuer und PKCE S256. Die übrigen 14 Endergebnisse sind unbekannt;
HTTP-Status, Transportursache und Feldvergleiche waren bisher ebenfalls unbekannt.

Der Exportadapter der **Kopie** beobachtet ausschließlich diesen vorhandenen
Discovery-Aufruf. Kein weiterer HTTP-Aufruf, keine geänderten Argumente, keine
Änderung von CA/TLS, DNS, Redirects oder Timeout. Antwort und Originalfehler werden
identisch zurückgegeben; alle anderen HTTP-/Token-/Admin-/API-Aufrufe bleiben
unangetastet. Originalspecs, ihre Assertions und die 28 kopierten Dateien bleiben
unverändert. Der bereits vorhandene sichere Reporter trägt die Beobachtung,
nicht eine vierte Hilfsdatei.

Eine eigene `discovery-result.json` wird ausschließlich neben dem eigenen gebundenen
`auth-result.json` mit `wx`/0600 angelegt. Sie enthält exakt `status` (tatsächlicher
Integer 100–599 oder null), `transport_code` (feste Nummer 1–13 oder null),
`issuer_matches` und `pkce_s256` (Boolean oder null). Nur dieselben bereits gelesenen
Antwortbytes werden im RAM geparst. URLs, Header, Antworten, Error-Rohtext, Tokens
und personenbezogene Inhalte werden niemals exportiert. Nicht-JSON/unbekannte
Werte sind **null**, kein erfundener Erfolg/Nullcode. Fehlende oder unbeschreibbare
Dateibindung ändert die originale HTTP-Entscheidung nicht und besitzt keinen Fallback.

Die geschlossene Liste der tatsächlichen [Node-Fehlercodes](https://nodejs.org/docs/latest-v22.x/api/errors.html)
und [TLS-Zertifikatcodes](https://nodejs.org/docs/latest-v22.x/api/tls.html) lautet:
1 `ENOTFOUND`, 2 `EAI_AGAIN`, 3 `ECONNREFUSED`, 4 `ECONNRESET`, 5 `ETIMEDOUT`,
6 `EPROTO`, 7 `ERR_TLS_CERT_ALTNAME_INVALID`, 8 `UNABLE_TO_VERIFY_LEAF_SIGNATURE`,
9 `SELF_SIGNED_CERT_IN_CHAIN`, 10 `DEPTH_ZERO_SELF_SIGNED_CERT`,
11 `UNABLE_TO_GET_ISSUER_CERT_LOCALLY`, 12 `CERT_HAS_EXPIRED`,
13 `ERR_SSL_WRONG_VERSION_NUMBER`. Andere oder fehlende Codes bleiben null.
Diese Namen stehen nur in der geprüften Source-Liste, nicht im Runtimebericht.

Ausschließlich nach einem **tatsächlichen Auth-Kindprozess-Exit** übernimmt der Runner
eine vorhandene, streng validierte Beobachtung (maximal 1024 Bytes) als `discovery`.
Fehlend bedeutet unbekannt; manipulierter Inhalt/Symlink/IO bekommt nur die feste
Zusatzphase `discovery_report`, niemals Ersatz des primären Auth-Exits. Sampling-
oder Stopfehler öffnen keine Discovery-Datei. Der erfolgreiche Authvertrag bleibt
exakt unverändert; diese Beobachtung ist kein neues Erfolgs-/Akzeptanzgate.

Neun Python- und fünf Mock-Node-Assertion-REDs belegten die fehlenden Verträge
inklusive Existenzprüfungs-IO und Diagnosepriorität. Danach bestehen **77 Python/21 Mock-Node ohne Skips**
mit derselben lokalen Leser-Allowlist (173 Git-, vier Ruby-YAML- und ein Node-VM-Read).
Alle bisherigen 69 Pythonmethoden und 16 Mock-Nodefälle bleiben erhalten. Es lief
lokal kein echter HTTP-/Browser-/Docker-/Authprozess. Maximal drei Diagnosen,
1500s, Statusannahmen, Ressourcenlimits, Ownership/Cleanup und sämtliche Netz-
und Authgrenzen bleiben unverändert.

Der erste unabhängige Gesamtreview fand P2: Auth-Exit plus beide optionalen
Berichtfehler konnten Max3 vor dem echten Cleanupfehler belegen. Ein unveränderlicher
abgelehnter Snapshot bleibt erhalten. Zwei additive echte Runpfad-Mock-REDs
belegten den Verlust für Ownership/Cleanup sowie für den tatsächlichen
`command → stop_process`-Pfad mit Cleanup-Timeout und Stop-IO. Innerhalb unverändert
Max3 dürfen jetzt ausschließlich spätere echte Stop-/Cleanup-Diagnosen einen
optionalen `auth_report`/`discovery_report`-Platz verdrängen. Die primäre Ursache
(Index0) und echte Runtimeursachen bleiben erhalten. Keine Cleanup-/Abbruchaktion,
Fehlerakzeptanz oder Ressourcen-/Zeitgrenze ändert sich.

Ein weiterer serieller Hosted-Erstlauf braucht
aktuellen Whole-Freeze, unabhängigen Gesamtreview, exakten SHA/Tree/Parent und frische
Source-CI/Publisher-/Registrybelege. Historische Fehlläufe werden nicht umetikettiert.

## Numerische Fehltestposition – Diagnose nach Run 38063115353

Der neue reale Erstlauf `38063115353/a1` auf
`7b7ae4e7d3845b2fe09f4137e5cadec1b0e07d80` bleibt **FAILURE** mit dem tatsächlichen
Auth-Exit1. Acht Samples und eigenes bestätigtes Cleanup sind Fortschritt, keine
vollständige Auth-/Ressourcenabnahme. Der herkunftsgebundene Fehlversuchsbericht
belegt `total=16`, `passed=1`, `failed=1`, `skipped=0`, `interrupted=0`, `errors=0`.
Die übrigen 14 Tests haben keine Endzähler; ihr Ergebnis bleibt unbekannt.
Eine Eingrenzung auf das erste Check-config-Projekt ist aus der festen Projekt-
und Abhängigkeitsreihenfolge abgeleitet, noch kein beobachteter Einzeltestbeleg.

Der eigene sichere Reporter ordnet nun die von Playwright dokumentierte
[sitzungseindeutige TestCase.id](https://playwright.dev/docs/api/class-testcase#test-case-id)
nur im RAM ihrer positiven 1-basierten Position in der tatsächlichen Suite zu.
Nur tatsächliche bekannte Fehltests ergänzen `failed_test_indexes`; maximal 16
sortierte eindeutige Integer und nie mehr als die tatsächliche Fehltestanzahl.
Titel, IDs, Pfade, Fehlermeldungen, Anhänge und Authmaterial werden nicht exportiert.
Unbekannte oder doppelte Identitäten bleiben ohne Kennung, niemals Index 0.
Erfolgreiche Berichte behalten exakt den bisherigen Sieben-Felder-Vertrag.

Ein echtes Python- und ein echtes Mock-Node-Assertion-RED belegten den fehlenden
optional geschlossenen Vertrag. Danach bestehen **69 Python- und 16 Mock-Node-Tests
ohne Skips**; alle bisherigen 68 Pythonmethoden und 14 Nodefälle bleiben erhalten.
Die Nodeprobe greift absichtlich geschützte Titel-/Rohfehlergetter nicht an.
Originalspecs und alle Auth-/Prozess-/Ressourcen-/Cleanup-/Netzgates bleiben gleich.
Diese Änderung ist zunächst nur eine weitere genaue Diagnose, keine Behebung
des unbekannten fachlichen Authfehlers. Vor neuer Publikation und genau einem
seriellen Hosted-Erstlauf sind Whole-Freeze, unabhängiger Review und aktuelle Proofs Pflicht.

## Fehlgeschlagene Auth-Zähler erhalten – Diagnose nach Run 38061614856

Der reale Erstlauf `38061614856/a1` auf
`2901249a458d6d6ab83df176932f696e9b25aa55` erreichte erstmals die Original-Authtests,
erhob **11 tatsächliche Samples** und bestätigte das eigene Cleanup. Er bleibt
**FAILURE** mit `auth/process_exit/1`; eine vollständige Auth-/Ressourcenabnahme
oder eine Langzeit-/Live-Abnahme folgt daraus nicht. Welche Tests scheiterten,
ist bisher unbekannt: Der bestehende sichere Reporter schreibt nur Zähler, aber
der Runner las sie bislang ausschließlich nach erfolgreichem Prozessende.

Nach einem tatsächlich beobachteten Auth-ProcessExit bleiben vorhandene,
streng geschlossene fehlgeschlagene Reporterzahlen jetzt separat als
`auth_attempt` erhalten. Sie sind **keine** erfolgreiche `auth`-Abnahme.
Keine Titel, Fehlertexte, Anhänge, URLs oder Secrets; maximal die sieben bisherigen
Zahlen-/Boolfelder. Zähler sind echte Integer und begrenzt, Überzählung und ein
widersprüchlicher Erfolg werden abgelehnt. Ein fehlender oder manipulierter Bericht
erhält nur `auth_report` als feste Zusatzphase; die primäre tatsächliche Exitdiagnose
bleibt zuerst. Nicht beendete Tests werden nicht als Erfolg oder als Null erfunden.

Der unabhängige Gesamtreview bestätigte zunächst P2: Ein allgemeiner
`ProcessExitError` kann auch aus Sampling stammen und beweist noch keinen Exit
des Auth-Kindprozesses. Der tatsächliche `returncode` des eigenen gemessenen
Kindprozesses erhält deshalb einen engen Untertyp. Nur diese Herkunft aktiviert
`auth_attempt`; Sampling-/Stopfehler bleiben bei ihrer eigenen Phase und lesen
keinen Authbericht. Weder Prozessstatusprüfung noch Fehlerprojektion ändern sich.
Ein unveröffentlichter Snapshot des abgelehnten Zwischenstands bleibt erhalten.

Vier echte Assertion-REDs in drei Methoden belegten zuerst den fehlenden Vertrag und
die verlorenen Fehlversuchszähler; drei weitere echte Assertion-REDs belegten die
Herkunftsgrenze. Die neuen Provenienztests durchlaufen den tatsächlichen gemockten
`measured_command`-Pfad für Samplingfehler und eigenen Kindprozess-Exit.
Final bestehen **68 Python- und 14 Mock-Node-Tests
ohne Skips**; alle bisherigen 63 Pythonmethoden und sämtliche JS-Quellen bleiben
unverändert. Es lief lokal kein echter Browser-, Docker- oder Authprozess.
Originalspecs, Prozessstatusprüfung, 1500s-Authgrenze, maximal drei Fehler,
Besitzprüfung, Cleanup und Ressourcen-/Netzgates ändern sich nicht. Dieser Zyklus
verbessert zunächst nur die Diagnose; ein neuer serieller Hosted-Erstlauf bleibt
an Freeze, unabhängigen Gesamtreview, exakten SHA/Tree/Parent und frische Proofs gebunden.

## Gruppierte Inspect-Ausdrücke – Quellenfix nach Run 38060250739

Der tatsächliche neue Hosted-Erstlauf `38060250739/a1` auf
`92beb45ed974ceee03668bf1556394055a4f1a31` bestand nun die reale
Browsergegenkalibrierung, die geschlossene Egressprobe und beide Fixturetests.
Image-Pull und Runtime-Startphase wurden erreicht. Danach endete er mit
`sampling/process_exit/1` und `cleanup/process_exit/1`: **null Samples, kein
bestätigtes Cleanup und kein Auth-/Ressourcen-/OOM-Erfolgsnachweis**.

Im gemeinsamen Inventorypfad war die Containerprojektion als
`{{json index .Config.Labels "..."}}` aufgebaut. Nach dem
[Go-Templatevertrag für Argumente und Pipelines](https://pkg.go.dev/text/template#hdr-Arguments)
ist ein verschachtelter Funktionsaufruf als Argument zu gruppieren;
[Docker verwendet Go-Templates und seine JSON-Funktion](https://docs.docker.com/engine/cli/formatting/).
Nun erhalten **alle 14 unveränderten Ausdrücke** genau eine solche Gruppe,
also beispielsweise `{{json (index .Config.Labels "...")}}`.
Felder, Eigentümer-/Digestprüfung, Limits und Reihenfolge bleiben identisch.
Die bekannte falsche Funktionsargumentbindung ist ein Source-Vertragsbefund;
welcher konkrete Laufbefehl die zwei beobachteten Exit1 verursachte, ist ohne
einen entsprechenden tatsächlichen Nachweis weiterhin nicht eindeutig belegt.

Zwei neue unabhängige Vertragstests über die vollständige Ausdrucksliste und
den tatsächlich aus `inventory()` gesendeten Formatparameter belegten vor dem
Fix **zwei echte Assertion-REDs**. Danach bestehen **63 Python- und 14
Mock-Node-Tests ohne Skips**. Alle bisherigen 61 Pythonmethoden/Assertions und
14 Nodefälle bleiben erhalten; es lief lokal kein echter Docker-/Go-Parser,
Browser, Netzwerkzugriff oder Container. Ein bloßer Go-Parse würde zudem keinen
korrekten Funktionsargumentvertrag zur Ausführungszeit beweisen.

Der nächste reale Nachweis nutzt den vorhandenen nativen Docker-Templatepfad
bei `inventory()` im isolierten Hosted-Erstlauf; ein zusätzlicher lokaler oder
Hosted-Fixtureprozess wird nicht eingeführt. Auch Cleanup muss unverändert
zuerst den eigenen Besitz beweisen und wird nicht zum Umgehen eines roten
Inspectpfads gelockert. Vor erneutem Push/Dispatch bleiben neue Whole-Freeze,
unabhängiger Gesamtreview und Root-Tree-Registrierung erforderlich.

## Synthetisches Zertifikat ohne Descriptor-Datei – Quellenfix nach Run38059398637

Der tatsächliche folgende Erstlauf `38059398637/a1` auf
`75cb951380d019071a9cc1b40bb315f9e7512373` endete nun mit der beobachteten
Diagnose `browser_probe_certificate/process_exit/1`: Der synthetische
OpenSSL-Zertifikatprozess lieferte Exit1. Runtime und Auth starteten nicht;
null Samples und nicht bestätigtes Cleanup bleiben ein Fehlernachweis.

Der bisherige Aufruf gab für Key und Zertifikat `/dev/stdout` als **Dateiname**
an. Node dokumentiert ausdrücklich, dass seine Spawn-Pipes nicht über solche
Descriptor-Dateien wieder geöffnet werden können ([Node stdio](https://nodejs.org/docs/latest-v22.x/api/child_process.html#optionsstdio)).
Der feste OpenSSL-Sentinel `-` verwendet dagegen für beide Ausgaben den bereits
geöffneten Stdout-Stream ([OpenSSL bio_open_owner/bio_open_default](https://github.com/openssl/openssl/blob/openssl-3.0.13/apps/lib/apps.c#L2842-L2942)).
Nur diese zwei Argumentwerte ändern sich. Kein Dateifallback, keine privaten
Schlüsseldateien, kein zusätzlicher Prozess, kein stderr-/Materiallog, keine
Änderung an RSA2048, einem Tag Gültigkeit, Subject, Timeout15s oder Buffer64KiB.

Eine neue Mock-Node-Unit belegte zuerst **ein echtes Assertion-RED** für die
vollständige Spawnargument-/Optionsbindung. Final bestehen **61 Python- und14
Mock-Node-Tests ohne Skips**; alle bisherigen Methoden/Assertions bleiben
unverändert. Kein echter OpenSSL-, Browser-, Socket- oder Dockerlauf lokal.
Der bekannte API-Vertragsfehler ist damit source-only korrigiert; ob er den
beobachteten Exit1 vollständig erklärt und der neue Pilot weiterkommt, wird
nicht vor seinem tatsächlichen neuen Hosted-Lauf behauptet. Neue Source-Freeze,
unabhängiger Review und exakte Root-Registrierung bleiben vor Push/Dispatch nötig.

## Kalibrierungsinterner Zahlenbericht – Quellenfortsetzung nach Run38057493900

Der tatsächlich neu ausgeführte Hosted-Erstlauf `38057493900/a1` auf
`ca42f6bc9e484feefb00cd368f65b455407ee9f0` endete vor Runtime/Containern mit
`browser_calibration_report/io/null`, null Samples und nicht bestätigtem Cleanup.
Die Kalibrierung hatte einen tatsächlich beobachteten akzeptierten Exit1, danach
fehlte der Bericht. Die konkrete JS-Ursache bleibt **unbekannt**; dieser Sourcefix
ist zunächst nur ein gezielter Beobachtbarkeits-/Cleanupfix, kein Runtime-Erfolg.

Die eigentlichen Probeimporte, der gepinnte Vertragscheck, synthetisches TLS und
alle eigenen Closeversuche liegen nun innerhalb eines gemeinsamen geschützten
Zahlenreportpfads. Jede eigene Ressource erhält auch nach einem anderen Closefehler
ihren bisherigen Schließversuch. Primärursache bleibt zuerst; maximal drei feste
Stage-/Fehler-/wirkliche Exitwerte, niemals Exceptiontexte oder TLSmaterial.
Ein nicht beobachteter Zertifikatprozessstatus bleibt unknown/null. Der Marker-RED
allein genügt nicht, wenn danach Cleanup scheitert. Die Python-Grenze akzeptiert
nur feste eigene Stage-/Fehlerkategorien und weist freie Felder, unbekannte Phasen,
Bool-Exitwerte, erfundene Status und übergroße Fehlerlisten geschlossen zurück.

Vor Umsetzung belegten vier echte Node-Assertions und vier Python-Subfall-Assertions
den fehlenden Bericht beziehungsweise die falsche Stufenprojektion; das sind
acht eindeutige REDs, keine Roh-Importfehler oder doppelt gezählte Wiederholungen.
Final bestehen **61 Python- und13 Mock-Node-Tests ohne Skips**; alle bisherigen
58 Pythonmethoden/Assertions und acht Nodefälle bleiben unverändert erhalten.
Zusätzliche Close-Regressionsfälle laufen nur gegen synthetische JS-Objekte, keine
wirklichen Listener, Browser, Prozesse, Zertifikate oder Netzwerkziele.

Workflow/Refguard, Publisher-/Produkt-/Original-Authquellen, Befehlsargumente und
Zeit-/Ressourcen-/Netzgrenzen bleiben unverändert. Vor weiterem Push benötigt dieser
Delta erneut unabhängigen Review und Root-Tree-Registrierung; der nächste Pilot
ist ein neuer serieller Erstlauf auf seinem exakt bestätigten Source-SHA, kein
Replay oder Rerun des fehlgeschlagenen ca42-Laufs.

## Serielle Pilotfortsetzung – 10. Oktober 2026

Christian hat die Fortsetzung des isolierten GitHub-Hosted-Piloten bis zum
Erfolg ausdrücklich freigegeben. Der eigene Topic
`codex/flowzer-runtime-calibration-pilot` beginnt am frisch gefetchten Archivstand
`18dc2e7f64f212c711b708f976d7574eca5868a5`; dessen Archivbranch und alle älteren
Belege bleiben unverändert. Nur genau diese neue Ref wird im Workflow,
Vor-I/O-Guard und unabhängigen aktuellen Run-Read-back akzeptiert. Keine
Wildcard, zusätzliche Alternative, Publisher-, Installations- oder reale Realmfreigabe.

Sieben tatsächliche Assertion-REDs belegten vor dem Refwechsel die neue positive
Eigenidentität und die Ablehnung des früheren Pilottopics. Die bisherigen
58 Pythonmethoden/Assertions bleiben erhalten; positive synthetische Identitäten
ändern nur die feste Eigenref, negative Archivfälle werden ergänzt. Die bestehende
Browserkalibrierungsdiagnose sowie alle JS-Quellen, Prozess-/Ressourcenlimits,
acceptedExit1, maximal drei Fehler und sämtliche Netz-/Authgrenzen bleiben gleich.

Root registriert den geprüften Source-Head/Tree und danach jeden einzelnen neuen
Erstlauf (`attempt=1`, voller bestätigter Workflow-SHA). Keine blinden unveränderten
Wiederholungen und keine Wiederanläufe alter Runs. Aus einem roten Run werden
nur geschlossene Phasen-/Fehler-/wirkliche Exitfelder übernommen. Quelltests sind
kein Runtimebeleg; bisherige unbekannte Ursachen bleiben unbekannt. Keine Writes
nach Main/Release/Production, keine Dev01-Installation oder Kostenerhöhung, keine
neuen Publisherimages, keine bestehenden Realm-/n8n-/Produktionsänderungen.

Die folgenden Abschnitte bleiben ausdrücklich historische Archivdokumentation.
Ihre früheren Freigabegrenzen gelten für ihre damaligen Stände, nicht als
Aufhebung der oben dokumentierten neuen seriellen menschlichen Freigabe.

## Kalibrierungsdiagnose – ausschließlich Quellenstand, 10. Oktober 2026

`codex/flowzer-browser-calibration-diagnostics` wurde im eigenen frischen Worktree
vom frisch gefetchten Remote-HEAD `8a40c12398c360052abbb86272f26cf09f0ffcb5`
angelegt. Der alte HEAD/Tree, seine Quellen-/Reviewbelege und der tatsächliche zweite
Hosted-Pilot `38039728395/a1` bleiben unverändert. Dieser Pilot endete vor dem
Containerstart mit `browser_preflight/io/null`: Die konkrete Ursache ist **unbekannt**;
RAM, Auth, Netzgrenzen und Cleanup wurden dadurch nicht abgenommen.

Der neue minimale Quellschritt ergänzt ausschließlich zwei geschlossene Diagnosephasen:

- `browser_calibration_process`: der unveränderte Kalibrierungsaufruf einschließlich
  Popen-/CWD-I/O und seines wirklich beobachteten Exitstatus;
- `browser_calibration_report`: die unveränderte anschließende Datei-/JSON-/Markerprüfung.

Exit1 alleine ist weiterhin kein Marker-RED. Ein fehlender Bericht bleibt I/O mit
unbekanntem Exitstatus (`null`), nicht erfundene0 oder automatisch unterstellter
Browserfehler. Ein tatsächlich unerwarteter Exit0 bleibt dagegen `process_exit/0`.
Die Unterphasen enden nach ihrer Operation; spätere Egress-/Fixturefehler behalten
`browser_preflight`. Maximal drei Diagnosen, Statusannahmen, Befehlsreihenfolge,
Argumente, Zeitlimits und alle Netz-/Ownership-/Freshness-/Ressourcengrenzen bleiben
identisch. Sichere spätere Berichtleser müssen die zwei festen Enumwerte kennen;
Rohlogs oder zusätzliche freie Fehlerfelder sind kein Ersatz.

Sieben neue Unitmethoden rufen die **echte** `browser_preflight`-Orchestrierung auf,
ersetzen aber jeden Popen und Socket. Sie prüfen Launch-I/O vor einem Kindstatus,
fehlenden Bericht nach beobachtetem Mock-Exit1, unerwarteten Exit0, Setupfehler,
sechs widersprüchliche Markerfälle sowie den gültigen Ablauf und spätere Bericht-I/O.
Vor der Umsetzung gab es **zehn wirkliche Assertion-REDs** in fünf Methoden
(sechs davon einzelne Marker-Subfälle); keine Importfehler oder zuvor grünen
Regressionen werden als RED gezählt. Nun bestehen **58 Python-/8 Node-Unitfälle**
ohne Fehler oder Skips. Alle bisherigen51 Pythonmethoden und ihre Assertions
bleiben unverändert. Die alten Quellentests lesen nur ihre bestehenden lokalen
Gitblobs beziehungsweise Ruby-YAML-/Node-VM-Fixtures; sie führen keine Runtime aus.
Ein zunächst zu pauschaler globaler Popen-Tripwire blockierte elf dieser alten
Quellenleser. Dieser getrennte Umfangsversuch zählt weder als58GREEN noch als TDD-RED;
der vollständige grüne Lauf verwendete die ausdrücklich präzisierte lokale Leserfreigabe.

**Kein neuer Pilot, Retry, Publisher, Install oder Produktmerge.** Workflow, feste
alte Ref und alle JS-Quellen bleiben bytegleich; der neue Topic ist daher absichtlich
nicht dispatchfähig. Commit/Push benötigen die abschließende Registrierung bei Root.
Ein späterer Runtimeversuch benötigt einen getrennten Source-Scope und eine **neue
menschliche Einzelfreigabe**: Die bisherige Zustimmung wurde durch den zweiten
fehlgeschlagenen Piloten verbraucht. Quellentests reparieren oder beweisen dessen
unbekannte Ursache nicht. Demo, Realm/Gruppen, Zugänge, Host und SQL bleiben unverändert.

Die folgenden Abschnitte sind archivierte Entwicklungsgeschichte. Ihre damaligen
„noch kein Lauf“-Aussagen beziehen sich auf die jeweiligen Quellenvorbereitungen,
nicht auf den oben ausdrücklich eingeordneten tatsächlichen zweiten Piloten.

## Historische feste Eigen-Ref – Quellenvorbereitung des Standes8a40

`codex/flowzer-runtime-pilot-diagnostics` beginnt im eigenen frischen Worktree vom
frisch gefetchten Archiv-Commit `31ae808cd47107b6d295813d488816460491b45d`.
Nur diese exakte Ref ist im Workflow, im Vor-I/O-Kontextguard und in der aktuellen
GitHub-Run-Prüfung gebunden. Main, Release, fremde Refs, der alte Ressourcenbranch
und der bloße Archivbranch bleiben in diesem Kandidaten ausgeschlossen. Keine
Wildcard und keine zusätzliche alternative Ref. Alte positive Testfixtures ändern
ausschließlich ihre feste Eigenidentität; keine Testfälle oder Erwartungen entfallen.

Drei neue rein synthetische Ref-Tests belegten zunächst sieben echte Assertion-REDs.
Sie prüfen auch die unabhängige aktuelle API-Run-Identität vor Publisher-I/O und
die vollständige Workflow-Konjunktion statt nur passende Teilstrings. Alle bisherigen
48 Python-/8 Node-Fälle bleiben erhalten; Schutzgrenzen, Publisherquellen, originale
Authspecs und Ressourcenlimits sind unverändert. Die bisherigen Archiv-/Pilotbelege
werden weder verändert noch als Nachweis eines neuen Laufs umgedeutet.
Der vollständige synthetische Lauf besteht jetzt mit **51 Python-/8 Node-Tests**
ohne Skips; das sind keine tatsächlichen Hosted- oder Authausführungen.

**Bis zur Anmeldung und separaten Freigabe kein Push dieses Kandidaten. Kein
Dispatch, Pilot, Retry, Publisher, SSH/Docker/Runtime, Install oder Produktmerge.**
Ein tatsächlicher einzelner Hosted-Versuch bleibt separat freizugeben und zentral
zu koordinieren. Quellentests sind keine Ressourcen-, Auth-, Cleanup- oder Liveabnahme.

## Archivierter lokaler Diagnosekandidat (10. Oktober 2026)

`codex/flowzer-runtime-diagnostics` startet vom frisch gelesenen Runtime-Stand
`4725477244b2c948d3181b19721da127a586f704`. Der alte Stand, seine 40 Python-/8 Node-
Belege und der fehlgeschlagene Pilot `38016986837/a1` bleiben unverändert. Dieser
Kandidat gestattet **keinen** neuen Pilot, Dispatch, Retry, Publisher oder Deploy.
Seine unveränderte alte Workflow-Ref-Grenze ließ den bloßen Archivbranch nicht laufen.

Diagnosen enthalten ausschließlich feste `phase`-/`error`-Werte und einen wirklich
beobachteten POSIX-`exit_code`; `null` bedeutet ausdrücklich **unbekannt**, nicht 0.
Auch eine tatsächlich unerwartete 0 und negative Signalwerte bleiben unverändert.
Maximal drei Ursachen erhalten den ersten Fehler sowie zusätzliche Stop-/Cleanup-
Fehler. Innere Messfehler werden beim Weiterreichen nicht zu Startfehlern umbenannt.
Roh-Ausnahmen, Argumente, stdout/stderr, URLs, Umgebungswerte und Secrets erscheinen
weder im Zusatzbericht noch in der Konsole. Zeitlimits, Prozessgruppen-Stoppfolge,
Eigentümer-/Freshness-/Auth-/RAM-Grenzen und Originalspecs bleiben unverändert.

Neue fünf Diagnose-Unitfälle hatten vor Umsetzung neun wirkliche Assertion-REDs.
Die zwei unabhängigen Gesamtreviews fanden drei weitere Diagnosefehler: Gesamt-
deadline wurde als Validierung gemeldet, ein Abschlussread konnte trotz Fehler
`success=true` hinterlassen, und Bericht-I/O-Fehler konnten aus der Konsole fehlen.
Drei zusätzliche Unitfälle belegten diese zuerst mit acht Assertion-REDs. Zwei
weitere Assertion-REDs sichern ab, dass ein fehlgeschlagenes Rechte-Setzen noch
keine Erfolgsbytes schreibt: Der ausschließlich eigene Bericht wird mit `xb`
angelegt und erhält 0600 **vor** seinem ersten Byte. Docker-/Stop-Reihenfolge und
alle Zeit-/Ressourcen-/Autorisierungsgrenzen bleiben unverändert.

Jetzt bestehen insgesamt **48 Python-Tests** und **8 Node-Tests** ohne Skips;
alle Prozesse, Docker-/Auth- und Netzwerkzugriffe der neuen Tests sind synthetisch
gemockt. Das belegt weder die
Ursache des alten Piloten noch Runtime-, Cleanup-, Ressourcen- oder Installationsreife.

```sh
python3 -B -m unittest discover -s scripts/demo-runtime -p 'test_*.py'
```

**Aktueller Kandidat ausschließlich `codex/flowzer-runtime-pilot-diagnostics`, nicht nach Main, Release,
PR379 oder Candidate-bca übernehmen.** Die registrierte `ci.yml` ist nur in diesem
Wegwerfbranch durch einen manuellen Vorschlag ersetzt. Alle anderen Original-
Workflows sowie Produktquelle CF und Original-Harness bleiben bytegleich.

**Noch kein Dispatch oder Container-/Flowzer-/PG-/Keycloak-/Ressourcenlauf.**
Der koordinierende Root prüft den fertigen Head/Tree und gibt gegebenenfalls genau
einen eigenen Hosted-Abnahmelauf separat frei. Kein Installations-, Merge-, DNS-,
Proxy-, Realm-, Resize- oder anderer Demo-/Productionpfad entsteht dadurch.

## Kleiner geschlossener Runner

`runner.py` ist der einzige Runtime-Einstieg. Vor jedem I/O bindet er exaktes Repo,
eigenen Branch, manuellen Erstlauf, vollen bestätigten Workflow-SHA und Hosted
Linux/X64. Fremde Docker-/Browser-/Proxytransportvariablen sind Fehler. Zusätzlich
bestätigt `proof.py` den tatsächlich laufenden GitHub-Run und frisch:

- Original-CI37994630569/a3 mit acht erfolgreichen Jobs, Preview399543/CF-Tree
  und erhaltenem Testartefakt über die feste bereits geprüfte Publisherbibliothek;
- Publisher38009656496/a1, Headbcaeef/Treef573, sieben erfolgreiche Jobs und
  sieben exakt hash-/inhaltgebundene Receipt-ZIPs (`publish-bindings.json`);
- API-/Console-Finalindexes, gesamte native Builddescriptorunion einschließlich
  Provenance, beide Linuxplattformen und vollständige CF-Configrevision;
- alle vier festen Baseimage-Indexes/native-amd64-Manifeste und deren Configdigests.

Token bleiben im Prozess. HTTPS-Redirects sind standardmäßig geschlossen; ein
explizit erlaubter GitHub-Artifact-/Registryblobredirect erhält **keine Authheader**.
Roh-OCI-Configs, signierte Download-URLs und Fehlertexte werden nicht gespeichert.
Vorläufige Dockerzugänge nutzen nur einen eigenen nichtgeheimen Credentialhelper
und eine Pipe; kein `docker login`, kein Token in Argumenten oder Authsdateien.
Nach Pull endet der Helper, Browser-/Start-/Testprozesse bekommen nur eine
Umgebungs-Whitelist ohne GitHub-/Cloud-/IdP-/Vaulttoken.

`prepare.py` kopiert ausschließlich 25 Dateien/Modes aus festen CF-Gitblobs in
einen neuen externen Ordner. Keine versteckten Worktreebytes oder untracked
Materialien. Nur in der Kopie: eigener Namespace und zusätzliche Eigentümerlabels
für Container/Netz/Volumes, sechs feste Images für sieben Dienste, native Plattform,
harte RAM-/CPU-/Noswap-Limits, internes Netz und geprüfte Browser-/Reporterhelfer.
Kein Build, kein Tag, kein Original-`run.sh`, kein fremdes `down`/`prune`.

Vor jedem Messen und Cleanup kontrolliert der Runner tatsächliche Labels, CIDs,
Image/Configdigest, Memory/MemorySwap/NanoCpus; eigenes Netz muss intern sein.
Die Namespace-Leerprüfung berücksichtigt auch Netze und Volumes. Startup und alle
**16 Original-Authspecs** laufen mit identischen Assertions, Reihenfolge, null
Retries und unveränderten Spectimeouts. Original-Specs werden nur durch ihre
zusätzliche Fixture vermittelt. Auch der `--check-config`-Container gehört
zur Messung, statt dessen Zusatzbedarf auszublenden. Genau sein `--rm` wird nur in
der Kopie entfernt: CID, Exit und OOM bleiben bis zum validierten Gesamtcleanup
erhalten. Die Original-Specassertions bleiben bytegleich. Bereits beendete
Containerwerte sind keine künstlichen Nullmessungen. Ein OOM ist ein Fehlbefund. Jeder vollständig eigentümer-/image-/limitgebundene
Zustandsread merkt OOM monoton, auch ein später Inspect oder Cleanup nach frühem
Startfehler; fremde Zustände verändern die Diagnose nicht.

Gemessen werden diskrete cgroup-Gesamt-RAMwerte über den eigenen lokalen
Docker-Daemonsocket, CLI-CPU/PIDs, tatsächliche Limits/OOM/Exit/Restartzustände,
Docker-Partition vor/nach Pull/Start/Tests/Cleanup und eigene allozierte PG-Verzeichnisbytes als explizite KiB-Rundungsnäherung
(`du -sk`, BusyBox-portabel, keine exakte logische DBgröße).
CLI-RAM bleibt separat `memory_bytes_approx`: Docker zieht unter Linux Cache ab,
also ist dies **nicht** der vollständige RAMbedarf. [Docker-Stats-Dokumentation](https://docs.docker.com/reference/cli/docker/container/stats/)

Nur ein begrenzter, geschlossener `resource-result.json` mit Zahlen, festen
Enumwerten und Quellen-/Digestbelegen ist uploadbar. Keine Env-, Token-, URL-,
Browserexception-, Titel-, Attachment-, Trace-, Screenshot-, Video-, HTML- oder
Rohlogartefakte. Fehler/Abbruch schließen nur eigene Kindprozessgruppen und
validierten Ressourcenbesitz. Auch ein Cleanupfehler bleibt `success=false`.
Images werden nicht global gepruned; ihre Cachelebensdauer endet mit dem
GitHub-hosted Wegwerfrunner, nicht durch Löschen möglicherweise geteilter Basisimages.

## Browsernetzgrenze und wirkliche lokale Proben

`restricted-test.js` vermittelt **Browserprozess und alle Contexts** über denselben
worker-eigenen CONNECT-Proxy. Nur `flowzer.test:8443` und
`auth.flowzer.test:8443` sind erlaubt; TCP führt immer nach Loopback8443, nie durch
angefragten DNS. `<-loopback>` verhindert direkten Loopbackbypass. QUIC und
nichtvermittelter WebRTC-UDPverkehr sind geschlossen. Originroute ist nur ein
zusätzlicher früher Schutz; insbesondere Redirects benötigen den Proxy.
WebSockets und ServiceWorker sind gesperrt. Jeder unerlaubte Versuch schlägt fehl.

`egress-probe.js` beweist mit **echtem Chromium1.59.1** und ausschließlich eigenen
kurzlebigen Loopback-TLS-/Markerlistenern: erlaubte Seite200; Redirect auf IP und
verbotenen Namen, direkte IP, WS und SW blockiert; verbotener Marker0Requests.
Ein vorab erreichbarer Marker liefert genau1 Positivkontrollrequest, danach wird
sein Zähler zurückgesetzt. Synthetischer Testschlüssel bleibt nur im Speicher.

`--calibrate-local-only` verändert ausschließlich eine IN-MEMORY-Proxykopie für
**genau den eigenen Marker**. Keine DNS-/WAN-Route. Der wirkliche Markerassert
muss rot werden (1Request, `marker_assertion_red=1`), sonst ist die Messung kein
beweisender Negativtest. Ein Setup-/Browserfehler zählt ausdrücklich nicht.
`fixture-probe.spec.js` prüft außerdem zwei echte Playwright-Contexttests mit
worker-/launchOptions-/Contextproxybindung. Eigene Browser/Listener werden geschlossen.

Ein initialer Folgefehler nach absichtlich abgebrochener Navigation war
`ERR_ABORTED`, kein Egressnachweis. Der Folgetest verwendet deshalb eine neue
eigene Seite; keine Schutzregel, Origin oder Assertion wurde dafür gelockert.

## Ausgeführt versus noch offen

Offlinebefehle (keine Container oder externen Dienste):

```sh
python3 scripts/demo-runtime/test_prepare.py
python3 scripts/demo-runtime/test_proof.py
python3 scripts/demo-runtime/test_runner.py
python3 scripts/demo-runtime/test_workflow.py
node --test scripts/demo-runtime/test_helpers.js scripts/demo-runtime/test_proxy.js
```

Aktuell **40 Python- plus8 Node-Unitfälle**, echte YAML-Parsing-/JS-Syntaxprüfungen,
keine Skips. Test-first belegte Assertion-REDs für Limits/Eigentümerlabels,
Browserflags, manuelle Workflowgrenzen, aktuelle Runbindung, RAM-/Leerprüfung und
Egressreportprojektion. Frühe Proof-/Runner-Stubs hatten zusätzlich erwartete
noch-nicht-implementiert-Fehler; diese werden nicht als reine Assertion-REDs gezählt.
Lifecycle-/Cleanupfälle sind ergänzende Offline-Regressionen, kein echter Dockerlauf.
Der Review-P2 zur automatisch entfernten Oneoff-CID wurde mit realem Mocktransport-
Assertion-RED geschlossen; daneben beendete Null-Stats/Oneoff-Projektion und die
BusyBox-KiB-Projektion jeweils zuerst tatsächliches Assertion-RED, danach grün.
Vier zusätzliche OOM-Regressionsfälle waren mit sechs wirklichen Assertion-REDs
belegt: späte CLI-/Daemonreads, letzte Zustandsprüfung und Cleanup nach frühem
Startfehler. Nach vollständiger Bindung bleibt der Befund monoton im Fehlerbericht;
ein später eigener OOM kann kein erfolgreiches Artefakt mehr ergeben.

Separat tatsächlich lokal ausgeführt: echte Chromium-Positiv-/Mutation-/Negativ-
Proben und zwei echte Fixturetests mit synthetischen eigenen Listenern. **Kein**
Flowzer, PostgreSQL, Keycloak, realer TT/IdP, Imagepull, Docker oder Workflowdispatch.
Die Registry-/GitHub-Freshnessfunktion und Runnerorchestrierung sind noch **nicht
live ausgeführt**; ihre Offlinefälle mocken Metadaten beziehungsweise Docker.

API512MiB/PG256MiB/Console64MiB (832MiB) sind Schutzlimits, **kein Bedarf**.
Migration512MiB, Test-Keycloak768MiB, Proxy64MiB, Init32MiB sowie der zusätzliche
Check-config-Oneoff werden getrennt betrachtet. Diskrete und pro Container sequenzielle Samples sind keine
synchrone kontinuierliche Peakmessung; kurze Init-/Endphasen können zwischen Samples liegen.
Hostdiskdelta ist keine isolierte DBwachstums- oder Backup-/Restoremessung.
Dieser Authpilot allein ersetzt weder Backup-/Restorepeak, TT-Spitzenlast,
45-Minuten-Formularabnahme noch gemeinsame reale Keycloak-Gruppen. Alle bleiben
Abnahmegates vor einer Installation auf dem derzeit RAM-knappen dev01 oder einer
menschlichen Ressourcenentscheidung. Schutzlimits werden bei roten Tests nicht
still erhöht, Sandbox-/TLS-/Authschutz wird nicht gelockert.

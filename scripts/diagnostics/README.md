# Temporärer Registry-Diagnosebranch — niemals mergen

Isolierter Worktree vom frisch geholten `origin/main` **073e595748ba7bec56218407c5426474a4f6ba1e**.
Dieser Branch ist **kein Pull Request und kein Produkt-/Pflicht-CI-Kandidat**.
Die einzige `.github/workflows/ci.yml`-Definition hier ersetzt nur auf dem
Wegwerfbranch die normalen Jobs. Main, Release, Flowzer-PR #379 und dessen
Pflichtlauf 37994630569/a2 bleiben unverändert. Nach Diagnose Branch aufräumen,
niemals die temporäre Jobersetzung promoten oder als bestandene Pflicht-CI ausgeben.

## Tatsächlich verfügbarer Triggerpfad

Die vorhandene `ci.yml` ist auf Main als Workflow **259225084**, `state=active`,
mit `workflow_dispatch` registriert. GitHub erlaubt die Dispatchausführung am
expliziten Branch-Ref; verwendet wird dessen Version:
[GitHub-Dispatchvertrag](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch),
[CLI-Ref](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow).
Kein neuer Dispatchname benötigt eine Defaultbranch-Änderung.

Der Branch-Push löst keine Jobs aus: ci.yml hat hier ausschließlich manuelles
Dispatch; die übrigen unveränderten Workflows binden Main/Release bzw.
workflow_call. Ein manueller Dispatch am exakten Branch startet ausschließlich
`registry_readonly` auf `ubuntu-latest`, ohne Environments, Registrylogin,
Imagepull/build, Container-/Produktstart, Produkt-/Integrationstests oder Deploy.
Andere Refs verweigern diesen Job. Vor einem tatsächlichen Dispatch muss die
vollständige Workflowsammlung auf andere Trigger geprüft und unabhängig reviewt sein.

## Grenzen und Datenschutz

Drei feste lokale `docker image inspect` mit fünf Sekunden bis TERM und einer
Sekunde Kill-Nachfrist (höchstens sechs Sekunden je Inspect);
`cache_present_confirmed=false` beweist keine Bildabwesenheit. Drei einzelne
anonyme HTTPS-Tokenhostprobes mit echten öffentlichen Repository-Pullscopes,
fünf Sekunden Connect- und zwölf Sekunden Gesamtfrist. `curl --disable` als
erste Option schließt lokale curlrc-Konfiguration aus. Keine Redirects/Retry,
TLS-Prüfung unverändert. HTTP-Body einschließlich Token wird unmittelbar nach
`/dev/null` verworfen; keinerlei Header-/Auth-/Env-/Konfigurationsausgabe.
Ausgabe ausschließlich Runnerklasse, feste Labels, boolesche Bestätigungen,
HTTP-Status und Zeit. Kein neues Secret oder Login. Ein 200 beweist nur den
konkreten frischen anonymen Hosted-Netzpfad, nicht den Dockerdaemon-Authpfad,
existierende Credentials, Digest-/Manifestpull oder irgendeine Pflichtprüfung.
Fehlende positive Endpointprobe hält die Diagnose rot. Pflicht-CI-Retry braucht
anschließend weiterhin eine getrennte Koordination; hier nicht freigegeben.

# Isolierter Flowzer-Demo-Candidate — Quellvorschlag, nicht freigeschaltet

Dieser **temporäre Topic-Branch wird niemals nach `main`, `release` oder in PR #379
übernommen**. Nur hier ersetzt `ci.yml` die Workflowdefinition durch einen manuellen
Candidate-Aufruf. Die auf Main bereits registrierte CI kann mit genau diesem Ref
aufgerufen werden, ohne den Default-Workflow oder den normalen CI-Vertrag zu ändern.
Ein Push auf diesen Branch startet weder Candidate-Publikation noch bestehende Deployments.
Die separate Registrydiagnose wird nicht übernommen.

## Warum nicht der vorhandene Images-/Stagingpfad?

```text
main → staging.yml → images.yml → shared flowzer-api/console:latest → SecureSteps-Staging
release/v* → release.yml → images.yml → shared sha-/semver/prod-next → bestehende Production

codex/flowzer-demo-candidate --manuell, separat freigegeben-->
  Quell-/CI-Beleg → native API/Console-Builds → eigene Demo-Pakete → Digestbeleg
  KEIN Deploy, KEIN bestehender Environment-/Coolify-/Server-/IdP-Aufruf
```

`publish_latest=false` wäre nicht ausreichend: `images.yml` schreibt trotzdem
in die gemeinsamen Pakete und kann SHA-, Versions- und mitlaufende Tags publizieren.
Keine der vier übrigen Workflowdateien wird geändert oder aufgerufen. Der normale
Main-Merge von #379 bleibt wegen seiner automatischen Stagingwirkung **nicht freigegeben**.

## Exakte Quellen- und Registrygrenze

- Produktquelle **`cf082ccd9a165429774de9aab907d3583832279c`**, Tree
  **`38362a4d81f72ed1550c926883b5bb7f28ac50b5`**; keine Sourcewahl per Input.
- Original-Pflicht-CI **37994630569/a3**, acht insgesamt erfolgreiche Jobs. Zwei
  schon grüne Vorgängerjobs wurden übernommen, sechs frisch ausgeführt. Der
  Preview-Checkout **`399543b35af0d026c9e24fd39ddee61030d44b76`** hat Eltern
  `073e595748ba7bec56218407c5426474a4f6ba1e` und cf082 und exakt denselben Tree.
  Die Quellenprüfung liest diese festen GitHub-Metadaten und Testartefakt
  **11651284331** erneut; Verfall, Drift oder ein neuer Versuch schließen den Weg.
- Separater vollständiger **Workflow-SHA** muss beim manuellen Erstlauf bestätigt
  werden. Ein Wiederanlauf schließt in **jedem** Job vor Registrylogin. Für einen
  neuen Versuch ist ein neuer separat bestätigter Workflowlauf erforderlich.
- Einzige Pakete: `ghcr.io/flowzer-io/flowzer-tt-demo-api` und
  `ghcr.io/flowzer-io/flowzer-tt-demo-console`. Keine gemeinsamen Pakete, `latest`,
  Versions-/Release-/prod-next-Tags oder frei gewählten Registry-/Image-/Hostziele.
- Native amd64- und arm64-Builds aus einem getrennten exakten Produktcheckout,
  OCI-Revision voller cf082, vier Digestbelege. Keine Secrets als Buildargumente.
  Der Buildpfad behält Buildx-Provenance; OCI-Basisimages können zeitlich driften:
  **aktuelle Imageabnahme ist vor Installation zusätzlich erforderlich**.
- Einziger lesbarer Manifesttag: `demo-<40-stelliger Produkt-SHA>-r<Run-ID>-a1`.
  Vor Publikation muss die Registry genau 404 für diesen neuen Tag bestätigen;
  Timeout/401/403 bedeutet unbekannt, nie „frei“. Ein vorhandener Tag wird nicht
  überschrieben. Der finale HTTP-Manifestkörper wird gegen seinen tatsächlichen
  SHA256-Digest geprüft. **Installiert wird ausschließlich `image@sha256:…`, nie
  dieser Convenience-Tag.** GHCR-Tags sind technisch veränderlich.
- Proof-, Architektur- und Manifestreceipts enthalten keine Secrets, nur Quelle,
  CI-/Workflow-/Buildlauf und OCI-Digests. Token ausschließlich zur Laufzeit;
  feste HTTPS-Endpoints, keine Redirects/Retrys, begrenzte Antwortbytes und generische
  Fehlerlogs. Kein Coolify-/Deploysecret und kein Secretstorezugriff im Workflow.

**Aktuell keine Publikation und kein OCI-Digest vorhanden.** Hermetische Tests und
Quellenreviews ersetzen weder Hosted-Workflow-, Registry-, Image- noch Liveabnahme.
Kein Timer, keine Mail-/Kalender-/KI-Effekte und keine Änderung an Christian-Checkouts.

## Neue Installation auf dev01 — getrennte Freigabestufen

1. **Read-only Bestandsaufnahme freigeben:** Hostarchitektur, Docker/Compose,
   existierende Stack-/Netz-/Port-/Volume-/DBnamen, TLS-Proxyroute/DNS, Kapazität und
   Backupziel auf dev01. Keine rohen `inspect`-/Compose-/Env-Ausgaben; nur nicht
   geheime Auswahlfelder/Präsenzprüfungen. Bisher keine solchen Liveprüfungen.
2. **Registry-Go:** Reviewter voller Candidate-Workflow-SHA, festes cf082,
   37994630569/a3 und genau diese zwei neuen Pakete bestätigen. Dann genau **ein**
   manueller Dispatch, terminale Job-/Artifact-/Digest-/OCI-Label-/Plattform-Readbacks.
   Ein Teilfehler ist nicht installiert; keine Blind-Reruns oder gemeinsamen Tags.
3. **Installationsplan-Go:** Nach Bestandaufnahme separater Stack `flowzer-tt-demo`,
   eigenes Netz, eigene PostgreSQL-17-DB/Schema und getrennte Runtime-/Migrationsrollen,
   eigene API-Keyring- und Daten-/Backupvolumes. Keine vorhandene DB, StackUUID,
   Hostport/Netzrange oder Client-ID ungeprüft übernehmen. Nur verifizierter neuer
   HTTPS-Route `flowzer.tickytask.de` zuführen, kein bestehendes Routing umhängen.
   API/DB intern, keine öffentlich freigegebenen PostgreSQL-/APIports. PostgreSQL/
   Gateway-Basisimages ebenfalls am tatsächlich abgenommenen Digest binden.
4. **Keycloak-/Secret-Go:** Realm `TickyTask` auf dem tatsächlich in TT gebundenen
   Issuer. Eigene API-, BFF-, Directory- und technische Workerclients, separate
   TT-Exchangebindung. Keine vorhandenen Produktionsclients/-Secrets verwenden.
   API-Zugang/Modeler/Operator/Worker getrennt; keine Impersonations-/Adminrolle.
   Directory nur bestätigter Demo-Root-Teilbaum; Root-/Personalpfade und stabile IDs
   klärt Christian **direkt hier**, bisher offen. m2 und Vertretung müssen aktiv im
   Teilbaum verifiziert werden. Keine Änderungen an TT-Abteilungen/-Rechten.
   BFF-Redirect ausschließlich `https://flowzer.tickytask.de/bff/signin-oidc`;
   API-Audience getrennt vom BFF, TT-Zugang exakt TT `Oidc:Audience`, feste `access`-
   Rolle. Fachaktionen durch persönlichen serverseitigen Exchange, nie Linksecret.
   Directory-Minimalrechte am echten Provider kontrollieren. Secrets in freigegebenem
   Vaultwardenziel, zur Runtime nur im betreffenden Prozess; kein `.env`/Log/Browser.
5. **Installations-/Migrations-Go:** Digestgebundene API zunächst separat `--migrate`,
   dann Runtime mit `ApplyMigrationsOnStartup=false`, PostgreSQL-Laufzeitrechten und
   persistentem nur API-beschreibbarem Keyring. Vor Datenänderungen Backup/Prüfung;
   vorhandene `backup.sh`-/`restore.sh`-Verträge für eigene DB/Schema+Dateien+Keyring,
   Restoreprobe in einer zweiten **eigenen** isolierten DB. Keine Änderung an TT-DB
   auf diesem Pfad; TT-Schema ausschließlich über den attestierten Demo-Pfad.
6. **Einbettungs-/Demo-Go:** API-Opt-in und Console-Gateway gemeinsam exakt auf
   `https://flowzer.tickytask.de` / Host `https://demo.tickytask.de`. Opaque Sandbox
   nur `allow-scripts`, kein same-origin/storage/Login/Skriptfallback. Anonymous
   Linkeinlösung read-only; alle Aktionen tatsächliche TT-Person. Live-HTTPS/Keycloak,
   >=45 Minuten ohne Remount einschließlich Tokenrenewal, Draftrevision/Konkurrenz,
   Rechteentzug/Abbruch und beide TT-Designs prüfen. Kein Schutzlockern bei Fehlschlag.
   TT-Worker weiterhin zu bis gesonderte Lifecycle-/S15-/persönliche Rechteabnahme.

## Rollback und Abschluss

- Erstinstallationfehler: nur neuen Demo-Stack stoppen/isolieren; Daten/Keyring und
  Diagnosebelege erhalten, keine automatischen `down -v` oder Löschungen.
- Späterer Updatefehler: vorherigen **Demo-Digest** und seine DB-/Keyring-Kompatibilität
  prüfen. Restore nur mit gesondertem Datenverlust-/Zielfreigabeentscheid. Prozessabbruch
  rollt bereits ausgeführte TT-Ticketaktionen nicht automatisch zurück.
- Erst nach verifizierter Liveabnahme ist der Auftrag „online“; Registrypublication
  allein ist kein Deployment. Rollouts und Zielmerges ausschließlich über
  „Digitas-Demo dringend reparieren“, Develop-/Demo-Holds bleiben unverändert.
- Danach temporären Branch/Worktree und dessen reduzierte ci.yml aufräumen;
  niemals in einen langlebigen Branch integrieren. Ein dauerhafter Demo-Releaseweg
  wird separat als kleine additive, reviewte Lösung entschieden.

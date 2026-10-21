# Manueller Demo-Bootstrap und Wiederanlauf

## Geltungsbereich und tatsächlicher Stand

Dieser Vertrag gehört zur lokalen Komposition von TT-Integration PR #381
(`36a34065`) und stdin-Bootstrap PR #383 (`4b888d50`). Es gibt **keine** neue
Produktionsinstallation, automatische Secretversorgung oder automatische
Neustartfunktion. Bestehende Installationen ohne `--secrets-stdin` bleiben
unverändert. Ein späterer privater Lauf benötigt einen eigenen seriellen
Demo-Slot mit exakten Images, Ressourcen und nachgewiesenem Secretkanal.

Der isolierte synthetische Linux/x64-Pilot `39482867524/a1` hat am 20.10.2026
vollständig bestanden, einschließlich eigenem Cleanup. Er prüfte die Quelle
von PR #383, **nicht diese Komposition**, Cool3/ARM64, echte Keycloak-/HTTPS-
Integration oder langlebige Formulare. Die drei vorherigen Pilotfehler und
unbekannten Cleanups bleiben historisch bestehen. Der numerische C#-PID-P3 ist
nicht behoben. `tests/runtime-bootstrap-smoke/` ist ein Wegwerf-Testfeeder;
dessen Initialisierung darf nicht auf persistente Demo-Daten angewendet werden.

## Kleiner manueller Ablauf

1. **Vorbereitung ohne Secrets:** Quelle/Tree/Eltern und tatsächliche
   ARM64-Runnable-/Native-/Runtimepins, eigener Host/App/Netz/Volumes,
   Ressourcenobergrenzen, Zugriff und keine kollidierenden Jobs frisch prüfen.
   Nicht die alte x64-Pilotimage-ID oder Migration20 wiederverwenden.
2. **Persistente PG einmalig initialisieren:** ausschließlich leeres, exakt
   eigenes Volume. Vorhandene Daten bleiben erhalten; kein erneutes `initdb`,
   `down -v`, Reset, Backup oder Rollen-Overwrite als Fehlerbehandlung.
   Rollen-/Schemahoheit bleibt bei `deploy/postgresql/01…` und `02…`:
   Runtime kein DDL/Superuser/CreateRole/CreateDB, Historie nur lesbar;
   Migration über die getrennte Migrationsrolle. Die operativen PG-/STDIN-Adapter sind inzwischen als Source vorhanden;
   private Erststarts benötigen weiterhin ihre eigene tatsächliche Rootbindung.
3. **Explizit migrieren:** Der eigene Prozess erhält genau
   `--migrate --secrets-stdin` und ein frisches rollenpassendes JSONpaket über
   den geprüften stdin-Kanal, danach EOF. Die DDL-Verbindung erreicht nur diesen
   Prozess. API/Console starten erst nach tatsächlichem Exit0 und dem exakt
   erwarteten eingebetteten Migrationshead; keine Migration beim APIstart.
4. **API beaufsichtigt starten:** eigener Prozess mit exakt `--secrets-stdin`;
   runtimeConnectionString, bffClientSecret und directoryClientSecret kommen
   ausschließlich per frischer stdin-Lieferung. Keine Migrationsidentität im
   Paket. Nichtgeheime Auth-/Audience-/Rollen-/Proxy-/Directorykonfiguration
   bleibt strikt; Outbound-/KI-/Mail-/Kalendereffekte bleiben aus.
5. **Wiederanlauf ausdrücklich manuell:** `restart=no` für den künftigen
   stdin-API-/Migratorpfad; keine Docker-/Coolify-Neustartschleife mit leerem
   stdin und kein gespeichertes Secret als Ersatz. Nach terminalem eigenem
   Prozess erst Eigentum/Zustand prüfen und ein neues vollständiges Paket
   liefern. Fehlt die Lieferung oder EOF, bleibt die API geschlossen. Bereits
   bestätigte Migrationen und PG-/Keyringdaten bleiben erhalten. Ein
   Prozessabbruch ist weder Datenrollback noch Erlaubnis für einen neuen Start.

Die vorhandene native Coolify-Composekonfiguration ist **kein** ausführbarer
Träger dieses Vertrags: Sie verwendet weiterhin ENV-Secrets und automatische
API-Neustarts. Sie wird hier nicht still umgestellt. Der belegte native
Coolify-Fehlerpfad ist unsicher; echte Secrets dürfen diesen Pfad nicht nehmen.

## Secret- und Fehlerkanal

Der Bediener löst nur die dokumentierten Demo-Referenzen zur Laufzeit im RAM
auf. Kein Secret in ENV, argv, Datei, Shellsubstitution, Here-Doc, Base64kommando,
Coolify-ENV, Log, Browserstorage oder Checkpoint. STDIN ist kein Freibrief: Ein
späterer Docker-/SSH-Transport muss dessen tatsächliche Eingabe-/Ausgabewege,
EOF, Fristen, Prozesslebensdauer, Containerbesitz und Logtreiber selbst belegen.
Unbekannter Startausgang bedeutet STOP/UNKNOWN, nicht blindes Wiederholen.

Der Produkt-Bootstrap begrenzt Pakete/Frist/Schlüssel und lehnt konkurrierende
Konfigurationsprovider ab. Die API validiert Npgsql-Verbindungsstrings vor dem
DataSource-Aufbau: bei Syntaxfehlern nur fester Schlüsselhinweis **ohne** innere
Treiberexception. `--migrate` protokolliert ausschließlich feste Fehlerkategorien,
bekannte SQLSTATE-Kategorien und numerische Migrationsversion, keine freie
Exception/SQLdetails. Ein solcher Fehler kann Passwortfragmente enthalten (#375).
Die sichere Validierung ist kein Beweis für sämtliche nachfolgenden Logger,
Treiber oder Betriebssysteme; private Runtime bleibt bis Kanalabnahme geschlossen.

## Kleinste noch offene technische Entscheidung

Der dünne persistente [Caller](../deploy/tickytask-demo-coolify/supervised-operator/PERSISTENT.md)
ist als Source vorhanden. Seine reale private Root-/Topologie-/Secretbindung ist
noch **nicht ausgestellt**. Die beiden Wege bleiben klar getrennt:

- **Bevorzugt:** sehr kleiner manueller Operator über die vorhandenen gebundenen
  Docker-/SSH-stdin-/Attachkanäle, feste eigene Container und Zustände,
  `restart=no`. Kein ständig laufender Secretagent. Vor Umsetzung konkreten
  Transport und Verhalten bei Verbindungsverlust festlegen und synthetisch testen.
- **Nicht in diesem Durchstich:** automatischer Secretagent mit unbeaufsichtigtem
  Wiederanlauf. Das wäre zusätzliche Architektur und benötigt einen eigenen Auftrag.

Ohne geprüften ersten Weg erfolgt kein privater Start. Insbesondere wird weder
auf den unsicheren nativen Coolify-ENVpfad noch auf persistierte Secretdateien
zurückgefallen. Dieser offen benannte Rest ist kein fehlgeschlagenes Demo-Deploy.

## Implementierter Transport-/Sequenzkern, noch kein Installer

Die [manuelle Operatorquelle](../deploy/tickytask-demo-coolify/supervised-operator/README.md)
ergänzt nun begrenzte echte lokale Pipe-Fixtures und die einmalige Initial-/Restart-
Sequenz über injizierte Ports. Sie startet bei Import/CLI keine privaten Dienste.
Die konkreten SSH-/Docker-/PG-/SQL-/Readinessadapter sind inzwischen vorhanden.
Der isolierte synthetische ARM64-Initialversuch auf Operatorquelle `293640c`
(Appquelle `a7fcde7`) hat Head22/Pending0, FEEL und anonyme401 mit anschließendem
eigenem vollständigem Cleanup belegt. Er ist **kein** persistenter Demo-,
Keycloak-, HTTPS- oder TT-Nachweis und wird nicht erneut verwendet.
`manual_persistent.py` komponiert dieselben Originalports ohne Remote-Cleanup;
sein Import/CLI bleiben inert und seine Tests vollständig synthetisch. Die echte
persistente Runtimegrenze bleibt bis separater Rootöffnung geschlossen.

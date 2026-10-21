# Persistenter manueller Caller: vorhandene Ports, kein zweiter Installer

## Was diese Quelle tatsächlich ergänzt

`manual_persistent.py` verbindet ausschließlich den vorhandenen `ManualOperator`
mit `RuntimePorts`, `PgBootstrapAdapter`, deren unveränderten SQL-, Readiness-
und begrenzten STDIN-Transportbausteinen. Der synthetische ARM64-Pilot wird **nicht**
importiert, umkonfiguriert oder um seinen `finally`-Cleanup gebracht. Sein Go bleibt
verbraucht. Neue Bibliothek und CLI führen beim Import/normalen Start keinerlei
Außenaktion aus; auch `--execute` öffnet keinen Lauf.

Tests verwenden die vorhandenen Originalports mit einem synthetischen Dockerfake,
keine echten SSH-/Docker-/SQL-/Keycloakzugriffe. Das ist Source-/Kompositionsbeleg,
kein installierter Dienst und keine vollständige Demoabnahme.

## Kleiner expliziter Aufrufvertrag

Ein später separat physisch gebundener privater Rootcaller muss **alle** folgenden
Werte liefern. Es gibt keine persönlichen Defaults oder ENV-/Datei-Secretalternative:

- `PersistentBinding`: bestehende `PgBinding`, `RuntimeBinding`, `SshTarget`;
  beim Restart zusätzlich die vorher registrierte PG-CID und gegebenenfalls die
  terminale alte API-CID. Erstellungsnonce und Labels der drei Datenvolumes bleiben
  beim Restart unverändert; nur API-/Migratorversuch bekommt eine neue Nonce.
- `guard`: vorhandener äußerer Rootguard vor/nach jedem Originaltransport. Er
  bindet frisch Hostkey, exakte Sourcebytes, Frist, Images/UIDs, den exklusiven
  Demo-Ressourcenscope und vollständige tatsächliche Netz-/Peeridentitäten.
- `claim_once`: vorhandener privater dauerhafter exklusiver Versuch, der nur nach
  tatsächlichem Claim exakt `True` liefert. Ein neues Pythonobjekt oder ein Fehler
  öffnet diesen Claim nicht erneut. Der Caller stellt **kein** eigenes Permit aus.
- `pipe`: expliziter bereits gebundener Originaltransport, niemals still ein neuer
  Livekanal. `deliver` liefert erst nach positiver Originalvorprüfung einmal das
  passende `SecretBundle` im RAM. Auflösung bleibt im privaten Rootkontext.

Callbacks und Dataclasses sind vertrauenswürdige lokale Bibliotheksinputs, keine
Sandbox gegen bösartigen Same-UID-Code und keine Selbstautorisierung. Der spätere
Rootcaller muss seine tatsächlichen Rawbytes und unveränderliche Authority extern
binden. Die private Inert-Templatefläche liegt bewusst **außerhalb** des Gitbaums;
reale Root-/RAM-Secretbindungen sind dort NOT_ISSUED.

## Lebensdauer und Fehler

**Initial:** Nur der Originalport bestätigt vollständige Abwesenheit der eigenen
Ressourcen. Er erzeugt genau eigene Daten-/Socket-/Keyringvolumes, initialisiert PG
und Rollen einmalig, migriert explizit, prüft ACL/Head22/Pending0 und startet die API.
`READY` wird erst nach Original-Inspect/FEEL/anonymer401 gemeldet. Danach bleiben PG,
API und Volumes erhalten; `close` leert nur eigene RAMpuffer.

**Restart:** Vorher registrierte Volumes und PG-CID bleiben gebunden. Die alte API
muss fehlen oder bestätigt beendet sein; eine laufende alte API wird nicht gestoppt.
Kein initdb, Volume-create, Rollenbootstrap, Passwortwechsel oder Adoptionslauf.
Erfolgreiche eigene Initial-Hilfscontainer müssen **vorher separat** bestätigt unter
Rootkontrolle entfernt sein, wie es der unveränderte Runtimeport fordert. Dieser
Caller erledigt das nicht heimlich. Fremde/unklare Hilfscontainer sperren den Start.

**Fehler, Transportverlust, Interrupt, Closefehler:** `STOP_UNKNOWN`, keine Folge-
oder Wiederholungsaktion, kein automatischer Stop/Remove/Reset/Rollback. Der sichere
Resultatvertrag enthält nur feste Phase, bekannte unbestätigte CIDkandidaten und
gegebenenfalls eigenen lokalen UNKNOWN-PID. `PRESERVE_NO_REMOTE_CLEANUP` heißt
„keine entfernte Löschung versucht“, nicht „alle Ressourcen nachweislich gesund“.
Das Resultat speichert weder Secrets noch Rohfehler. Entfernte unklare Writer werden
nicht durch vermeintliches Cleanup überholt; keine Fremd-/Produktivprozesse beenden.

## Noch exakt fehlende reale Bindungen

Diese Source erfindet keine Werte und führt keine Vorabprobes aus. Vor Aktivierung
fehlen weiterhin die Rootbindung für den **neuen persistenten** Versuch, dedizierte
Ressourcenidentitäten, aktueller realer Proxy-/Console-CID-/Alias-/Routingvertrag und
vollständige tatsächliche Netzwerklabels/CIDR, reale Demo-BFF-/Directory-Clients mit
verifiziertem Keycloak-Demo-Wurzelgruppen-ID sowie die feste RAM-Lieferung aus den
zugelassenen Secretreferenzen. Öffentliche Origins sind Flowzer/TT-Demo, Issuer ist
exakt `https://auth.tickytask.de/realms/TickyTask`; diese Namen allein gewähren nichts.

Der bestehende Netzwerkguard wird nicht erweitert. Console/Proxy werden nicht als
neue Peers geraten oder automatisch eingerichtet. Coolify-ENV/Datei-Secrets,
automatische Restartschleifen, Backups/Dumps/Keyringkopien, Production und alle
unfreigegebenen Outboundeffekte bleiben geschlossen. HTTPS/realer IdP, persönliche
TT-Einbettung und mindestens45 Minuten Formularbearbeitung sind eigene echte
Abnahmen nach einem separat eröffneten Runtime-Slot, nicht Folge grüner Sourcetests.

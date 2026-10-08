# Persönlicher Read-only-Formulareinstieg — erster Integrations-Slice

Status: **API-Grundlage, keine fertige oder ausgerollte Einbettungsoberfläche.**
Die Renderer-/Sandbox-Abnahme, der gebundene Nachrichtenkanal und die echte
HTTPS-/Identity-Abnahme sind zwingende Folge-Gates. Das Installations-Opt-in
bleibt standardmäßig geschlossen und wird noch nicht in Compose aktiviert.

## Vertrag

- `POST /usertask/{id}/form-link`: authentifizierter, persönlich berechtigter
  Bearbeiter, Body `{ "hostOrigin": "https://host.example" }`.
- Antwort `url` hat einen zufälligen 256-Bit-Einstieg ausschließlich im Fragment
  von `/embed.html`, `redeemBeforeUtc` ist die **Einlösefrist**, keine Arbeitsfrist.
- `POST /form-embed/redeem`: anonymer, strikt lesender Snapshot, Body mit `secret`.
  Der isolierte Endpunkt akzeptiert `Origin: null`, aber keine CORS-Credentials.
  Kein Secret im Pfad, Queryparameter, Browserstorage, Zugriffstoken oder Host-DB.
- Einlösung wird atomar verbraucht. Auch bei aktuellem Operatorrecht darf kein
  persönlicher Einstieg für fremde Arbeit erstellt werden.
- Aufgabenbindung, Definition, Lifecycle-Revision und aktuelle aktive
  Directory-Identität werden bei Ausgabe **und Einlösung** geprüft. Der neue Pfad
  unterstützt bewusst nur stabile Directory-Zuweisungen, keinen Namensfallback.
- Der Snapshot enthält das gebundene, erneut kompilierte Formular, ausschließlich
  deklarierte Kontextfelder und den privaten Entwurf desselben Akteurs. Keine
  Instanzdiagnose, kein Token oder Mutationsrecht.
- Claim, Release, Entwurf, Directory und Abschluss bleiben unverändert
  authentifizierte API-Operationen. Ein Formularlink ist kein Bearer-Token.
  Eine zwischenzeitlich geänderte Aufgabenrevision erfordert einen neuen Einstieg.

## Lebenszeit

Fünf Minuten gelten nur für die noch nicht erfolgte Einlösung. Nach dem Bootstrap
vermittelt der Host alle Aktionen über seine aktuelle Benutzersitzung. Ein neuer
Zugriffstoken darf das Formular nicht remounten. Aktueller Rechteentzug, Release,
Abschluss oder Abbruch wird dennoch erzwungen; der Host muss den Frame entfernen.

Manuelles Speichern nutzt den bestehenden privaten Human-Task-Entwurf mit Revision.
Unvollständige Pflichtfelder sind dort zulässig; erst der Abschluss validiert sie.
Keine neue Entwurfsablage, kein Autosave und keine Startformular-Entwürfe.

## Konfiguration und Ablage

`FormEmbedding:Enabled=false` ist der Default. Für die spätere geprüfte Aktivierung
werden `PublicOrigin` und `AllowedHostOrigins` als exakte HTTPS-Origins benötigt
(keine Pfade, Wildcards, Queries oder Credentials).

Migration `021_form_embed_grants.sql` ergänzt eine leere PostgreSQL-Tabelle.
Persistiert werden nur SHA-256-Hash und stabile persönliche Bindung, kein Secret
oder Formularinhalt. `DELETE RETURNING` entscheidet die Einlösung zwischen
API-Prozessen, Aufgabenlöschung entfernt Freigaben per indiziertem FK. Erst die
Aufgabe, dann die Freigabe werden gesperrt; ein PostgreSQL-Service-Test erzwingt
einen parallelen Abschluss. Eine sperrfreie Hash-Vorprüfung hält zufällige anonyme
Anfragen aus der globalen Engine-Sperre heraus. Der Einlösepfad hat zusätzlich
eigene Endpoint-gebundene, geordnete Kontingente (standardmäßig zuerst 60 POSTs je
IPv4-Adresse bzw. IPv6-/64 und Minute, danach 600 je API-Prozess, keine Warteschlange),
auch bei deaktiviertem allgemeinem Limiter. Beide Limits sind über
`RedeemPerCallerPermitLimit`/`RedeemGlobalPermitLimit` konfigurierbar. Eine bereits
gedrosselte Quelle verbraucht das Globalbudget nicht weiter; Routingvarianten wie
`/redeem/` haben dieselben Metadaten. Dies ist kein verteiltes Gateway-Ratenlimit.
Hinter Proxys müssen `ForwardedHeaders:KnownNetworks`/`KnownProxies` sowie
`ForwardLimit` für die konkrete vertrauenswürdige Kette gesetzt sein; beliebige
Forwarded-Header sind niemals vertrauenswürdig. Ohne diesen Nachweis teilen alle
Quellen die Proxyadresse. Auch Firmen-NAT teilt eine IPv4-Partition; die Grenzen
sind für die erwartete Nutzung bewusst zu dimensionieren und im echten Gateway
zu prüfen. Ein verteiltes Botnetz oder eine große IPv6-Zuteilung mit mehreren
/64-Netzen kann das Globalbudget weiterhin erschöpfen. Dieses Restrisiko bleibt
ohne zusätzliche Gateway-Abwehr ausdrücklich bestehen; kein zweiter verteilter
Limiter wird für den Demo-Durchstich gebaut.

Höchstens vier noch nicht eingelöste Links bleiben je Aufgabe bestehen; weitere
Ausgaben verdrängen die ältesten Links, niemals ein bereits geöffnetes Formular.
Diese Begrenzung ist bewusst aufgabenweit, nicht persönlich; zwischen Übergabe und
Einlösung kann deshalb ein späterer berechtigter Besitzer einen alten Link verdrängen.
Der Host muss zuerst atomar übernehmen und erst danach den persönlichen Link anfordern.
Abgelaufene Personenbindungen werden minütlich in begrenzten Batches sowie vor
neuen Ausgaben außerhalb aller Engine-/Aufgabensperren bereinigt. Die Dateiablage
bleibt Einzelprozess-Entwicklung mit atomarer Entnahme und kollisionsfreiem
Neuanlegen. Anders als PostgreSQL kann sie bei einem anschließenden internen
Fehler keinen Verbrauch zurückrollen; der Host muss dann einen neuen Link holen.

## Nachweisgrenzen

HTTP-TDD deckt Fremdakteure, Replay, Ablauf, unvollständige Entwürfe, Wiederaufnahme,
Definition-/Lifecycle-Revisionen, Directory-Entzug/Abbruch, Operator-Fremdzugriff,
Lastkontingent und isoliertes CORS einschließlich JSON-Preflight ab. Ein entzogener
Keycloak-Basisrollenclaim wird an dieser anonymen Grenze nicht live introspektiert:
Die maximal fünf Minuten gültige Anzeige-Freigabe prüft das aktuelle Directory und
die Aufgabenbindung erneut, Mutationen benötigen immer die aktuelle Anmeldung.
Der virtuelle 46-Minuten-Test
belegt nur, dass die Einlösefrist die vorhandenen Benutzeroperationen nicht sperrt;
er ersetzt weder reale Token-Erneuerung noch einen echten 45-Minuten-Browserlauf.
PostgreSQL-Tests prüfen konkurrierende Sessions, Transaktionsrollback und FK-Cleanup.

Der Sandbox-Probe verwendet die bestehende Renderer-Komponente mit eigenem
Offline-Runtime-Setup und gesperrten Console-API-Adaptern, `allow-scripts` ohne
`allow-same-origin` und CSP ohne `unsafe-eval`. Das separate Setup entfernt die
unnötigen Form.io-Cookie-/Storage-Identitätsleser, deaktiviert Skriptauswertung und
bündelt die bereits verwendeten Kalender-/Zeitzonenversionen lokal. Die normale
Console bleibt unverändert. Das öffentliche statische Probe-Stylesheet erlaubt
`Origin: null` ohne Credentials für CSSOM; keine API- oder Session-CORS-Lockerung.
Textarea-Editoren/WYSIWYG und Kalender-Shortcut-Plugins sind im Embed-Profil derzeit
geschlossen: Die API erstellt für solche Formulare keinen Link. Der isolierte
SDK-Nachlader weist alle nicht gebündelten Libraries sofort zurück statt endlos zu
pollen. Die normale Console und der allgemeine Formularvertrag werden nicht beschränkt.

`node tests/form-embedding/run-sandbox-probe.cjs` baut immer frisch und läuft auch
in der CI. Der Browser prüft Text/Datum, editierbaren lokalen Zwischenstand, echten
Kalender, geladene Styles und fehlende CSP-/Konsolenfehler sowie tatsächlich
gesperrte Cookies/Storage. Directory-Auswahl, Formularabschnitte, Hostkanal und
Actions sind noch **kein** Bestandteil dieser Renderer-Probe und bleiben offen.
Das Probe-Bundle ist ein klassisches Einzeldatei-IIFE mit bewusstem statischem
CSS-CORS. Der normale Console-Auslieferungspfad besitzt diese Bedingungen noch
nicht; `/embed.html` würde dort derzeit im nicht einbettbaren SPA-Fallback landen.
Ein späterer separater Embed-Build und seine HTTPS-/CSP-/Asset-Auslieferung müssen
genau gegen das reale Containerartefakt abgenommen werden, bevor das Opt-in öffnet.
Ein fehlgeschlagener Probe darf nicht durch eine lockere Sandbox repariert werden.
Kein Demo- oder Produktivdeployment ohne abgeschlossene Folge-Gates.

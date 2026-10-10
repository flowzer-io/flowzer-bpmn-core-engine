# TT-Demo: feste Ticketaktionen

Separates BPMN-Beispiel für **Anlegen → ausdrücklich Lesen → Delegieren → Abschließen**.
Der [Urlaubsantrag](../tickytask-urlaub/README.md) bleibt davon getrennt und enthält keine Ticket-Worker.
Keine Mail-, Kalender-, KI-, Timer- oder Urlaubsbuchungswirkung. Diese Quelldateien aktivieren
keinen Worker, importieren keine Definition und verändern weder Demo noch Keycloak.

## Modell und geschlossener Vertrag

Original: [`ticketaktionen.bpmn`](ticketaktionen.bpmn), Definitionskennung
`tickytask-demo-ticketaktionen`, Prozess `Process_TickyTaskTicketActions`.
Die GUID der **tatsächlich veröffentlichten Fassung** muss zusätzlich in der reviewten
TT-Policy gebunden werden. Der lesbare Definitionsname allein ist keine Freigabe.

| Fester Knoten | Jobtyp | Eingänge | Ausdrücklich projizierte Ergebnisse |
|---|---|---|---|
| `Task_CreateTicket` | `tt.ticket.create` | `operation = "execute"` | `ticketReference` → `createdTicketReference` |
| `Task_ReadTicket` | `tt.ticket.read` | Marker und `createdTicketReference` → `ticketReference` | `idWithChecksum` → `demoTicketId`, `state` → `demoTicketState` |
| `Task_DelegateTicket` | `tt.ticket.delegate` | Marker und dieselbe Quittung | `completed` → `demoTicketDelegated` |
| `Task_CloseTicket` | `tt.ticket.close` | Marker und dieselbe Quittung | `completed` → `demoTicketClosed` |

`ticketReference` ist ein **opaker serverseitiger Anlagebeleg**, keine Roh-Ticket-ID oder
Berechtigung. Die drei Folgeschritte sind in der TT-Policy an `Task_CreateTicket`, denselben
TT-Vorgang, dieselbe Instanz und Fassung gebunden. `idWithChecksum` und `state` sind die
numerischen TT-Vertragswerte; der Status ist kein Workflowstatus. Die erfolgreiche Delegation
liefert wie der Abschluss **`completed: true`**, kein neues `delegated`-Transportfeld.

Kunde, Betreff, Anlagegruppe sowie feste Delegationsgruppe/optionaler TT-Kontakt stammen
nur aus der TT-Policy, nie aus Start- oder Jobdaten. Die Leseprojektion muss in TT exakt
`IdWithChecksum` und `State` freigeben; keine automatische Übernahme zusätzlicher Ticketfelder.
Die TT-Policy-Vorlage liegt im TickyTask-Repository unter
`Dev/TT-Next/ops/flowzer-demo/ticket-actions.policy.template.json` und ist mit ungelösten
Platzhaltern absichtlich **nicht aktivierbar**. Kein allgemeiner Import-/Grantmechanismus.

## Voraussetzungen vor einer tatsächlichen Demo-Ausführung

1. Beide Quellstände unabhängig prüfen und ihre SHAs sowie den BPMN-Dateihash festhalten.
2. Im bestätigten Koordinationsslot die Originaldefinition kontrolliert veröffentlichen;
   den tatsächlichen unveränderlichen Versionsbeleg lesen. Jede neue Fassung braucht eine neue Policyprüfung.
3. Nur verifizierte synthetische Demo-Kunden-/Gruppen-/Kontaktwerte in die geschlossene
   TT-Policy übernehmen. TT-Zuständigkeiten sind **keine Keycloak-Gruppen/Subjects**.
   Die Realm-/Demo-Wurzel-/Personalgruppen-Zuordnung wird separat direkt mit Christian geklärt.
4. Eine echte persönliche TT-Startregistrierung erzeugen. Eine unmittelbar in Flowzer
   gestartete Instanz oder mitgegebene Ticket-ID genügt dem TT-Worker nicht.
5. Bestehende Host- und DB-Jobgates ausschließlich nach den SHA-/Schema-/Backup-/HTTPS-
   und Betriebsfreigaben über den koordinierten Pfad öffnen. Keine neue Admin-Maske.
6. Aktuelle Flowzer-/TT-Rechte des Initiators für **jeden** Schritt prüfen; Zugangsverlust
   oder unklare Zugangsprüfung hält auch Lesen an. Eine Wiederfreigabe braucht den
   vorhandenen auditierten Vorgangspfad mit TT-Admin- und nachgewiesenem Flowzer-Betriebsrecht.

Abbruch beendet ausstehende Engineaufträge, bedeutet aber **keinen Rollback bereits
committeter Tickets/Delegationen/Abschlüsse**. Derselbe Job darf wegen des dauerhaften
atomaren TT-Effektbelegs auch nach Worker-Wiederholung keine zweite Ticketwirkung auslösen.
Diese Eigenschaften sind vor Livegang separat gegen die wirklichen TT-Worker-/SQL-Pfade
abzunehmen; das BPMN-Modell umgeht oder ersetzt sie nicht.

## Tests und Nachweisgrenzen

`TickyTaskTicketActionsExampleTest` liest diese **Originaldatei** direkt und verwendet die
wirkliche Kernengine/den wirklichen Modellparser. Acht Fälle prüfen die vier exakten Ein-/
Ausgangsprojektionen, die geschlossene Aktionsmenge, die Reihenfolge, tatsächliche Engine-
Token-Akteurablage und Engineabbruch vor/nach synthetischer Anlage. Bewusst eingebrachte
Root-/Worker-Fremddaten dürfen nicht in Jobs bzw. Ergebnisprojektionen gelangen.

Die Workerresultate sind dabei **synthetisch**: kein Nachweis tatsächlicher Ticketanlage,
SQL-Races, Transportautorisierung, HTTPS, Keycloak, Ressourcenbudget oder TT-Browserbetrieb.
TT prüft die separate Original-Policy-Vorlage mit seinem wirklichen Parser/Katalog. Keine
Timerimplementierung oder Schemaänderung in diesem Beispiel.

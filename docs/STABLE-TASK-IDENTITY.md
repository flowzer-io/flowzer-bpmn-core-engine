# Stabile Aufgabenidentität

M0/M3-Slice #184 / PR #185, aufbauend auf #183.

## Vertrag

Die tatsächlich aktive Aufgabe wird über ihr Token innerhalb einer Instanz
identifiziert. `SaveUserTasks` aktualisiert ihre Subscription unter **derselben ID**,
statt bei jeder Instanzänderung alle Aufgaben zu löschen und neu anzulegen.

- Neue Tokens erhalten neue Aufgaben-IDs. Entscheidend ist nicht der BPMN-Knotenname:
  Mehrere Token desselben Knotens dürfen eigenständige Aufgaben sein.
- Nur nicht mehr aktive Aufgaben werden gezielt entfernt (Abschluss oder Abbruch).
- Name und Tokenkontext werden aktualisiert; ID, gespeicherte Textzuweisung,
  Kandidaten und vorhandene Bearbeitermetadaten bleiben erhalten.
- Das gilt nach parallelem Fortschritt, Timerfortschritt und dem Laden mit einer
  neuen Engine-Instanz. Eine noch aktive Aufgabe bleibt über ihren bisherigen
  Formular-/Aufgabenlink erreichbar.
- `IMessageSubscriptionStorage.AddUserTaskSubscription` verlangt ausdrücklich
  **Upsert nach ID**. Dateiablage und PostgreSQL erfüllten dies bereits; ein eigener
  Storage-Adapter muss denselben Vertrag implementieren.

## Fehlerhafte Bestände und Upgrade

Doppelte Token-/Aufgaben-IDs, falsche Instanz-/Definitions-/Prozesszuordnungen oder
widersprüchliche Knoten-/Tokenherkunft werden vor den Aufgaben-Writes abgelehnt.
Solche Bestände dürfen nicht still zusammengeführt werden: Verschiedene Einträge
könnten unterschiedliche Bearbeitungszustände enthalten. Der Betrieb muss sie
kontrolliert klären; es gibt in diesem Slice noch keine Reparaturoberfläche.
Der Konsistenzfehler wird serverseitig protokolliert, nicht als fremde Ressource offengelegt.

Bestehende eindeutige IDs bleiben beim ersten neuen Speichervorgang erhalten.
Es ist weder eine Massenumschreibung noch eine Datenbankschemaänderung nötig.
Vor Upgrade trotzdem Bestände und laufende Instanzen in einer Testinstallation prüfen.
Keine produktive Migration oder automatische Text-zu-Verzeichnis-Konvertierung.

## Grenzen und Verifikation

Dies ist die Grundlage für Claims/Entwürfe, **keine neue Claim-/Draft-/Delegations-API**.
Insbesondere die Erhaltung von `CurrenAssignedUser` aktiviert noch keine exklusive
Bearbeiterberechtigung; diese muss im separaten Human-Task-Vertrag implementiert werden.
Textzuweisungen bleiben erhalten, die explizite Verzeichnis-/Text-Moduswahl folgt M1.

Der Zyklus nutzt die bestehende prozesslokale Engine-Sperre. PostgreSQL hält die
Mutation transaktional; eine neue Datenbank-Unique-Constraint je Token, Revisionen
und echte Mehrprozess-Konkurrenztests sind weiterhin erforderlich. Dateiablage
besitzt keinen Rollback: Andere Subscriptiontypen können bei späteren Fehlern
bereits verändert sein. Keine allgemeine Mehrprozess- oder Produktionsfreigabe.

Identische Szenarien laufen gegen isolierte Dateiablage und echtes PostgreSQL:
paralleler Fortschritt mit Metadaten-/Kontexterhalt, neue Engine nach Persistierung,
Timer, Abschluss, instanzbezogener Abbruch sowie doppelte/inkonsistente Bestände.
Externe Reviews sind für diesen autonomen Lauf ausdrücklich ausgesetzt; TDD und CI nicht.

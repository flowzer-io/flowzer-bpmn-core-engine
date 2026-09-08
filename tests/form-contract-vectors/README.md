# Gemeinsame Formularvertragsvektoren

`manifest.json` ist der versionierte, gemeinsame Prüfbestand für
`flowzer.forms/1`, `flowzer.forms/2`, `flowzer.forms/3` und das additive
`flowzer.forms/4` für explizite Human-Task-Entscheidungsaktionen.

- `.NET`: `FormContractVectorTest`
- React/Vitest: `formContractVectors.test.ts`

Jeder Fall besitzt eine stabile `id`, eine deutsche `purpose`, `profile`, `schema`
und die erwartete Ausgabe beziehungsweise kanonische Fehlercodes.

`comparison: "client-server"` bedeutet, dass die nebenwirkungsfreie Browser-
Vorprüfung denselben Ausgang liefern muss. `server-authoritative` ist für Regeln,
die aktuelle Verzeichnisdaten oder eine benannte Serverberechnung benötigen. Der
Browser darf diese Fälle nicht als autoritativ erfolgreich bewerten. Aktionsvektoren
führen zusätzlich `actionId` und `allowActions`, damit der .NET-Lauf die tatsächliche
Human-Task-Grenze prüft; der Browser liest daraus nur Anzeige und Vorprüfung.

`schemaPaddingLength` erzeugt im jeweiligen Testprozess ein großes gültiges Schema,
ohne mehr als ein MiB redundante Testdaten ins Repository einzuchecken. Neue
Profilfunktionen werden zuerst als gemeinsame rote Vektoren ergänzt und anschließend
in beiden dafür zuständigen Prüfern umgesetzt. Secrets, echte Personen- oder
Kundendaten gehören nicht in diesen Katalog.

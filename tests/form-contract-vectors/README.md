# Gemeinsame Formularvertragsvektoren

`manifest.json` ist der versionierte, gemeinsame Prüfbestand für
`flowzer.forms/1` und `flowzer.forms/2`.

- `.NET`: `FormContractVectorTest`
- React/Vitest: `formContractVectors.test.ts`

Jeder Fall besitzt eine stabile `id`, eine deutsche `purpose`, `profile`, `schema`
und die erwartete Ausgabe beziehungsweise kanonische Fehlercodes.

`comparison: "client-server"` bedeutet, dass die nebenwirkungsfreie Browser-
Vorprüfung denselben Ausgang liefern muss. `server-authoritative` ist für Regeln,
die aktuelle Verzeichnisdaten oder eine benannte Serverberechnung benötigen. Der
Browser darf diese Fälle nicht als autoritativ erfolgreich bewerten.

`schemaPaddingLength` erzeugt im jeweiligen Testprozess ein großes gültiges Schema,
ohne mehr als ein MiB redundante Testdaten ins Repository einzuchecken. Neue
Profilfunktionen werden zuerst als gemeinsame rote Vektoren ergänzt und anschließend
in beiden dafür zuständigen Prüfern umgesetzt. Secrets, echte Personen- oder
Kundendaten gehören nicht in diesen Katalog.

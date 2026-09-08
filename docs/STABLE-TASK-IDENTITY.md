# Stabile Aufgabenidentität

Geplanter M0/M3-Slice zu #184, aufbauend auf #183.

Die fachliche Aufgabe wird durch ihr tatsächlich aktives Token innerhalb einer
Instanz identifiziert. Eine erneute Persistierung aktualisiert ihre Subscription
unter derselben ID. Nur neue Tokens erhalten neue Aufgaben-IDs. Erledigte oder
abgebrochene Aufgaben werden gezielt entfernt. Gespeicherte Zuweisungen werden
nicht aus einem neuen Modell- oder Tokenstand rekonstruiert.

Mehrdeutige/inkonsistente Bestände brauchen Klärung; sie dürfen nicht automatisch
zusammengeführt werden. Das ist eine Grundlage für spätere Claims/Entwürfe, keine
Einführung dieser Aktionen oder Freigabe für mehrere API-Prozesse. Textzuweisungen
bleiben unverändert erhalten.

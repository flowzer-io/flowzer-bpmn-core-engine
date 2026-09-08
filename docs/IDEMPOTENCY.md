# HTTP-Idempotenz

Geplanter M0-Slice #186. Direkte Starts und Aufgabenabschlüsse erhalten einen
persistenten Wiederholungsvertrag über `Idempotency-Key`. Identität, Inhalt und
Ziel werden serverseitig gebunden; abweichender Inhalt darf nicht erneut mutieren.
Details und gemessene Grenzen werden mit der Implementierung ergänzt.

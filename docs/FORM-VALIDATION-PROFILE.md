# Serverseitiges Formular-Prüfprofil

M0/M2-P0-Folgepaket, aufbauend auf #180 / PR #181.

Das erste Profil `flowzer.forms/1` definiert bewusst eine begrenzte, serverseitig
prüfbare Teilmenge von Form.io. Es ist keine Behauptung vollständiger Form.io-
Kompatibilität. Nicht unterstützte Feldtypen oder Geschäftsregeln werden beim
Deployment abgelehnt, statt sie ausschließlich dem Browser zu überlassen.

## Vorgesehener Umfang

- Skalare Text-/Zahl-/Bool-/Datumsfelder, statische Einfach-/Mehrfachauswahl,
  Layoutgruppen, Pflichtwerte, Typ-/Bereichsregeln und einfache deklarative Bedingungen.
- Nicht deklarierte Ergebnisfelder werden abgelehnt. Nur lesbarer Kontext kann
  angezeigt werden, aber keine Prozessvariablen über Submissiondaten überschreiben.
- Keine Ausführung von Formular-JavaScript, Custom-Conditions oder dynamischen
  Datenquellen als alleiniger Geschäftsregel. Weitere Verträge folgen separat.
- Gleicher Validator für direkte HTTP-Starts sowie beide Abschlussrouten; erst
  Berechtigung und Aufgabenidentität prüfen, dann Eingaben, anschließend mutieren.
- Feldbezogene Fehlercodes über Problem Details, ohne Echo von Eingabewerten;
  kompatible Fehlerangaben für vorhandene Clients.

## Noch nicht abgeschlossen

Wiederholbare Gruppen, Verzeichnisreferenzen, vollständige Custom-Skript-Migration,
Entwurfsverwaltung sowie gemeinsame Client-/Server-Konformitätsvektoren gehören in
eigene Folgepakete. Der Server bleibt verbindlich, auch ohne Browservalidierung.

## Upgrade

Neue Deployments müssen den Vertrag erfüllen. Historische externe Referenzen ohne
belegten Formularstand benötigen bereits seit dem Bindungsslice ausdrückliche
Klärung. Nicht prüfbare historische Regeln dürfen nicht still durchgelassen werden.
Keine produktive Migration und keine automatische Veröffentlichung im autonomen Lauf.

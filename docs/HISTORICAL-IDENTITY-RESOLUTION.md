# Historische Identitätsreferenzen

Issue #234 ergänzt die aktive, kontextgebundene Verzeichnissuche um einen getrennten
Anzeigevertrag. Eine bereits gespeicherte `SubjectRef` kann damit nach Deaktivierung oder
Löschung weiterhin verständlich beschriftet werden, ohne wieder auswählbar zu werden.

## Getrennter Vertrag

Die Suche liefert ausschließlich aktuell aktive und nach heutiger Policy auswählbare
Identitäten. Die Anzeigeauflösung verwendet `POST`, akzeptiert ein Array von einer bis
höchstens 50 eindeutigen typisierten Referenzen und antwortet mit:

- `generationId` des vollständig veröffentlichten Directory-Snapshots,
- `displayName` und dem eindeutigen Zusatz (`subject` beziehungsweise Gruppenpfad),
- `isActive` für den heutigen Directory-Status,
- `isSelectable` für die heutige Auswahlpolicy des konkreten Kontexts.

Ein historischer Treffer hat stets `isSelectable: false`, sobald er deaktiviert oder
heute durch die Policy ausgeschlossen ist. Submission, Aufgabenübertragung und
Rechteauswertung verwenden weiterhin die strikte aktive Auflösung und lehnen ihn ab.

## Kontext ist Teil des Leserechts

Eine bekannte UUID allein ist kein Verzeichnis-Leserecht. Der Server projiziert nur
Referenzen, die bereits in dem autorisierten fachlichen Kontext gespeichert sind:

- Workflow-Modellierung: Referenzen der zuletzt gespeicherten BPMN-Version,
- Ordnerpflege: direkte typisierte Zuweisungen dieses Ordners,
- gebundenes Startformular: bei Veröffentlichung geprüfte explizite Feldfilter,
- gebundenes Aufgabenformular: geprüfte Feldfilter und persistierte Werte des aktiven
  Task-Tokenkontexts,
- Lifecycle: modellierte Task-Zuweisungen sowie tatsächlicher und protokollierter
  Bearbeiter dieses Tasks.

Nicht gebundene, manipulierte oder unbekannte IDs bleiben ohne Treffer. Das ist bewusst
kein Fehler mit unterschiedlicher Detailmeldung, damit der Endpunkt nicht als
Directory-Orakel verwendet werden kann. Fremde oder nicht bearbeitbare Ressourcen
antworten wie unbekannte Ressourcen mit `404`.

Private, nur auf ihre Form beschränkte Entwürfe werden nicht nachträglich zum
Directory-Lesebeleg. Ein frisch ausgewählter aktiver Treffer behält seine Projektion im
lokalen, kontextgebundenen UI-Cache; wird eine Entwurfsreferenz vor ihrer Übernahme in den
Prozesskontext deaktiviert, bleibt sie als nicht auflösbare Warnung sichtbar und kann
nicht abgeschlossen werden.

## Endpunkte

- `POST /identity-directory/workflows/{definitionId}/subjects/resolve`
- `POST /identity-directory/folders/{folderId}/subjects/resolve`
- `POST /identity-directory/start-forms/{definitionId}/fields/{fieldKey}/subjects/resolve`
- `POST /identity-directory/user-tasks/{taskId}/fields/{fieldKey}/subjects/resolve`
- `POST /identity-directory/user-tasks/{taskId}/assignees/resolve?action=assign|delegate`

Der Request enthält ausschließlich `subjects: [{ "kind": "user|group", "id": "..." }]`.
Anzeigenamen, E-Mail-Adressen oder Legacy-Freitext werden nie automatisch in eine
Verzeichnisreferenz umgewandelt. Gleichnamige Gruppen bleiben durch stabile ID und vollen
Pfad unterscheidbar; frühere Mitgliedschaften werden nicht rekonstruiert.

SDK und React-Schicht bieten denselben gebundenen Batch-Vertrag. Sie kennen weder eine
globale Historienliste noch eine konkrete konsumierende Anwendung.

/** Verständliche Handlungsanweisungen; technische Codes bleiben für Support erhalten. */
const MESSAGES: Record<string, string> = {
  'bpmn.service_task.implementation_required': 'Für diese Service-Aufgabe fehlt der Worker-Typ. Wähle die Aufgabe an und trage rechts den zuständigen Dienst ein – oder ändere den Aufgabentyp.',
  'bpmn.user_task.form_missing': 'Diese menschliche Aufgabe hat kein Formular. Sie wird dann ohne Eingaben nur bestätigt. Soll etwas ausgefüllt werden, wähle rechts unter „Formular“ ein veröffentlichtes Formular aus.',
  'bpmn.ai_task.connection_invalid': 'Für diese KI-Aufgabe fehlt eine gültige Verbindung. Wähle rechts eine eingerichtete KI-Verbindung aus.',
  'bpmn.ai_task.connection_not_found': 'Die gewählte KI-Verbindung existiert nicht mehr. Bitte eine vorhandene Verbindung auswählen.',
  'bpmn.ai_task.connection_disabled': 'Die gewählte KI-Verbindung ist deaktiviert. Bitte eine freigegebene Verbindung auswählen.',
  'bpmn.ai_task.connection_not_ready': 'Die KI-Verbindung ist noch nicht vollständig eingerichtet. Prüfe ihre Konfiguration im Bereich „KI-Verbindungen“.',
  'bpmn.ai_task.input_mapping_required': 'Der KI-Aufgabe fehlen Eingaben. Ordne unter „Zuordnungen“ die benötigten Prozessdaten zu.',
  'bpmn.ai_task.output_mapping_required': 'Der KI-Aufgabe fehlt die Ausgabezuordnung. Lege fest, in welcher Prozessvariable das Ergebnis gespeichert wird.',
  'bpmn.ai_task.result_schema_invalid': 'Das Ergebnisschema der KI-Aufgabe ist kein gültiges JSON. Bitte das Schema rechts korrigieren.',
  'bpmn.ai_task.result_schema_object_required': 'Das KI-Ergebnisschema muss ein JSON-Objekt beschreiben.',
  'bpmn.ai_task.tools_runtime_unavailable': 'KI-Werkzeuge sind noch nicht ausführbar. Der Entwurf kann gespeichert werden; zum Veröffentlichen die Werkzeugaktionen entfernen.',
  'bpmn.ai_task.limit_invalid': 'Ein Ausführungslimit der KI-Aufgabe liegt außerhalb des erlaubten Bereichs. Bitte die Grenzen rechts prüfen.',
  'bpmn.element.not_executable': 'Diesen BPMN-Typ kann Flowzer noch nicht ausführen. Der Entwurf bleibt speicherbar; zum Veröffentlichen einen unterstützten Typ verwenden.',
  'bpmn.element.unsupported': 'Dieser BPMN-Typ wird noch nicht unterstützt. Bitte für die Veröffentlichung einen unterstützten Typ verwenden.',
  'bpmn.element.id_required': 'Diesem Element fehlt eine eindeutige Kennung.',
  'bpmn.element.id_duplicate': 'Diese Elementkennung wird mehrfach verwendet. Bitte jedem Element eine eigene Kennung geben.',
  'bpmn.flow_node.unreachable': 'Dieser Schritt ist vom Start aus nicht erreichbar. Verbinde ihn mit dem Ablauf oder entferne ihn.',
  'bpmn.sequence_flow.invalid_reference': 'Eine Verbindung zeigt auf einen fehlenden Schritt. Verbinde ihre beiden Enden mit vorhandenen Elementen.',
  'bpmn.exclusive_gateway.condition_required': 'An dieser Entscheidung fehlt eine Bedingung oder ein Standardweg. Lege für jeden ausgehenden Weg fest, wann er genommen wird.',
  'bpmn.exclusive_gateway.default.invalid_reference': 'Der Standardweg dieser Entscheidung verweist nicht auf eine passende ausgehende Verbindung.',
  'bpmn.inclusive_gateway.condition_required': 'An diesem inklusiven Tor fehlt eine Bedingung. Gib jedem ausgehenden Weg außer dem Standardweg eine Bedingung — genommen werden alle Wege, deren Bedingung zutrifft.',
  'bpmn.inclusive_gateway.default.invalid_reference': 'Der Standardweg dieses inklusiven Tors zeigt auf keine seiner ausgehenden Verbindungen. Wähle einen Weg, der an diesem Tor beginnt.',
  'bpmn.event_based_gateway.invalid_target': 'Hinter diesem ereignisbasierten Tor steht ein Schritt, der nicht wartet. Lass jeden Weg zu einem Zwischenereignis mit Nachricht, Zeit oder Signal führen — oder zu einer Empfangsaufgabe.',
  'bpmn.event_based_gateway.outgoing_required': 'Diesem ereignisbasierten Tor fehlen Wege. Führe mindestens zwei ausgehende Verbindungen von ihm weg, sonst gibt es nichts zu entscheiden.',
  'bpmn.event_based_gateway.condition_not_allowed': 'An einem ereignisbasierten Tor entscheiden die Ereignisse. Entferne die Bedingungen der ausgehenden Wege und den Standardweg.',
  'bpmn.event_subprocess.start_required': 'Diesem Ereignis-Subprozess fehlt sein Auslöser. Gib ihm genau ein Startereignis mit Nachricht, Zeit, Signal, Fehler oder Eskalation.',
  'bpmn.start_event.event_subprocess_only': 'Ein Start mit Fehler oder Eskalation gehört in einen Ereignis-Subprozess. Verschiebe ihn dorthin oder wähle eine andere Art von Start.',
  'bpmn.error_boundary.cancel_activity_invalid': 'Ein Fehler-Boundary unterbricht laut BPMN immer. Entferne rechts am Ereignis die Einstellung „nicht unterbrechend“.',
  'bpmn.timer.definition_required': 'Für diesen Timer fehlt der Zeitpunkt, die Dauer oder der Zyklus. Trage die Zeitregel rechts ein.',
  'bpmn.process.executable_required': 'Kein ausführbarer Prozess ist festgelegt. Aktiviere beim Prozess die Ausführbarkeit.',
  'bpmn.xml.invalid': 'Das BPMN-Dokument ist beschädigt und kann nicht gelesen werden. Der vorhandene gespeicherte Stand bleibt erhalten.',
};

export function describeBpmnDiagnostic(code: string, fallback: string): string {
  return MESSAGES[code] ?? fallback;
}

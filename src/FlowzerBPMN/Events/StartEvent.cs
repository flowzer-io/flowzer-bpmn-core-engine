namespace BPMN.Events;

public record StartEvent : CatchEvent
{
    /// <summary>
    /// Der Verweis auf das Startformular — aus <c>zeebe:formDefinition/@formKey</c> (ersatzweise
    /// <c>@formId</c>) in den <c>extensionElements</c> des Startereignisses. Er folgt derselben
    /// Schreibweise wie am User-Task: Bestandsname, <c>Name:Version</c> oder
    /// <c>camunda-forms:bpmn:Kennung</c> für ein im Workflow eingebettetes Formular.
    ///
    /// Optional: Ein Startereignis ohne Formular bleibt gültig, der Workflow startet dann ohne
    /// Eingabe. Ausgewertet wird der Schlüssel nur am reinen Startereignis — an einem Timer-,
    /// Nachrichten- oder Signalstart gibt es niemanden, der das Formular ausfüllen könnte.
    /// </summary>
    public string? FlowzerFormKey { get; init; }

    /// <summary>
    /// Nur am Startereignis eines Event-Subprozesses: ob das Ereignis den umschliessenden Scope
    /// unterbricht. Entspricht <c>isInterrupting</c>; BPMN 2.0 setzt <c>true</c> voraus, wenn das
    /// Attribut fehlt. An einem gewoehnlichen Startereignis ist der Wert ohne Bedeutung.
    /// </summary>
    public bool FlowzerIsInterrupting { get; init; } = true;
}

using BPMN.Events;

// Alias, weil "Process" innerhalb von BPMN.Flowzer sonst den gleichnamigen Namensraum meint.
using ProcessModel = BPMN.Process.Process;

namespace BPMN.Flowzer;

/// <summary>
/// Das Startformular eines Prozesses: das Formular, das ausfüllt, wer den Workflow von Hand
/// startet. Seine Werte werden die Startvariablen der Instanz.
///
/// Die Ermittlung liegt bewusst an einer einzigen Stelle. Start, Formularabruf und Löschschutz
/// müssen dieselbe Antwort geben — läse eine Stelle einen anderen Schlüssel als die andere,
/// zeigte die Konsole ein Formular an, dessen Werte der Start nicht erwartet.
/// </summary>
public static class FlowzerStartForm
{
    /// <summary>
    /// Der Form-Key des Prozesses, oder eine Meldung, wenn er nicht eindeutig ist.
    /// Beides <c>null</c> heißt: Dieser Workflow hat kein Startformular.
    /// </summary>
    public sealed record Result(string? FormKey, string? ErrorMessage);

    /// <summary>
    /// Sucht den Form-Key an den reinen Startereignissen des Prozesses — also an denen ohne
    /// Timer-, Nachrichten- oder Signaldefinition, weil nur sie von Hand gestartet werden.
    ///
    /// Genau eines darf ein Formular tragen: Bei mehreren entschiede sonst die Reihenfolge im
    /// Diagramm, welches Formular gilt.
    /// </summary>
    public static Result FormKeyOf(ProcessModel process)
    {
        ArgumentNullException.ThrowIfNull(process);

        var withForm = process.FlowElements
            .OfType<StartEvent>()
            // Exakt der Typ: Timer-, Nachrichten- und Signalstarts erben von StartEvent.
            .Where(startEvent => startEvent.GetType() == typeof(StartEvent))
            .Where(startEvent => !string.IsNullOrWhiteSpace(startEvent.FlowzerFormKey))
            .ToArray();

        return withForm.Length switch
        {
            0 => new Result(null, null),
            1 => new Result(withForm[0].FlowzerFormKey!.Trim(), null),
            _ => new Result(null,
                $"The process \"{process.Id}\" has more than one start form ({string.Join(", ", withForm.Select(startEvent => startEvent.Id))}). Exactly one plain start event may carry a form.")
        };
    }
}

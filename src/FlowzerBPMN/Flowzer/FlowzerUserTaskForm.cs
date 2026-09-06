namespace BPMN.Flowzer;

/// <summary>
/// Ein Formular, das im Workflow selbst liegt statt im Formularbestand — im BPMN das Element
/// <c>zeebe:userTaskForm</c> in den <c>extensionElements</c> des Prozesses. Der User-Task
/// verweist ueber den Form-Key <see cref="FormKeyPrefix"/> + <see cref="Id"/> darauf.
///
/// Ein solches Formular ist mit dem Workflow versioniert: Eine neue Workflow-Version bringt
/// ihr eigenes Formular mit, laufende Instanzen behalten das ihre.
/// </summary>
/// <param name="Id">Die Kennung im Workflow, auf die der Form-Key zeigt.</param>
/// <param name="Schema">Das Formularschema als JSON — derselbe Inhalt wie in einem gespeicherten Formular.</param>
public record FlowzerUserTaskForm(string Id, string Schema)
{
    /// <summary>
    /// Praefix, mit dem ein Form-Key auf ein eingebettetes statt auf ein gespeichertes Formular
    /// zeigt. Bewusst Camundas Schreibweise: So laeuft ein im Camunda Modeler erstellter
    /// Workflow ohne Umbau, und die Konsole schreibt dieselbe Form.
    /// </summary>
    public const string FormKeyPrefix = "camunda-forms:bpmn:";

    /// <summary>
    /// Die Kennung aus einem Form-Key, oder <c>null</c>, wenn der Schluessel nicht auf ein
    /// eingebettetes Formular zeigt.
    /// </summary>
    public static string? IdFromFormKey(string? formKey) =>
        formKey is not null && formKey.StartsWith(FormKeyPrefix, StringComparison.Ordinal)
            ? formKey[FormKeyPrefix.Length..].Trim()
            : null;
}

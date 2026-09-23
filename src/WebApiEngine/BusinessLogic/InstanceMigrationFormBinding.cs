using Model;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Vergleicht die Formularbindung einer Aufgabe zwischen zwei Workflow-Versionen. Ein privater
/// Entwurf gehoert zum Schema, gegen das er entstanden ist: Nur wenn beide Versionen nachweislich
/// dasselbe Formular binden, darf er den Umzug ueberleben.
/// </summary>
internal static class InstanceMigrationFormBinding
{
    /// <summary>
    /// <c>true</c> nur bei belegter, gleicher Bindung. Eine fehlende Version, ein historischer
    /// Stand ohne Bindungen (<c>FormBindings is null</c>), ein unbekannter Schluessel oder ein
    /// abweichender Formularstand sind allesamt „nicht identisch" — im Zweifel wird verworfen
    /// statt ein Schema zu unterstellen.
    /// </summary>
    internal static bool IsIdentical(
        BpmnDefinition? source,
        BpmnDefinition? target,
        string? sourceFormKey,
        string? targetFormKey)
    {
        if (source?.FormBindings is not { } sourceBindings || target?.FormBindings is not { } targetBindings)
            return false;
        if (string.IsNullOrWhiteSpace(sourceFormKey) || string.IsNullOrWhiteSpace(targetFormKey))
            return false;

        return sourceBindings.TryGetValue(sourceFormKey.Trim(), out var boundInSource)
               && targetBindings.TryGetValue(targetFormKey.Trim(), out var boundInTarget)
               && boundInSource == boundInTarget;
    }
}

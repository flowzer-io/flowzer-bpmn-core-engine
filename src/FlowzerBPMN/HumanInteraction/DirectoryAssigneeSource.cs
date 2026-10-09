namespace BPMN.HumanInteraction;

/// <summary>Syntax des optionalen Flowzer-1.0-Bearbeiterquellenvertrags, ohne Ausführung.</summary>
public static class DirectoryAssigneeSource
{
    /// <summary>Beim Start verifizierte Identität, niemals eine Formularvariable.</summary>
    public const string Initiator = "initiator";
    /// <summary>Präfix eines einzelnen typisierten Benutzerwerts aus Prozessdaten.</summary>
    public const string VariablePrefix = "variable:";

    /// <summary>Erlaubt nur den Initiator oder einen einfachen ASCII-Namen (maximal 128 Zeichen).</summary>
    public static bool IsValid(string? source) => source == Initiator || TryGetVariableName(source, out _);

    /// <summary>Extrahiert einen einzelnen Variablenschlüssel, niemals einen Pfad oder Ausdruck.</summary>
    public static bool TryGetVariableName(string? source, out string name)
    {
        name = "";
        if (source is null || !source.StartsWith(VariablePrefix, StringComparison.Ordinal)) return false;
        var candidate = source[VariablePrefix.Length..];
        if (candidate.Length is < 1 or > 128
            || !(char.IsAsciiLetter(candidate[0]) || candidate[0] == '_')
            || candidate.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '_'))) return false;
        name = candidate;
        return true;
    }
}

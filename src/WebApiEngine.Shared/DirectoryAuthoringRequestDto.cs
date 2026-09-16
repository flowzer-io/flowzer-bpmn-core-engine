namespace WebApiEngine.Shared;

/// <summary>Nur im Modellierer-Kontext: optionaler lokaler Formularstand für die Vorschau.</summary>
public sealed class DirectoryAuthoringRequestDto
{
    public string? FormData { get; set; }
    public string? FieldKey { get; set; }
    public string? Query { get; set; }
    public string Kind { get; set; } = "all";
    public List<SubjectRefDto> Subjects { get; set; } = [];
}

namespace Model;

/// <summary>
/// Unveränderlicher Formularstand einer Workflow-Version. Eingebettete Formulare
/// haben keine externe ID/Version. Der Inhalt enthält Schema, niemals Submissiondaten.
/// </summary>
public sealed record BoundForm(Guid? Id, Guid? FormId, string? Version, string FormData, string? ValidationProfile = null);

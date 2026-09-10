namespace BPMN.Flowzer;

/// <summary>
/// Freigabestufe eines versionierten Werkzeugbezugs. Die Runtime darf diese Angabe nur
/// weiter einschraenken; Prompt oder Modellantwort koennen sie niemals erweitern.
/// </summary>
public enum AiToolApprovalMode
{
    Automatic,
    HumanRequired,
    PreApproved
}

/// <summary>
/// Stabile, nicht geheime Werkzeugreferenz eines KI-Tasks. Die konkrete Implementierung
/// und ihre Konfiguration bleiben in der serverseitigen Registry.
/// </summary>
public sealed record AiTaskToolReference(
    string ToolId,
    int ToolVersion,
    AiToolApprovalMode ApprovalMode);

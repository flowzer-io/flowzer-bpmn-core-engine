namespace WebApiEngine.Shared;

public class FormDto
{
    public Guid? Id { get; set; }

    /// <summary>
    /// Die Kennung des Formulars im Formularbestand. <c>null</c> bei einem Formular, das im
    /// Workflow selbst liegt: Es steht in keinem Bestand, laesst sich also auch nicht einzeln
    /// laden, und es ist mit dem Workflow versioniert statt mit einer eigenen Version.
    /// </summary>
    public Guid? FormId { get; set; }

    /// <summary>
    /// Die Version des Formulars im Bestand. <c>null</c> bei einem Formular aus dem Workflow:
    /// Es ist mit dem Workflow versioniert und traegt keine eigene Nummer. Eine erfundene 0.0
    /// waere hier eine Angabe, die es nicht gibt.
    /// </summary>
    public VersionDto? Version { get; set; }
    public string? FormData { get; set; }
}

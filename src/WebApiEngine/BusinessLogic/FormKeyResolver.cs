using BPMN.Flowzer;
using core_engine;
using Model;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;

namespace WebApiEngine.BusinessLogic;

/// <summary>
/// Löst einen Form-Key auf ein konkretes Formular auf — den einer menschlichen Aufgabe ebenso
/// wie den des Startformulars am Startereignis.
///
/// Der Form-Key stammt aus <c>zeebe:formDefinition/@formKey</c> und meint eines von zwei Dingen:
/// <list type="bullet">
/// <item>ein Formular aus dem Bestand — als <c>Formularname</c> (neueste Version beim Deployment)
/// oder <c>Formularname:1.0</c> (dann gilt genau diese Version). Statt des Namens darf auch die
/// Kennung des Formulars stehen (<c>Guid</c> bzw. <c>Guid:1.0</c>): Der Modeler legt sie als
/// <c>formId</c> ab, und der Parser nimmt sie als Form-Key an — ohne diesen Weg wäre ein so
/// modelliertes Formular hier unauffindbar;</item>
/// <item>ein Formular, das im Workflow selbst liegt — als
/// <c>camunda-forms:bpmn:Kennung</c> (siehe <see cref="FlowzerUserTaskForm"/>).</item>
/// </list>
/// Laufzeitaufrufe lesen ausschließlich den Deployment-Snapshot; externe Altbestände
/// ohne belegte Bindung werden nicht automatisch auf heutige Fassungen migriert.
///
/// Die Auflösung lag bisher im Blazor-Client und benötigte dort drei API-Aufrufe.
/// Als Teil der API gehört sie hierher: die Regel ist fachlich, nicht darstellend.
/// </summary>
public sealed partial class FormKeyResolver(IStorageSystem storageSystem)
{
    public sealed record Result(FormDto? Form, string? ErrorMessage)
    {
        public static Result FromStore(Form form) => new(form.ToDto(), null);

        /// <summary>
        /// Ein Formular aus dem Workflow. Es hat weder eine Kennung im Bestand noch eine eigene
        /// Version — beides gehört dem Workflow, mit dem es ausgeliefert wurde.
        /// </summary>
        public static Result FromWorkflow(string formData) =>
            new(new FormDto { FormData = formData, Version = null }, null);

        public static Result Failure(string message) => new(null, message);
    }

    /// <param name="formKey">Der Form-Key aus dem Modell.</param>
    /// <param name="definitionId">
    /// Die Version des Workflows, zu der der Schlüssel gehört. Nur darüber ist ein eingebettetes
    /// Formular erreichbar: Es steht im Diagramm, nicht in der Ablage der Formulare.
    /// </param>
    public async Task<Result> ResolveAsync(string? formKey, Guid definitionId)
    {
        if (string.IsNullOrWhiteSpace(formKey)) return await ResolveForDeploymentAsync(formKey, definitionId);
        var key = formKey.Trim();
        BpmnDefinition? definition;
        try
        {
            definition = await storageSystem.DefinitionStorage.GetDefinitionById(definitionId);
        }
        catch (FileNotFoundException)
        {
            definition = null;
        }

        if (definition?.FormBindings is { } bindings)
        {
            return bindings.TryGetValue(key, out var bound)
                ? FromBinding(bound)
                : Result.Failure($"No deployment form binding exists for form key \"{key}\". Deploy a new workflow version.");
        }

        // Historische eingebettete Formulare tragen ihren Stand bereits in der konkreten
        // BPMN-Version. Für externe Referenzen wäre „latest“ dagegen eine geratene Migration.
        if (FlowzerUserTaskForm.IdFromFormKey(key) is { } embeddedId)
            return await ResolveFromWorkflowAsync(embeddedId, definitionId);

        return Result.Failure($"The workflow has no verified form binding for \"{key}\". "
            + "Deploy a new workflow version for new instances; existing instances require an explicitly verified form migration.");
    }

    /// <summary>
    /// Ausschließlich für das Erstellen neuer Deployment-Bindungen, nicht für laufende
    /// Aufgaben. Hier darf ein unversionierter Schlüssel noch die neueste Fassung wählen.
    /// </summary>
    public async Task<Result> ResolveForDeploymentAsync(string? formKey, Guid definitionId)
    {
        if (string.IsNullOrWhiteSpace(formKey))
        {
            // Nur ueber User-Tasks erreichbar: Ein Startereignis ohne Formular wird gar nicht
            // erst aufgeloest, sondern gilt als „kein Startformular".
            return Result.Failure(
                "The user task has no form key. Set 'formKey' in the BPMN task properties.");
        }

        var embeddedFormId = FlowzerUserTaskForm.IdFromFormKey(formKey.Trim());
        if (embeddedFormId is not null)
        {
            return await ResolveFromWorkflowAsync(embeddedFormId, definitionId);
        }

        return await ResolveFromStoreAsync(formKey);
    }

    /// <summary>
    /// Holt ein eingebettetes Formular aus dem Diagramm der Workflow-Version, an der die Aufgabe
    /// hängt. Bewusst aus genau dieser Version: Eine laufende Instanz muss das Formular sehen,
    /// mit dem sie gestartet wurde, auch wenn der Workflow inzwischen ein anderes trägt.
    /// </summary>
    private async Task<Result> ResolveFromWorkflowAsync(string embeddedFormId, Guid definitionId)
    {
        if (embeddedFormId.Length == 0)
        {
            return Result.Failure(
                $"The form key \"{FlowzerUserTaskForm.FormKeyPrefix}\" names no form inside the workflow.");
        }

        string xml;
        try
        {
            xml = await storageSystem.DefinitionStorage.GetBinary(definitionId);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return Result.Failure(
                $"The workflow diagram holding the embedded form \"{embeddedFormId}\" is no longer available.");
        }

        // Bewusst nur die Formulare lesen und nicht das ganze Modell bauen: Ein Modellfehler an
        // einer anderen Stelle des Diagramms darf einer wartenden Aufgabe nicht ihr Formular
        // nehmen — und aus einem fachlichen Fehler keinen Serverfehler machen.
        FlowzerList<FlowzerUserTaskForm> forms;
        try
        {
            forms = ModelParser.ParseUserTaskForms(xml);
        }
        catch (System.Xml.XmlException exception)
        {
            return Result.Failure(
                $"The workflow diagram holding the embedded form \"{embeddedFormId}\" is not readable: {exception.Message}");
        }

        var form = forms
            .FirstOrDefault(candidate => string.Equals(candidate.Id, embeddedFormId, StringComparison.Ordinal));

        if (form is null)
        {
            return Result.Failure($"The workflow contains no embedded form \"{embeddedFormId}\".");
        }

        return Result.FromWorkflow(form.Schema);
    }

    private async Task<Result> ResolveFromStoreAsync(string formKey)
    {
        var (formName, requestedVersion, versionError) = SplitFormKey(formKey);
        if (versionError is not null)
        {
            return Result.Failure(versionError);
        }

        // Eine Kennung ist eindeutig, ein Name muss es sein: Deshalb wird nach Kennung gesucht,
        // sobald der Schluessel als Guid lesbar ist, und nur sonst nach Namen.
        var allMetadata = await storageSystem.FormStorage.GetFormMetadatas();
        var searchesById = Guid.TryParse(formName, out var formId);

        var metadata = (searchesById
                ? allMetadata.Where(candidate => candidate.FormId == formId)
                : allMetadata.Where(candidate =>
                    string.Equals(candidate.Name, formName, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        if (metadata.Length == 0)
        {
            return Result.Failure(searchesById
                ? $"No form with the id \"{formName}\" was found."
                : $"No form named \"{formName}\" was found.");
        }

        if (metadata.Length > 1)
        {
            return Result.Failure(
                $"Multiple forms named \"{formName}\" were found. Form names must be unique.");
        }

        var versions = (await storageSystem.FormStorage.GetForms(metadata[0].FormId)).ToArray();
        if (versions.Length == 0)
        {
            return Result.Failure($"The form \"{formName}\" has no saved version yet.");
        }

        var match = requestedVersion is null
            ? versions.MaxBy(version => version.Version)
            : versions.SingleOrDefault(version => version.Version.Equals(requestedVersion));

        if (match is null)
        {
            return Result.Failure($"Version {requestedVersion} of form \"{formName}\" was not found.");
        }

        return Result.FromStore(match);
    }

    /// <summary>
    /// Der Formularname aus einem Form-Key — ohne eine angehaengte Version, aber mit einem
    /// Doppelpunkt, der zum Namen gehoert („Pruefung: Detail"). Oeffentlich, damit die
    /// Loeschpruefung dieselbe Regel benutzt: Zwei Auslegungen desselben Schluessels waeren
    /// genau der Weg, ein benutztes Formular doch zu loeschen.
    /// </summary>
    public static string ExtractFormName(string formKey) => SplitFormKey(formKey).Name;

    /// <summary>
    /// Trennt <c>Name:Major.Minor</c> in Name und Version. Nur ein Suffix, das tatsaechlich eine
    /// Version ist, gilt als Versionsangabe; ein Formularname darf selbst Doppelpunkte enthalten
    /// ("Pruefung: Detail"). Ein Suffix, das wie eine Version aussieht, aber ungueltig ist
    /// ("Name:1.x"), wird als Fehler gemeldet, damit Tippfehler nicht still die neueste Version
    /// liefern.
    /// </summary>
    private static (string Name, Model.Version? Version, string? Error) SplitFormKey(string formKey)
    {
        var separatorIndex = formKey.LastIndexOf(':');
        if (separatorIndex < 0)
        {
            return (formKey.Trim(), null, null);
        }

        var name = formKey[..separatorIndex].Trim();
        var versionPart = formKey[(separatorIndex + 1)..].Trim();

        if (versionPart.Length == 0)
        {
            return (name, null, null);
        }

        if (!LooksLikeVersion(versionPart))
        {
            return (formKey.Trim(), null, null);
        }

        try
        {
            return (name, Model.Version.FromString(versionPart), null);
        }
        catch (ArgumentException exception)
        {
            return (name, null, $"The form key \"{formKey}\" has an invalid version: {exception.Message}");
        }
    }

    private static bool LooksLikeVersion(string candidate)
    {
        return candidate.Length > 0 && candidate.All(character => char.IsDigit(character) || character == '.');
    }
}

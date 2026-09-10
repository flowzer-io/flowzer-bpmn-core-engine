using System.Text;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using WebApiEngine.Auth;
using StorageSystem.Exceptions;
using WebApiEngine.Idempotency;
using WebApiEngine.Ai;
using core_engine.Exceptions;

namespace WebApiEngine.Controller;

/// <summary>
/// Definitionen und ihr Katalog. Alle Antworten tragen den einheitlichen Umschlag
/// <see cref="ApiStatusResult{T}"/>: Ein Client muss den Erfolgsfall nicht am Statuscode und den
/// Fehlerfall am Antwortkoerper unterscheiden, sondern liest beides an derselben Stelle.
/// Ausnahme ist bewusst nur die XML-Auslieferung, die ein Dokument liefert, kein JSON.
/// </summary>
[ApiController, Route("[controller]")]
public class DefinitionController(
    IStorageSystem storageSystem,
    DefinitionBusinessLogic definitionBusinessLogic,
    BpmnBusinessLogic bpmnBusinessLogic,
    FolderBusinessLogic folderBusinessLogic,
    FormKeyResolver formKeyResolver,
    InstanceAccessService instanceAccess,
    IAiSecretStore aiSecretStore,
    AiToolRegistry aiToolRegistry) : FlowzerControllerBase
{
    /// <summary>
    /// Meldung, wenn die Zustaendigkeit fuer den Ordner fehlt. Bewusst dieselbe Formulierung an
    /// allen Stellen: Wer sie einmal verstanden hat, versteht sie ueberall.
    /// </summary>
    private const string MissingFolderPermission =
        "Fuer diesen Ordner fehlt Ihnen die Bearbeitungsberechtigung.";

    private const string MissingRootPermission =
        "Workflows ausserhalb eines Ordners zu aendern ist der Rolle fuers Modellieren vorbehalten.";

    [HttpPost]
    [ProducesResponseType<ApiStatusResult<BpmnDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebApiEngine.Middleware.BpmnCapabilityProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> UploadDefinition([FromQuery] Guid? previousGuid)
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!permissions.MayEditAnywhere)
        {
            return ForbiddenCapability<BpmnDefinitionDto>(MissingRootPermission);
        }

        var rawContent = await GetRawContent();
        if (await DenyIfFolderIsForbidden<BpmnDefinitionDto>(rawContent, permissions) is { } denied)
        {
            return denied;
        }

        var definition = await definitionBusinessLogic.StoreDefinition(rawContent, previousGuid);
        return Ok(new ApiStatusResult<BpmnDefinitionDto>(definition.ToDto()));
    }
    
    [HttpPost("deploy")]
    [ProducesResponseType<ApiStatusResult<BpmnDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebApiEngine.Middleware.BpmnCapabilityProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> DeployDefinition([FromQuery] Guid? previousGuid)
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!permissions.MayEditAnywhere)
        {
            return ForbiddenCapability<BpmnDefinitionDto>(MissingRootPermission);
        }

        BpmnDefinition? definition = null;

        try
        {
            var rawContent = await GetRawContent();
            if (await DenyIfFolderIsForbidden<BpmnDefinitionDto>(rawContent, permissions) is { } denied)
            {
                return denied;
            }

            definition = await definitionBusinessLogic.StoreDefinition(rawContent, previousGuid, true);
            await bpmnBusinessLogic.DeployDefinition(definition);
            return Ok(new ApiStatusResult<BpmnDefinitionDto>(definition.ToDto()));
        }
        catch (UnauthorizedAccessException)
        {
            await CleanupOrphanedVersionAsync(definition);
            throw;
        }
        catch (BpmnCapabilityValidationException)
        {
            await CleanupOrphanedVersionAsync(definition);
            throw;
        }
        catch (Exception e)
        {
            await CleanupOrphanedVersionAsync(definition);
            return BadRequest(new ApiStatusResult<BpmnDefinitionDto>(e.Message));
        }
    }

    /// <summary>
    /// Liefert den einen versionierten Vertrag, den Modellieransichten für Palette,
    /// Gliederung und Hinweise verwenden. Der Vertrag enthält keine Host-Annahmen.
    /// </summary>
    [HttpGet("capabilities")]
    [ProducesResponseType<ApiStatusResult<BpmnCapabilityContract>>(StatusCodes.Status200OK)]
    public ActionResult<ApiStatusResult<BpmnCapabilityContract>> GetCapabilities() =>
        Ok(new ApiStatusResult<BpmnCapabilityContract>(BpmnCapabilityMatrix.Contract));

    /// <summary>
    /// Prüft BPMN vor dem Speichern ohne eine Version anzulegen. Fehler nutzen denselben
    /// Problem-Details-Vertrag wie Upload und Deployment.
    /// </summary>
    [HttpPost("validate")]
    [ProducesResponseType<ApiStatusResult<BpmnCapabilityContract>>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebApiEngine.Middleware.BpmnCapabilityProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<ActionResult<ApiStatusResult<BpmnCapabilityContract>>> ValidateDefinition() =>
        ValidateDefinition(BpmnCapabilityMatrix.ValidateForAuthoring);

    /// <summary>
    /// Prueft dieselbe Eingabe gegen die strengere ausfuehrbare Teilmenge. Ein eigener Pfad
    /// verhindert, dass ein Requestparameter eine sicherheitsrelevante Pruefung abschwaecht.
    /// </summary>
    [HttpPost("validate/deployment")]
    [ProducesResponseType<ApiStatusResult<BpmnCapabilityContract>>(StatusCodes.Status200OK)]
    [ProducesResponseType<WebApiEngine.Middleware.BpmnCapabilityProblemDetails>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    public Task<ActionResult<ApiStatusResult<BpmnCapabilityContract>>> ValidateDeployment() =>
        ValidateDefinition(BpmnCapabilityMatrix.ValidateForDeployment);

    private async Task<ActionResult<ApiStatusResult<BpmnCapabilityContract>>> ValidateDefinition(
        Action<string> validateCapabilities)
    {
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!permissions.MayEditAnywhere)
        {
            return ForbiddenCapability<BpmnCapabilityContract>(MissingRootPermission);
        }

        var rawContent = await GetRawContent();
        if (await DenyIfFolderIsForbidden<BpmnCapabilityContract>(rawContent, permissions) is { } denied)
        {
            return denied;
        }

        validateCapabilities(rawContent);
        var model = ModelParser.ParseModel(rawContent);
        await AiTaskDeploymentValidator.ValidateAsync(
            model,
            storageSystem.AiConnectionStorage,
            aiSecretStore,
            aiToolRegistry);
        return Ok(new ApiStatusResult<BpmnCapabilityContract>(BpmnCapabilityMatrix.Contract));
    }

    /// <summary>
    /// Startet eine Instanz. Der Rumpf ist optional: Ein Workflow ohne Startformular startet wie
    /// bisher ohne Angaben, ein Workflow mit Startformular bekommt dessen Werte als
    /// <c>variables</c>.
    /// </summary>
    [HttpPost("meta/{id}/instance")]
    [ProducesResponseType<WebApiEngine.Middleware.ApiValidationProblem>(StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType<ApiStatusResult<ProcessInstanceInfoDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<ProcessInstanceInfoDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<WebApiEngine.Middleware.ApiProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<ApiStatusResult<ProcessInstanceInfoDto>>> StartInstance(
        [FromRoute] string id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] StartInstanceDto? body,
        [FromHeader(Name = HttpIdempotency.HeaderName)] string? _idempotencyKey = null)
    {
        try
        {
            var (currentUser, canInspect) = await instanceAccess.GetPermissionsAsync();
            var idempotency = HttpIdempotency.Create(Request, currentUser,
                "workflow-start", id, body?.Variables);
            var processInstance = await bpmnBusinessLogic.StartProcessInstance(id, body?.Variables,
                initiator: currentUser.Identity, idempotency: idempotency);
            var processInstanceDto = await processInstance.ToDtoAsync(storageSystem.DefinitionStorage, canInspect);
            return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(processInstanceDto));
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or WebApiEngine.Forms.FormSubmissionException or IdempotencyConflictException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return BadRequest(new ApiStatusResult<ProcessInstanceInfoDto>(exception.Message));
        }
    }

    /// <summary>
    /// Liefert das Formular, das ausfuellt, wer diesen Workflow startet.
    ///
    /// Ein Workflow ohne Startformular antwortet mit 204: Die Oberflaeche soll daran erkennen,
    /// dass sie sofort starten kann, statt einen leeren Dialog zu zeigen.
    /// </summary>
    [HttpGet("meta/{id}/start-form")]
    [ProducesResponseType<ApiStatusResult<FormDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiStatusResult<FormDto>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiStatusResult<FormDto>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiStatusResult<FormDto>>> GetStartForm([FromRoute] string id)
    {
        BpmnBusinessLogic.StartFormReference startForm;
        try
        {
            startForm = await bpmnBusinessLogic.GetStartFormReference(id);
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (DefinitionStorageNotFoundException exception)
        {
            return NotFound(new ApiStatusResult<FormDto>(exception.Message));
        }
        catch (Exception exception)
        {
            return BadRequest(new ApiStatusResult<FormDto>(exception.Message));
        }

        if (startForm.FormKey is null)
        {
            return NoContent();
        }

        // Mit der Kennung der deployten Version, damit auch ein im Diagramm eingebettetes
        // Formular gefunden wird — es steht in keinem Formularbestand.
        var resolved = await formKeyResolver.ResolveAsync(startForm.FormKey, startForm.DefinitionId);
        if (resolved.Form is null)
        {
            return BadRequest(new ApiStatusResult<FormDto>(resolved.ErrorMessage));
        }

        return Ok(new ApiStatusResult<FormDto>(resolved.Form));
    }


    /// <summary>
    /// Legt eine leere Definition samt Katalogeintrag an.
    ///
    /// Bewusst POST und nicht GET: Der Aufruf legt Daten an. Als GET reichte ein Link oder ein
    /// Vorablade-Versuch des Browsers, um den Katalog mit leeren Eintraegen zu fuellen.
    /// Der Name kommt vom Aufrufer — die Oberflaeche fragt ihn, bevor sie anlegt, damit kein
    /// unbenannter Entwurf entsteht, den danach niemand zuordnen kann.
    /// </summary>
    [HttpPost("new")]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> NewDefinition(
        [FromQuery] string? name,
        [FromQuery] Guid? folderId)
    {
        var trimmedName = name?.Trim();
        if (trimmedName is { Length: > MaxDefinitionNameLength })
        {
            return BadRequest(new ApiStatusResult<BpmnMetaDefinitionDto>(
                $"Der Name darf höchstens {MaxDefinitionNameLength} Zeichen lang sein."));
        }

        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!FolderBusinessLogic.IsKnownTarget(folderId, permissions.Folders))
        {
            return NotFound(new ApiStatusResult<BpmnMetaDefinitionDto>($"Es gibt keinen Ordner mit der Kennung {folderId}."));
        }

        if (!permissions.MayEditIn(folderId))
        {
            return ForbiddenCapability<BpmnMetaDefinitionDto>(folderId is null ? MissingRootPermission : MissingFolderPermission);
        }

        var definitionId = "definition_" + Guid.NewGuid();
        var modelId = "model_" + Guid.NewGuid();
        var emptyXml = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI" id="{definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
                          <bpmn:process id="{modelId}" isExecutable="true" />
                          <bpmndi:BPMNDiagram id="BPMNDiagram_1">
                            <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="{modelId}" />
                          </bpmndi:BPMNDiagram>
                        </bpmn:definitions>
                        """;
        
        // Version und Katalogeintrag liegen in getrennten Transaktionen. Scheitert der zweite
        // Schritt, bleibt sonst eine Version ohne Eintrag zurueck — im Katalog unsichtbar, ueber
        // /definition und /definition/xml aber weiterhin abrufbar. Deshalb dasselbe Aufraeumen
        // wie beim fehlgeschlagenen Deploy.
        var definition = await definitionBusinessLogic.StoreDefinition(emptyXml, null);
        var metaDefinition = new BpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = string.IsNullOrWhiteSpace(trimmedName) ? "Neuer Workflow" : trimmedName,
            FolderId = folderId
        };

        try
        {
            await storageSystem.DefinitionStorage.StoreMetaDefinition(metaDefinition);
        }
        catch
        {
            await CleanupOrphanedVersionAsync(definition);
            throw;
        }

        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(metaDefinition.ToDto()));
    }

    

    [HttpGet]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto[]>>> GetAllDefinitions()
    {
        var allBinaryDefinitions = await storageSystem.DefinitionStorage.GetAllDefinitions();
        var bpmnDefinitionDto = allBinaryDefinitions.Select(definition => definition.ToDto()).ToArray();
        return Ok(new ApiStatusResult<BpmnDefinitionDto[]>(bpmnDefinitionDto));
    }
    
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> GetDefinitionById([FromRoute] Guid id)
    {
        var definitionById = await storageSystem.DefinitionStorage.GetDefinitionById(id);
        return Ok(new ApiStatusResult<BpmnDefinitionDto>(definitionById.ToDto()));
    }
    
     
        
    [HttpGet("xml/{guid}")]
    [Produces("application/xml")]
    public async Task<ActionResult> GetDefinitionXml([FromRoute] Guid guid)
    {
        var xml = await storageSystem.DefinitionStorage.GetBinary(guid);

        // Bewusst als Content mit festem Content-Type: Über `Ok(xml)` liefert die
        // Content-Negotiation bei `Accept: application/json` ein JSON-String-Literal
        // ("<?xml version=\"1.0\" …") — für jeden XML-Parser unbrauchbar.
        return Content(xml, "application/xml", Encoding.UTF8);
    }

    
    #region meta

    [HttpGet("meta")]
    public async Task<ActionResult<ApiStatusResult<ExtendedBpmnMetaDefinitionDto[]>>> MetaIndex()
    {
        var allMetaDefinitions = await storageSystem.DefinitionStorage.GetAllMetaDefinitions();
        var bpmnDefinitionDto = allMetaDefinitions.Select(definition => definition.ToDto()).ToArray();
        return Ok(new ApiStatusResult<ExtendedBpmnMetaDefinitionDto[]>(bpmnDefinitionDto));
    }
    
    [HttpGet("meta/{id}")]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaGetById([FromRoute] string id)
    {
        var metaDefinition = await storageSystem.DefinitionStorage.GetMetaDefinitionById(id);
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(metaDefinition.ToDto()));
    }
    
        
    [HttpGet("meta/{id}/latest")]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> LatestDefinition([FromRoute] string id)
    {
        var latestDefinition = await storageSystem.DefinitionStorage.GetLatestDefinition(id);
        return Ok(new ApiStatusResult<BpmnDefinitionDto>(latestDefinition.ToDto()));
    }
    

    [HttpPost("meta")]
    [ProducesResponseType<ApiStatusResult<BpmnMetaDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<BpmnMetaDefinitionDto>>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaPost([FromBody] BpmnMetaDefinitionDto dto)
    {
        // Die Kennung wird in der Dateiablage zum Dateinamen; sie darf den Ablageordner nicht
        // verlassen. Siehe DefinitionIdRules.
        if (!DefinitionIdRules.IsValid(dto.DefinitionId))
        {
            return BadRequest(new ApiStatusResult<BpmnMetaDefinitionDto>(
                DefinitionIdRules.BuildErrorMessage(dto.DefinitionId)));
        }

        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!FolderBusinessLogic.IsKnownTarget(dto.FolderId, permissions.Folders))
        {
            return NotFound(new ApiStatusResult<BpmnMetaDefinitionDto>($"Es gibt keinen Ordner mit der Kennung {dto.FolderId}."));
        }

        if (!permissions.MayEditIn(dto.FolderId))
        {
            return ForbiddenCapability<BpmnMetaDefinitionDto>(dto.FolderId is null ? MissingRootPermission : MissingFolderPermission);
        }

        var definition = dto.ToModel();
        await storageSystem.DefinitionStorage.StoreMetaDefinition(definition);
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(definition.ToDto()));
    }


    /// <summary>
    /// Aendert Name und Beschreibung eines Katalogeintrags.
    ///
    /// Der Ordner bleibt dabei, wie er ist — auch wenn die Anfrage einen anderen nennt.
    /// Verschieben ist eine eigene Handlung mit einer eigenen Pruefung (siehe
    /// <see cref="MoveDefinition"/>): Sie braucht die Berechtigung an zwei Ordnern, nicht an
    /// einem, und darf nicht als Nebenwirkung eines Umbenennens passieren.
    /// </summary>
    [HttpPut("meta")]
    [ProducesResponseType<ApiStatusResult<BpmnMetaDefinitionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiStatusResult<BpmnMetaDefinitionDto>>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaPut([FromBody] BpmnMetaDefinitionDto dto)
    {
        // Dieselbe Pruefung wie beim Anlegen: Sonst liesse sie sich mit einem PUT umgehen.
        if (!DefinitionIdRules.IsValid(dto.DefinitionId))
        {
            return BadRequest(new ApiStatusResult<BpmnMetaDefinitionDto>(
                DefinitionIdRules.BuildErrorMessage(dto.DefinitionId)));
        }

        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        var folderId = await folderBusinessLogic.GetFolderOfDefinitionAsync(dto.DefinitionId);
        if (!permissions.MayEditIn(folderId))
        {
            return ForbiddenCapability<BpmnMetaDefinitionDto>(folderId is null ? MissingRootPermission : MissingFolderPermission);
        }

        var definition = dto.ToModel();
        definition.FolderId = folderId;
        await storageSystem.DefinitionStorage.UpdateMetaDefinition(definition);
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(definition.ToDto()));
    }

    /// <summary>
    /// Verschiebt einen Workflow in einen anderen Ordner; <c>folderId</c> ohne Wert bedeutet
    /// oberste Ebene.
    ///
    /// Geprueft wird an beiden Enden: Wer den Workflow herausnimmt, muss im Herkunftsordner
    /// bearbeiten duerfen, und wer ihn ablegt, im Zielordner. Nur das Ziel zu pruefen erlaubte
    /// es, fremde Workflows in den eigenen Ordner zu holen; nur die Herkunft zu pruefen erlaubte
    /// das Gegenteil.
    /// </summary>
    [HttpPut("meta/{id}/folder")]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MoveDefinition(
        [FromRoute] string id,
        [FromQuery] Guid? folderId)
    {
        BpmnMetaDefinition metaDefinition;
        try
        {
            metaDefinition = await storageSystem.DefinitionStorage.GetMetaDefinitionById(id);
        }
        catch (DefinitionStorageNotFoundException)
        {
            return NotFound(new ApiStatusResult<BpmnMetaDefinitionDto>($"Es gibt keinen Workflow mit der Kennung {id}."));
        }

        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        if (!FolderBusinessLogic.IsKnownTarget(folderId, permissions.Folders))
        {
            return NotFound(new ApiStatusResult<BpmnMetaDefinitionDto>($"Es gibt keinen Ordner mit der Kennung {folderId}."));
        }

        if (!permissions.MayEditIn(metaDefinition.FolderId))
        {
            return ForbiddenCapability<BpmnMetaDefinitionDto>(metaDefinition.FolderId is null
                ? MissingRootPermission
                : "Fuer den Ordner, in dem der Workflow liegt, fehlt Ihnen die Bearbeitungsberechtigung.");
        }

        if (!permissions.MayEditIn(folderId))
        {
            return ForbiddenCapability<BpmnMetaDefinitionDto>(folderId is null
                ? MissingRootPermission
                : "Fuer den Zielordner fehlt Ihnen die Bearbeitungsberechtigung.");
        }

        metaDefinition.FolderId = folderId;
        await storageSystem.DefinitionStorage.UpdateMetaDefinition(metaDefinition);
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(metaDefinition.ToDto()));
    }

    /// <summary>
    /// Loescht einen Workflow endgueltig: Katalogeintrag, alle Versionen, deren BPMN-XML und
    /// die bereits beendeten Instanzen samt ihrer Anmeldungen und offenen Worker-Auftraege.
    ///
    /// Laufende Instanzen halten das Loeschen auf: Ihnen wuerde die Definition unter den
    /// Fuessen weggezogen, die Engine koennte sie danach nicht mehr fortsetzen. Der Aufruf
    /// antwortet dann mit 409 und nennt die Anzahl.
    ///
    /// Beendete Instanzen gehen mit — die Prozesshistorie dieses Workflows ist danach fort.
    /// Blieben sie liegen, stuenden sie ohne Definition in der Instanzliste: ohne Namen und
    /// ohne abrufbares Diagramm. Der Aufruf ist nicht rueckgaengig zu machen.
    /// </summary>
    [HttpDelete("meta/{id}")]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaDelete([FromRoute] string id)
    {
        // Rechte zuerst und anhand des gespeicherten Ordners: Ohne Berechtigung darf der Aufruf
        // nicht einmal verraten, ob es diesen Workflow gibt.
        var permissions = await folderBusinessLogic.LoadPermissionsAsync(User);
        var folderId = await folderBusinessLogic.GetFolderOfDefinitionAsync(id);
        if (!permissions.MayEditIn(folderId))
        {
            return ForbiddenCapability<BpmnMetaDefinitionDto>(folderId is null ? MissingRootPermission : MissingFolderPermission);
        }

        BpmnMetaDefinition metaDefinition;
        try
        {
            // Wirft, wenn es den Eintrag nicht gibt — dann ist das Loeschen kein Erfolg.
            // Zugleich die Vorlage fuer die Antwort: Danach ist der Eintrag weg.
            metaDefinition = await storageSystem.DefinitionStorage.GetMetaDefinitionById(id);
        }
        catch (DefinitionStorageNotFoundException)
        {
            return NotFound(new ApiStatusResult<BpmnMetaDefinitionDto>(
                errorMessage: $"Es gibt keinen Workflow mit der Kennung {id}."));
        }

        // Pruefung und Loeschen liegen in der Engine unter derselben Sperre wie der
        // Instanzstart; hier auseinandergezogen koennte dazwischen eine Instanz starten.
        var activeInstances = await bpmnBusinessLogic.DeleteDefinition(id);
        if (activeInstances > 0)
        {
            return Conflict(new ApiStatusResult<BpmnMetaDefinitionDto>(
                errorMessage: $"Der Workflow hat noch {activeInstances} laufende Instanz(en). Erst beenden oder abbrechen, dann löschen."));
        }

        // Bewusst der geloeschte Eintrag und nicht nur die Kennung als Zeichenkette:
        // `new ApiStatusResult<string>(id)` traefe den Konstruktor fuer die Fehlermeldung
        // und meldete den erfolgreichen Aufruf als Fehlschlag.
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(metaDefinition.ToDto()));
    }

    #endregion

    /// <summary>Grenze fuer den Namen einer Definition — verhindert unbrauchbar lange Katalogeintraege.</summary>
    private const int MaxDefinitionNameLength = 200;

    /// <summary>
    /// Lehnt einen Definitionsupload ab, wenn der Workflow in einem Ordner liegt, fuer den die
    /// Berechtigung fehlt. Liefert <c>null</c>, wenn nichts entgegensteht.
    /// </summary>
    private async Task<ActionResult<ApiStatusResult<T>>?> DenyIfFolderIsForbidden<T>(
        string rawContent,
        FolderPermissions permissions)
    {
        var definitionId = DefinitionBusinessLogic.TryReadDefinitionId(rawContent);
        if (definitionId is null)
        {
            return null;
        }

        var folderId = await folderBusinessLogic.GetFolderOfDefinitionAsync(definitionId);
        return permissions.MayEditIn(folderId)
            ? null
            : ForbiddenCapability<T>(folderId is null ? MissingRootPermission : MissingFolderPermission);
    }

    /// <summary>
    /// Entfernt eine Version, deren zugehoeriger Schritt fehlgeschlagen ist. Ohne das bliebe
    /// sie im Katalog unsichtbar liegen und waere trotzdem ueber /definition abrufbar.
    /// </summary>
    private async Task CleanupOrphanedVersionAsync(BpmnDefinition? definition)
    {
        if (definition == null)
        {
            return;
        }

        try
        {
            await storageSystem.DefinitionStorage.DeleteBinary(definition.Id);
            await storageSystem.DefinitionStorage.DeleteDefinition(definition.Id);
        }
        catch
        {
            // Best effort only: the original error is more relevant for the caller than
            // cleanup follow-up problems in the date-based storage fallback.
        }
    }
}

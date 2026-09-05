using System.Text;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;
using StorageSystem.Exceptions;

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
    DefinitionBusinessLogic definitionBusinessLogic, BpmnBusinessLogic bpmnBusinessLogic) : FlowzerControllerBase
{
    
    [HttpPost]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> UploadDefinition([FromQuery] Guid? previousGuid)
    {
        var rawContent = await GetRawContent();
        var definition = await definitionBusinessLogic.StoreDefinition(rawContent, previousGuid);
        return Ok(new ApiStatusResult<BpmnDefinitionDto>(definition.ToDto()));
    }
    
    [HttpPost("deploy")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<BpmnDefinitionDto>>> DeployDefinition([FromQuery] Guid? previousGuid)
    {
        BpmnDefinition? definition = null;

        try
        {
            var rawContent = await GetRawContent();
            definition = await definitionBusinessLogic.StoreDefinition(rawContent, previousGuid, true);
            await bpmnBusinessLogic.DeployDefinition(definition);
            return Ok(new ApiStatusResult<BpmnDefinitionDto>(definition.ToDto()));
        }
        catch (UnauthorizedAccessException)
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

    [HttpPost("meta/{id}/instance")]
    public async Task<ActionResult<ApiStatusResult<ProcessInstanceInfoDto>>> StartInstance([FromRoute] string id)
    {
        try
        {
            var processInstance = await bpmnBusinessLogic.StartProcessInstance(id);
            var processInstanceDto = await processInstance.ToDtoAsync(storageSystem.DefinitionStorage);
            return Ok(new ApiStatusResult<ProcessInstanceInfoDto>(processInstanceDto));
        }
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return BadRequest(new ApiStatusResult<ProcessInstanceInfoDto>(exception.Message));
        }
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
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> NewDefinition([FromQuery] string? name)
    {
        var trimmedName = name?.Trim();
        if (trimmedName is { Length: > MaxDefinitionNameLength })
        {
            return BadRequest(new ApiStatusResult<BpmnMetaDefinitionDto>(
                $"Der Name darf höchstens {MaxDefinitionNameLength} Zeichen lang sein."));
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
            Name = string.IsNullOrWhiteSpace(trimmedName) ? "Neuer Workflow" : trimmedName
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
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaPost([FromBody] BpmnMetaDefinitionDto dto)
    {
        var definition = dto.ToModel();
        await storageSystem.DefinitionStorage.StoreMetaDefinition(definition);
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(definition.ToDto()));
    }
    
    
    [HttpPut("meta")]
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaPut([FromBody] BpmnMetaDefinitionDto dto)
    {
        var definition = dto.ToModel();
        await storageSystem.DefinitionStorage.UpdateMetaDefinition(definition);
        return Ok(new ApiStatusResult<BpmnMetaDefinitionDto>(definition.ToDto()));
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
    [Authorize(Policy = FlowzerPolicies.Modeler)]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> MetaDelete([FromRoute] string id)
    {
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

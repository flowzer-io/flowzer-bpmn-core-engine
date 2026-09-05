using System.Text;
using WebApiEngine.BusinessLogic;
using WebApiEngine.Mappers;
using WebApiEngine.Shared;
using Microsoft.AspNetCore.Authorization;
using WebApiEngine.Auth;

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
            await CleanupFailedDeployVersionAsync(definition);
            throw;
        }
        catch (Exception e)
        {
            await CleanupFailedDeployVersionAsync(definition);
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


    [HttpGet("new")]
    public async Task<ActionResult<ApiStatusResult<BpmnMetaDefinitionDto>>> NewDefinition()
    {
        
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
        
        await definitionBusinessLogic.StoreDefinition(emptyXml, null);
        var metaDefinition = new BpmnMetaDefinition
        {
            DefinitionId = definitionId,
            Name = "New Definition"
        };
        await storageSystem.DefinitionStorage.StoreMetaDefinition(metaDefinition);
        
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

 
    #endregion

    private async Task CleanupFailedDeployVersionAsync(BpmnDefinition? definition)
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
            // Best effort only: the original deploy error is more relevant for the caller
            // than cleanup follow-up problems in the date-based storage fallback.
        }
    }
}

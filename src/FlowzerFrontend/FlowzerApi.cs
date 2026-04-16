using System.Net.Http.Json;
using FlowzerFrontend.Exceptions;
using WebApiEngine.Shared;

namespace FlowzerFrontend;

/// <summary>
/// Kapselt die HTTP-Kommunikation des Blazor-Frontends mit der Flowzer-Web-API.
/// </summary>
public class FlowzerApi: ApiBase
{
    public FlowzerApi(HttpClient httpClient) : base(httpClient)
    {
    }

    internal async Task<ProcessInfoDto[]> GetModels()
    {
        return await GetAsJsonAsyncSave<ProcessInfoDto[]>($"model");
    }
    
    internal async Task<BpmnMetaDefinitionDto> GetMetaDefinitionById(string definitionId)
    {
        return await GetRequiredJsonAsync<BpmnMetaDefinitionDto>(
            $"definition/meta/{definitionId}",
            $"No meta definition found for definitionId {definitionId}");
    }

    internal async Task<BpmnDefinitionDto> GetLatestDefinition(string metaDefinitionId)
    {
        return await GetRequiredJsonAsync<BpmnDefinitionDto>(
            $"definition/meta/{metaDefinitionId}/latest",
            $"No definition found for definitionId {metaDefinitionId}");
    }
    
    /// <summary>
    /// Lädt eine konkrete Definitionsversion anhand ihrer GUID.
    /// </summary>
    public async Task<BpmnDefinitionDto> GetDefinition(Guid? definitionId)
    {
        return await GetRequiredJsonAsync<BpmnDefinitionDto>(
            $"definition/{definitionId}",
            $"No definition found for definitionId {definitionId}");
    }
    
    /// <summary>
    /// Persistiert geänderte Metadaten einer BPMN-Definition.
    /// </summary>
    public async Task UpdateMetaDefinition(BpmnMetaDefinitionDto currentMetaDefinition)
    {
        await PutAsJsonAsyncSave<object>($"definition/meta", currentMetaDefinition);
    }
    
    /// <summary>
    /// Lädt das BPMN-XML zu einer konkreten Definitionsversion.
    /// </summary>
    internal async Task<string> GetXmlDefinition(Guid guid)
    {
        return await HttpClient.GetStringAsync($"definition/xml/{guid}");
    }

    /// <summary>
    /// Lädt alle bekannten BPMN-Metadefinitionen inklusive erweitertem UI-Kontext.
    /// </summary>
    public Task<ExtendedBpmnMetaDefinitionDto[]> GetAllBpmnMetaDefinitions()
    {
        return GetAsJsonAsyncSave<ExtendedBpmnMetaDefinitionDto[]>("definition/meta");
    }

    /// <summary>
    /// Erstellt serverseitig eine neue leere BPMN-Definition.
    /// </summary>
    public async Task<BpmnMetaDefinitionDto> CreateEmptyDefinition()
    {
        return await GetAsJsonAsyncSave<BpmnMetaDefinitionDto>("definition/new");
    }

    /// <summary>
    /// Speichert eine BPMN-Definition als neue Version, optional basierend auf einer bestehenden Version.
    /// </summary>
    public async Task<BpmnDefinitionDto> UploadDefinition(string xml, Guid? previousGuid = null)
    {
        var url = AppendPreviousGuidQuery("definition", previousGuid);
        return await PostAsJsonAsyncSave<BpmnDefinitionDto>(url, xml);
    }

    /// <summary>
    /// Speichert und deployt eine BPMN-Definition als neue aktive Version.
    /// </summary>
    public async Task<BpmnDefinitionDto> DeployDefinition(string xml, Guid? previousGuid = null)
    {
        var url = AppendPreviousGuidQuery("definition/deploy", previousGuid);
        var apiStatusResult = await PostAsJsonAsyncSave<ApiStatusResult<BpmnDefinitionDto>>(url, xml, true, false);
        if (apiStatusResult.Successful)
            return apiStatusResult.Result!;
        
        throw new ApiException(apiStatusResult.ErrorMessage);
    }

    /// <summary>
    /// Startet eine neue Prozessinstanz für die aktuell deployte Workflow-Version.
    /// </summary>
    public async Task<ProcessInstanceInfoDto> StartProcessInstance(string relatedDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relatedDefinitionId);

        var apiStatusResult = await PostAsJsonAsyncSave<ApiStatusResult<ProcessInstanceInfoDto>>(
            $"definition/meta/{relatedDefinitionId}/instance",
            new { },
            throwOnUnsuccessfulStatusCodes: false);

        if (apiStatusResult.Successful)
        {
            return apiStatusResult.Result!;
        }

        throw new ApiException(apiStatusResult.ErrorMessage);
    }

    /// <summary>
    /// Lädt alle bekannten Prozessinstanzen.
    /// </summary>
    public async Task<List<ProcessInstanceInfoDto>> GetAllInstances()
    {
        return await GetAsJsonAndThrowOnErrorAsync<List<ProcessInstanceInfoDto>>("instance");
    }

    /// <summary>
    /// Lädt die Detaildaten einer Prozessinstanz.
    /// </summary>
    public async Task<ProcessInstanceInfoDto> GetProcessInstance(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<ProcessInstanceInfoDto>("instance/" + instanceGuid);
    }

    /// <summary>
    /// Lädt alle aktiven Message-Subscriptions einer Instanz.
    /// </summary>
    public async Task<MessageSubscriptionDto[]> GetMessageSubscriptions(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<MessageSubscriptionDto[]>("instance/" + instanceGuid + "/subscription/messages");
    }
    
    /// <summary>
    /// Lädt alle aktiven Service-Task-Subscriptions einer Instanz.
    /// </summary>
    public async Task<TokenDto[]> GetServiceSubscriptions(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<TokenDto[]>("instance/" + instanceGuid + "/subscription/services");
    }

    /// <summary>
    /// Lädt alle aktiven Signal-Subscriptions einer Instanz.
    /// </summary>
    public async Task<SignalSubscriptionDto[]> GetSignalSubscriptions(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<SignalSubscriptionDto[]>("instance/" + instanceGuid + "/subscription/signals");
    }

    /// <summary>
    /// Lädt alle aktiven User-Tasks einer Instanz.
    /// </summary>
    public async Task<TokenDto[]> GetUserTasks(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<TokenDto[]>("instance/" + instanceGuid + "/subscription/userTasks");
    }

    /// <summary>
    /// Lädt alle offenen User-Tasks über alle Instanzen hinweg.
    /// </summary>
    public async Task<ExtendedUserTaskSubscriptionDto[]> GetAllUserTasks()
    {
        return await GetAsJsonAndThrowOnErrorAsync<ExtendedUserTaskSubscriptionDto[]>("usertask");
    }
    
    /// <summary>
    /// Lädt die Metadaten eines Formulars.
    /// </summary>
    public async Task<FormMetaDataDto> GetFormMetaData(Guid metaDataId)
    {
        return await GetAsJsonAndThrowOnErrorAsync<FormMetaDataDto>("form/meta/" + metaDataId);
    }
    
    /// <summary>
    /// Lädt die aktuellste Version eines Formulars.
    /// </summary>
    public async Task<FormDto> GetLatestForm(Guid formId)
    {
        return await GetAsJsonAndThrowOnErrorAsync<FormDto>("form/" + formId + "/latest");
    }
    
    /// <summary>
    /// Lädt eine konkrete Version eines Formulars.
    /// </summary>
    public async Task<FormDto> GetForm(Guid formId, VersionDto version)
    {
        return await GetAsJsonAndThrowOnErrorAsync<FormDto>("form/" + formId + "/" + version);
    }

    /// <summary>
    /// Persistiert geänderte Formularmetadaten.
    /// </summary>
    public async Task SaveFormMetaData(FormMetaDataDto formMetaDataDto)
    {
        await PostAsJsonAndOnErrorAsync<dynamic>("form/meta/" + formMetaDataDto.FormId, formMetaDataDto);
    }

    /// <summary>
    /// Speichert ein Formular als neue Version.
    /// </summary>
    public async Task<FormDto> SaveForm(FormDto formData)
    {
        return await PostAsJsonAndOnErrorAsync<FormDto>("form", formData);
    }

    /// <summary>
    /// Lädt alle bekannten Formularmetadaten.
    /// </summary>
    public async Task<List<FormMetaDataDto>> GetFormMetaDatas()
    {
        return await GetAsJsonAndThrowOnErrorAsync<List<FormMetaDataDto>>("form/meta");
    }

    /// <summary>
    /// Sucht Formularmetadaten anhand des Formularnamens.
    /// </summary>
    public async Task<List<FormMetaDataDto>> GetFormMetaByName(string formName)
    {
        return await GetAsJsonAndThrowOnErrorAsync<List<FormMetaDataDto>>("form/meta?search=" + formName);
    }

    /// <summary>
    /// Sendet ein abgeschlossenes User-Task-Formular an die API zurück.
    /// </summary>
    public async Task CompleteUserTask(UserTaskResultDto userTaskResult)
    {
        await PostAsJsonAndOnErrorAsync<dynamic>("form/result", userTaskResult);
    }

    private static string AppendPreviousGuidQuery(string url, Guid? previousGuid)
    {
        if (!previousGuid.HasValue || previousGuid.Value == Guid.Empty)
        {
            return url;
        }

        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}previousGuid={Uri.EscapeDataString(previousGuid.Value.ToString())}";
    }
}

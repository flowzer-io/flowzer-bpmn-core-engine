using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FlowzerFrontend.Models;
using WebApiEngine.Shared;

namespace FlowzerFrontend;

public class FlowzerApi: ApiBase
{
    internal async Task<ProcessInfoDto[]> GetModels()
    {
        return await GetAsJsonAsyncSave<ProcessInfoDto[]>($"model");
    }
    
    internal async Task<BpmnMetaDefinitionDto> GetMetaDefinitionById(string definitionId)
    {
        var bpmnMetaDefinitionDto = await HttpClient.GetFromJsonAsync<BpmnMetaDefinitionDto>($"definition/meta/{definitionId}");
        if (bpmnMetaDefinitionDto == null)
        {
            throw new Exception($"No meta definition found for definitionId {definitionId}");
        }
        return bpmnMetaDefinitionDto;
    }

    internal async Task<BpmnDefinitionDto> GetLatestDefinition(string definitionId)
    {
        var bpmnDefinitionDto = await HttpClient.GetFromJsonAsync<BpmnDefinitionDto>($"definition/meta/{definitionId}/latest");
        if (bpmnDefinitionDto == null)
        {
            throw new Exception($"No definition found for definitionId {definitionId}");
        }
        return bpmnDefinitionDto;
    }
    
    public async Task UpdateMetaDefinition(BpmnMetaDefinitionDto currentMetaDefinition)
    {
        await PutAsJsonAsyncSave<object>($"definition/meta", currentMetaDefinition);
    }
    
    internal async Task<string> GetXmlDefinition(Guid guid)
    {
        return await HttpClient.GetStringAsync($"definition/xml/{guid}");
    }

    public Task<BpmnMetaDefinitionDto[]> GetAllBpmnMetaDefinitions()
    {
        return GetAsJsonAsyncSave<BpmnMetaDefinitionDto[]>("definition/meta");
    }

    public async Task<BpmnMetaDefinitionDto> CreateEmptyDefinition()
    {
        return await GetAsJsonAsyncSave<BpmnMetaDefinitionDto>("definition/new");
    }

    public async Task<BpmnDefinitionDto> UploadDefinition(string xml)
    {
        return await PostAsJsonAsyncSave<BpmnDefinitionDto>("definition",xml);
    }
    public async Task<BpmnDefinitionDto> DeployDefinition(string xml)
    {
        var apiStatusResult = await PostAsJsonAsyncSave<ApiStatusResult<BpmnDefinitionDto>>("definition/deploy",xml, true, false);
        if (apiStatusResult.Successful)
            return apiStatusResult.Result!;
        
        throw new ApiException(apiStatusResult.ErrorMessage);
    }


    public async Task<List<ProcessInstanceInfoDto>> GetAllRunningInstances()
    {
        return await GetAsJsonAndThrowOnErrorAsync<List<ProcessInstanceInfoDto>>("instance");
    }

    public async Task<ProcessInstanceInfoDto> GetProcessInstance(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<ProcessInstanceInfoDto>("instance/" + instanceGuid);
    }



    public async Task<MessageSubscriptionDto[]> GetMessageSubscriptions(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<MessageSubscriptionDto[]>("instance/" + instanceGuid + "/subscription/messages");
    }
    
    public async Task<TokenDto[]> GetServiceSubscriptions(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<TokenDto[]>("instance/" + instanceGuid + "/subscription/services");
    }

    public async Task<SignalSubscriptionDto[]> GetSignalSubscriptions(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<SignalSubscriptionDto[]>("instance/" + instanceGuid + "/subscription/singals");
    }

    public async Task<TokenDto[]> GetUserTasks(Guid instanceGuid)
    {
        return await GetAsJsonAndThrowOnErrorAsync<TokenDto[]>("instance/" + instanceGuid + "/subscription/userTasks");
    }


    public async Task<HttpResponseMessage> GetJsonRequest(string url, HttpMethod method, string body)
    {
        return await HttpClient.PostAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    }
}

public class ApiException(string? errorMessage) : Exception(errorMessage);
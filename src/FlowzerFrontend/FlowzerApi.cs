using System.Net.Http.Json;
using WebApiEngine.Shared;

namespace FlowzerFrontend;

public class FlowzerApi
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5182/")
    };

    internal async Task<ProcessInfoDto[]> GetModels()
    {
        return await GetFromJsonAsyncSave<ProcessInfoDto[]>($"model");
    }

    private async Task<T> GetFromJsonAsyncSave<T>(string url)
    {
        var result = await _httpClient.GetFromJsonAsync<T>(url);
        if (result == null)
        {
            throw new Exception($"Failed to get data from server. Returned object was null on request: {url}");
        }
        return result;
    }

    internal async Task<BpmnMetaDefinitionDto> GetMetaDefinitionById(string definitionId)
    {
        var bpmnMetaDefinitionDto = await _httpClient.GetFromJsonAsync<BpmnMetaDefinitionDto>($"definition/meta/{definitionId}");
        if (bpmnMetaDefinitionDto == null)
        {
            throw new Exception($"No meta definition found for definitionId {definitionId}");
        }
        return bpmnMetaDefinitionDto;
    }

    internal async Task<BpmnDefinitionDto> GetLatestDefinition(string definitionId)
    {
        var bpmnDefinitionDto = await _httpClient.GetFromJsonAsync<BpmnDefinitionDto>($"definition/meta/{definitionId}/latest");
        if (bpmnDefinitionDto == null)
        {
            throw new Exception($"No definition found for definitionId {definitionId}");
        }
        return bpmnDefinitionDto;
    }

    internal async Task<string> GetXmlDefinition(Guid guid)
    {
        return await _httpClient.GetStringAsync($"definition/xml/{guid}");
    }
}
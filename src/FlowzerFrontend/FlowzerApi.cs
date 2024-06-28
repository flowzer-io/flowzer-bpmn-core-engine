using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using WebApiEngine.Shared;

internal class FlowzerApi
{
    private readonly HttpClient _httpClient;

    public FlowzerApi()
    {
        _httpClient = new HttpClient()
        {
            BaseAddress = new Uri("http://localhost:5182/")
        };
    }

    public async Task<ProcessInfoDto[]> GetModels()
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

    public async Task<BpmnMetaDefinitionDto> GetMetaDefinitionById(string definitionId)
    {
        return await _httpClient.GetFromJsonAsync<BpmnMetaDefinitionDto>($"definition/meta/{definitionId}");
    }

    public async Task<BpmnDefinitionDto> GetLatestDefinition(string definitionId)
    {
        return await _httpClient.GetFromJsonAsync<BpmnDefinitionDto>($"definition/meta/{definitionId}/latest");
    }

    public async Task<string> GetXmlDefinition(Guid guid)
    {
        return await _httpClient.GetStringAsync($"definition/xml/{guid}");
    }
}
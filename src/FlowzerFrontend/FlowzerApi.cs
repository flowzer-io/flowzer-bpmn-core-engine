using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using WebApiEngine.Shared;

namespace FlowzerFrontend;

public class FlowzerApi: ApiBase
{
    internal async Task<ProcessInfoDto[]> GetModels()
    {
        return await GetFromJsonAsyncSave<ProcessInfoDto[]>($"model");
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
        return GetFromJsonAsyncSave<BpmnMetaDefinitionDto[]>("definition/meta");
    }

    public async Task<BpmnMetaDefinitionDto> CreateEmptyDefinition()
    {
        return await GetFromJsonAsyncSave<BpmnMetaDefinitionDto>("definition/new");
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


}

public class ApiException : Exception
{
    public ApiException(string? errorMessage): base(errorMessage)
    {
    }
        
}

public class ApiBase
{
    
    protected readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5182/")
    };
    
    protected async Task<T> GetFromJsonAsyncSave<T>(string url)
    {
        var result = await HttpClient.GetFromJsonAsync<T>(url);
        if (result == null)
        {
            throw new Exception($"Failed to get data from server. Returned object was null on request: {url}");
        }
        return result;
    }

    protected async Task<T> PostAsJsonAsyncSave<T>(string url, string value, bool isJson = false, bool throwOnUnsuccessfulStatusCodes = true)
    {
        return await SendAsyncSave<T>(url, value, HttpMethod.Post, isJson,throwOnUnsuccessfulStatusCodes);
    }    
    
    protected async Task<T> PostAsJsonAsyncSave<T>(string url, object value, bool throwOnUnsuccessfulStatusCodes = true)
    {
        var json = JsonSerializer.Serialize(value);
        return await SendAsyncSave<T>(url, json, HttpMethod.Post, true, throwOnUnsuccessfulStatusCodes);
    }    
    
    protected async Task<T> PutAsJsonAsyncSave<T>(string url, object value, bool throwOnUnsuccessfulStatusCodes = true)
    {
        var json = JsonSerializer.Serialize(value);
        return await SendAsyncSave<T>(url, json, HttpMethod.Put, true, throwOnUnsuccessfulStatusCodes);
    }
    
    protected async Task<T> PutAsJsonAsyncSave<T>(string url, string value, bool isJson = false, bool throwOnUnsuccessfulStatusCodes = true)
    {
        return await SendAsyncSave<T>(url, value, HttpMethod.Put, isJson, throwOnUnsuccessfulStatusCodes);
    }
    
    protected async Task<T> SendAsyncSave<T>(string url, string value, HttpMethod method, bool isJson, bool throwOnUnsuccessfulStatusCodes = true)
    {
        HttpResponseMessage result;
        HttpContent content;
        if (isJson)
        {
            content = new StringContent(value);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }
        else
        {
            content = new StringContent(value);
        }

        if (method == HttpMethod.Post)
            result = await HttpClient.PostAsync(url, content);
        else if (method == HttpMethod.Put)
            result = await HttpClient.PutAsync(url, content);
        else
            throw new Exception("Method not supported");
        
        if (result == null)
        {
            throw new Exception($"Failed to get data from server. Returned result was null on request: {url}");
        }

        var readFromJsonAsync = await result.Content.ReadFromJsonAsync<T>();
        
        if (readFromJsonAsync == null)
        {
            throw new Exception($"Failed to get data from server. Returned object was null on request: {url}");
        }
        
        if (throwOnUnsuccessfulStatusCodes &&  !result.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get data from server. Returned status code was {result.StatusCode} on request: {url}");
        }
        
        return readFromJsonAsync;
    }
    
}
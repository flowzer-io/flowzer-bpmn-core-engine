using System.Net.Http.Json;
using System.Text.Json;
using WebApiEngine.Shared;

namespace FlowzerFrontend;

public class ApiBase
{
    
    protected readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5182/")
    };


    #region Get
    
    protected async Task<T> GetAsJsonAndThrowOnErrorAsync<T>(string url)
    {
        var apiStatusResult = await GetAsJsonAsyncSave<ApiStatusResult<T>>(url);
        if (apiStatusResult.Successful)
            return apiStatusResult.Result!;
        
        throw new ApiException(apiStatusResult.ErrorMessage);
    }
    
    
    protected async Task<T> GetAsJsonAsyncSave<T>(string url)
    {
        var result = await HttpClient.GetFromJsonAsync<T>(url);
        if (result == null)
        {
            throw new Exception($"Failed to get data from server. Returned object was null on request: {url}");
        }
        return result;
    }
    
        
    public async Task<HttpResponseMessage> GetJsonRequest(string url, HttpMethod method, string body)
    {
        return await HttpClient.PostAsync(url, new StringContent(body, System.Text.Encoding.UTF8, "application/json"));
    }

    
    #endregion
    
    #region Post
    protected async Task<T> PostAsJsonAndOnErrorAsync<T>(string url, object value)
    {
        var json = JsonSerializer.Serialize(value);
        var apiStatusResult = await PostAsJsonAsyncSave<ApiStatusResult<T>>(url,json, true);
        if (apiStatusResult.Successful)
            return apiStatusResult.Result!;
        
        throw new ApiException(apiStatusResult.ErrorMessage);
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


    #endregion


    #region Put


    protected async Task<T> PutAsJsonAsyncSave<T>(string url, object value, bool throwOnUnsuccessfulStatusCodes = true)
    {
        var json = JsonSerializer.Serialize(value);
        return await SendAsyncSave<T>(url, json, HttpMethod.Put, true, throwOnUnsuccessfulStatusCodes);
    }
    
    protected async Task<T> PutAsJsonAsyncSave<T>(string url, string value, bool isJson = false, bool throwOnUnsuccessfulStatusCodes = true)
    {
        return await SendAsyncSave<T>(url, value, HttpMethod.Put, isJson, throwOnUnsuccessfulStatusCodes);
    }
    

    #endregion

    
    
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

        if (throwOnUnsuccessfulStatusCodes &&  !result.IsSuccessStatusCode)
        {
            throw new Exception($"Failed to get data from server. Returned status code was {result.StatusCode} on request: {url}");
        }
        
        var readFromJsonAsync = await result.Content.ReadFromJsonAsync<T>();
        
        if (readFromJsonAsync == null)
        {
            throw new Exception($"Failed to get data from server. Returned object was null on request: {url}");
        }
        
        
        return readFromJsonAsync;
    }
    
}
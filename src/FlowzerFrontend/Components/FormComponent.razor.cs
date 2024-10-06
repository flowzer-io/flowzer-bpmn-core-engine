using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FlowzerFrontend.Components;

public partial class FormComponent : ComponentBase
{
    
    [Inject] public required IJSRuntime JsRuntime { get; set; }
    [Inject] public required ILogger<FormComponent> Logger { get; set; }

    public string Debug { get; set; }
    
    [Parameter] public string Schema { get; set; } = string.Empty;
    [Parameter] public string OutData { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> OutDataChanged { get; set; } 

    
    
    private string _data = string.Empty;
    [Parameter] public string Data
    {
        get => _data;
        set
        {
            if (IgnoreDataChange)
                return;
            _data = value;
            _ = SetSubmissionData();
        }
    }

    public bool IgnoreDataChange { get; set; }

    [Parameter] public EventCallback<string> DataChanged { get; set; }

    protected override void OnAfterRender(bool firstRender)
    {
        if (firstRender)
        {
            var dotNetReference = DotNetObjectReference.Create(this);
            JsRuntime.InvokeVoidAsync("SetDotNetRef", dotNetReference);
        }
    }

    protected async override Task OnInitializedAsync()
    {
        await LoadWithSchema(Schema);
    }
    
    [JSInvokable]
    public async Task OnFormChange(object formData)
    {
        IgnoreDataChange = true;
        OutData = await GetSubmissionData();
        await OutDataChanged.InvokeAsync(OutData);
        IgnoreDataChange = false;
    }

    

    private async Task LoadWithSchema(string schema)
    {
        while (true)
        {
            try
            {
                var ret = await JsRuntime.InvokeAsyncNoneCached<bool>("executeInIframe", "#iframe_viewer", "IsReady");
                Logger.LogInformation("IsReady: {IsReady}", ret);
                if (ret)
                    break;
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, "Waiting for iframe to be ready");
            }

            await Task.Delay(100);
        }
        
        using var doc = JsonDocument.Parse(schema);
        await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "#iframe_viewer", "loadViewer",doc.RootElement);
        await SetSubmissionData();
    }

    private async Task SetSubmissionData()
    {
        using var doc = JsonDocument.Parse(Data);
        await JsRuntime.InvokeAsyncNoneCached<object>("executeInIframe", "#iframe_viewer", "setSubmission", doc.RootElement);
    }
    
    private async Task<string> GetSubmissionData()
    {
        var data = await JsRuntime.InvokeAsyncNoneCached<JsonElement>("executeInIframe", "#iframe_viewer", "getSubmission");
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }
    
    
}
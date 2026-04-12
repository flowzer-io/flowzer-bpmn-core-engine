using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FlowzerFrontend.Components;

public partial class FormComponent : ComponentBase, IAsyncDisposable
{
    private static readonly TimeSpan ViewerReadyTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ViewerReadyPollInterval = TimeSpan.FromMilliseconds(100);

    private readonly CancellationTokenSource _disposeCancellationTokenSource = new();
    private DotNetObjectReference<FormComponent>? _dotNetReference;
    private string? _loadedSchema;
    private string? _lastAppliedData;
    
    [Inject] public required IJSRuntime JsRuntime { get; set; }
    [Inject] public required ILogger<FormComponent> Logger { get; set; }

    public string Debug { get; set; } = string.Empty;
    
    [Parameter] public string Schema { get; set; } = string.Empty;
    [Parameter] public string OutData { get; set; } = string.Empty;
    [Parameter] public EventCallback<string> OutDataChanged { get; set; } 

    
    
    [Parameter] public string Data { get; set; } = string.Empty;

    public bool IgnoreDataChange { get; set; }

    [Parameter] public EventCallback<string> DataChanged { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _dotNetReference = DotNetObjectReference.Create(this);
            await JsRuntime.InvokeVoidAsync("SetDotNetRef", _dotNetReference);
        }

        try
        {
            if (!string.IsNullOrEmpty(Schema) && !string.Equals(_loadedSchema, Schema, StringComparison.Ordinal))
            {
                await LoadWithSchema(Schema, _disposeCancellationTokenSource.Token);
                _loadedSchema = Schema;
                _lastAppliedData = Data;
                return;
            }

            if (!IgnoreDataChange &&
                _loadedSchema != null &&
                !string.Equals(_lastAppliedData, Data, StringComparison.Ordinal))
            {
                await SetSubmissionData();
                _lastAppliedData = Data;
            }
        }
        catch (OperationCanceledException) when (_disposeCancellationTokenSource.IsCancellationRequested)
        {
            Logger.LogDebug("Form component rendering was cancelled during disposal.");
        }
        catch (TimeoutException exception)
        {
            Logger.LogWarning(exception, "Form viewer iframe did not become ready within the configured timeout.");
        }
    }
    
    [JSInvokable]
    public async Task OnFormChange(object formData)
    {
        IgnoreDataChange = true;
        OutData = await GetSubmissionData();
        await OutDataChanged.InvokeAsync(OutData);
        IgnoreDataChange = false;
    }

    

    private async Task LoadWithSchema(string schema, CancellationToken cancellationToken)
    {
        using var timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(ViewerReadyTimeout);

        while (true)
        {
            timeoutCancellationTokenSource.Token.ThrowIfCancellationRequested();

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

            try
            {
                await Task.Delay(ViewerReadyPollInterval, timeoutCancellationTokenSource.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("The form viewer iframe did not become ready in time.");
            }
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

    public async ValueTask DisposeAsync()
    {
        _disposeCancellationTokenSource.Cancel();

        try
        {
            await JsRuntime.InvokeVoidAsync("ClearDotNetRef");
        }
        catch (Exception exception) when (exception is JSDisconnectedException or InvalidOperationException)
        {
            Logger.LogDebug(exception, "The JS runtime was no longer available while disposing the form component.");
        }

        _dotNetReference?.Dispose();
        _disposeCancellationTokenSource.Dispose();
    }
}

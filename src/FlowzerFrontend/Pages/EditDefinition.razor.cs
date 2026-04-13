using System.Text.Json.Nodes;
using FlowzerFrontend.Exceptions;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class EditDefinition : IAsyncDisposable
{
    private const string EditorInteropPath = "window.FlowzerDefinitionEditor";

    [Parameter]
    public string? MetaDefinitionId { get; set; }
        
    [Parameter]
    public Guid? DefinitionId { get; set; }

    [Inject]
    public required IJSRuntime JsRuntime { get; set; }

    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }
    
    private FluentMenuButton _menubutton = new();
 
    public string? ErrorString { get; set; }
    public bool IsDocumentLoading { get; set; } = true;
    private bool IsEditorInitialized { get; set; }
    private string? PendingXml { get; set; }
    private string? LastFailedXmlImport { get; set; }
    private bool IsPersistingDefinition { get; set; }
    private bool NeedsEditorInitialization { get; set; } = true;

    private BpmnMetaDefinitionDto CurrentMetaDefinition { get; set; } = CreateLoadingMetaDefinition();

    private BpmnDefinitionDto CurrentDefinition { get; set; } = CreateEmptyDefinition();

    public List<BpmnMetaDefinitionDto> AvailableVersions { get; set; } = new();

    private bool CanEditMetadata => !IsDocumentLoading && string.IsNullOrWhiteSpace(ErrorString);
    private bool CanPersistDefinition =>
        !IsDocumentLoading &&
        !IsPersistingDefinition &&
        IsEditorInitialized &&
        string.IsNullOrWhiteSpace(ErrorString);

    protected override async Task OnParametersSetAsync()
    {
        if (IsEditorInitialized)
        {
            await DisposeEditorAsync();
            IsEditorInitialized = false;
        }

        NeedsEditorInitialization = true;
        await LoadModel();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (NeedsEditorInitialization)
        {
            NeedsEditorInitialization = false;
            await TryInitializeEditorAsync();
        }

        if (!IsEditorInitialized || string.IsNullOrWhiteSpace(PendingXml))
        {
            return;
        }

        var xml = PendingXml;

        if (string.Equals(LastFailedXmlImport, xml, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            await LoadDiagramXml(xml);
            PendingXml = null;
            LastFailedXmlImport = null;
            ErrorString = null;
        }
        catch (Exception exception)
        {
            LastFailedXmlImport = xml;
            PendingXml = xml;
            ErrorString = $"Failed to load BPMN XML into the editor: {exception.Message}";
            await SafeStateHasChangedAsync();
        }
    }

    private async Task TryInitializeEditorAsync()
    {
        try
        {
            await JsRuntime.EvalCodeBehindJsScripts(this);
            await JsRuntime.InvokeVoidAsyncNoneCached($"{EditorInteropPath}.initialize");
            IsEditorInitialized = true;
            ErrorString = null;
            await SafeStateHasChangedAsync();
        }
        catch (Exception exception)
        {
            IsEditorInitialized = false;
            ErrorString = $"Failed to initialize the BPMN editor: {exception.Message}";
            await SafeStateHasChangedAsync();
        }
    }

    private async Task LoadModel()
    {
        IsDocumentLoading = true;
        ErrorString = null;
        PendingXml = null;
        LastFailedXmlImport = null;
        CurrentMetaDefinition = CreateLoadingMetaDefinition(MetaDefinitionId);
        CurrentDefinition = CreateEmptyDefinition();

        try
        {
            if (string.IsNullOrWhiteSpace(MetaDefinitionId))
            {
                ErrorString = "No model identifier was provided.";
                CurrentMetaDefinition = CreateUnavailableMetaDefinition();
                return;
            }

            CurrentMetaDefinition = await FlowzerApi.GetMetaDefinitionById(MetaDefinitionId);
            CurrentDefinition = DefinitionId.HasValue
                ? await FlowzerApi.GetDefinition(DefinitionId.Value)
                : await FlowzerApi.GetLatestDefinition(MetaDefinitionId);

            var xml = await FlowzerApi.GetXmlDefinition(CurrentDefinition.Id);
            if (string.IsNullOrWhiteSpace(xml))
            {
                ErrorString = "No XML found for this model.";
                return;
            }

            PendingXml = xml;
        }
        catch (Exception exception)
        {
            CurrentMetaDefinition = CreateUnavailableMetaDefinition(MetaDefinitionId);
            CurrentDefinition = CreateEmptyDefinition();
            ErrorString = $"Could not load model. {exception.Message}";
        }
        finally
        {
            IsDocumentLoading = false;
            await SafeStateHasChangedAsync();
        }
    }

    private async Task LoadDiagramXml(string xml)
    {
        await JsRuntime.InvokeVoidAsyncNoneCached($"{EditorInteropPath}.importXml", xml);
        ErrorString = null;
    }

    private async Task OnSaveClick()
    {
        await PersistDefinitionAsync((xml, previousGuid) => FlowzerApi.UploadDefinition(xml, previousGuid));
    }

    private async Task OnDeployClick()
    {
        await PersistDefinitionAsync((xml, previousGuid) => FlowzerApi.DeployDefinition(xml, previousGuid));
    }

    private async Task PersistDefinitionAsync(Func<string, Guid?, Task<BpmnDefinitionDto>> persistDefinition)
    {
        if (!CanPersistDefinition)
        {
            var dialog = await DialogService.ShowErrorAsync("The BPMN editor is not ready yet.", "Error");
            await dialog.Result;
            return;
        }

        IsPersistingDefinition = true;
        await SafeStateHasChangedAsync();

        try
        {
            var xmlData = await GetXmlData();
            var persistedDefinition = await persistDefinition(xmlData, GetPreviousDefinitionGuid());

            CurrentDefinition = persistedDefinition;
            DefinitionId = persistedDefinition.Id;
            PendingXml = xmlData;
            LastFailedXmlImport = null;
            ErrorString = null;

            var targetUrl = UriHelper.GetEditDefinitionUrl(CurrentMetaDefinition.DefinitionId, persistedDefinition.Id);
            NavigationManager.NavigateTo(targetUrl, replace: true);
        }
        catch (ApiException exception)
        {
            var dialog = await DialogService.ShowErrorAsync(exception.Message, "Error");
            await dialog.Result;
        }
        catch (Exception exception)
        {
            var dialog = await DialogService.ShowErrorAsync(exception.Message, "Error");
            await dialog.Result;
        }
        finally
        {
            IsPersistingDefinition = false;
            await SafeStateHasChangedAsync();
        }
    }

    private async Task<string> GetXmlData()
    {
        var saveXmlResult = await JsRuntime.InvokeAsyncNoneCached<JsonObject>($"{EditorInteropPath}.saveXml");
        if (saveXmlResult["xml"] is not JsonNode xmlNode)
        {
            throw new InvalidOperationException("The BPMN editor returned no XML payload.");
        }

        return xmlNode.GetValue<string>();
    }

    private Guid? GetPreviousDefinitionGuid()
    {
        return CurrentDefinition.Id == Guid.Empty ? null : CurrentDefinition.Id;
    }

    private async Task OnRetryClick()
    {
        ErrorString = null;

        if (!IsEditorInitialized)
        {
            NeedsEditorInitialization = true;
            await LoadModel();
            return;
        }

        await LoadModel();
    }

    private async Task AfterChanged(string _)
    {
        if (!CanEditMetadata)
        {
            return;
        }

        await FlowzerApi.UpdateMetaDefinition(CurrentMetaDefinition);
    }

    private static void OnSaveMenuChoosen(MenuChangeEventArgs _)
    {
        // Platzhalter für spätere Optionsmenü-Aktionen.
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeEditorAsync();
    }

    private async Task DisposeEditorAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsyncNoneCached($"{EditorInteropPath}.dispose");
        }
        catch
        {
            // Best effort only: the page may already be gone while the browser tears down the component.
        }
    }

    private async Task SafeStateHasChangedAsync()
    {
        await InvokeAsync(StateHasChanged);
    }

    private static BpmnMetaDefinitionDto CreateLoadingMetaDefinition(string? definitionId = null)
    {
        return new BpmnMetaDefinitionDto
        {
            Description = string.Empty,
            Name = "Loading...",
            DefinitionId = string.IsNullOrWhiteSpace(definitionId) ? "Loading..." : definitionId
        };
    }

    private static BpmnMetaDefinitionDto CreateUnavailableMetaDefinition(string? definitionId = null)
    {
        return new BpmnMetaDefinitionDto
        {
            Description = string.Empty,
            Name = "Unavailable model",
            DefinitionId = string.IsNullOrWhiteSpace(definitionId) ? "Unavailable model" : definitionId
        };
    }

    private static BpmnDefinitionDto CreateEmptyDefinition()
    {
        return new BpmnDefinitionDto
        {
            DefinitionId = "Loading...",
            Hash = "Loading...",
            Id = Guid.Empty,
            PreviousGuid = Guid.Empty,
            SavedByUser = Guid.Empty,
            SavedOn = DateTime.UtcNow,
            Version = new VersionDto(0, 0)
        };
    }
}

using System.Text.Json;
using FlowzerFrontend.BusinessLogic;
using FlowzerFrontend.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;
namespace FlowzerFrontend.Pages;

public partial class Instance : IAsyncDisposable
{
    private const string ViewerInteropPath = "window.FlowzerInstanceViewer";

    private ProcessInstanceInfoDto? _instance;
    private bool IsViewerInitialized { get; set; }
    private bool NeedsViewerInitialization { get; set; } = true;
    private string? PendingXml { get; set; }

    [Parameter] public Guid InstanceGuid { get; set; }

    [Inject] public required IJSRuntime JsRuntime { get; set; }

    [Inject] public required FlowzerApi FlowzerApi { get; set; }

    public IEnumerable<ITreeViewItem> VariableItems = [];
    
    public string? VariableContent;

    private IEnumerable<ITreeViewItem> Items = [];
    
    public string ErrorString { get; set; } = string.Empty;

    private ITreeViewItem? _currentSelectedToken;
    public ITreeViewItem? CurrentSelectedToken
    {
        get => _currentSelectedToken;
        set
        {
            _currentSelectedToken = value;
            VariableContent = LoadVariablesTreeForToken(_currentSelectedToken?.Id);
        }
    }

    private string? LoadVariablesTreeForToken(string? tokenId)
    {
        if (string.IsNullOrEmpty(tokenId))
            return null;
        
        var token = _instance?.Tokens.FirstOrDefault(x => x.Id.ToString() == tokenId);
        if (token?.OutputData == null)
            return null;

        return JsonSerializer.Serialize(token.OutputData, new JsonSerializerOptions() {WriteIndented = true});

    }

    private bool _trapFocus = true;
    private bool _modal = true;
    private readonly Dictionary<string, object> _treeItemMappings = new();

    protected override async Task OnParametersSetAsync()
    {
        if (IsViewerInitialized)
        {
            await DisposeViewerAsync();
            IsViewerInitialized = false;
        }

        NeedsViewerInitialization = true;
        await Reload();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        try
        {
            if (NeedsViewerInitialization)
            {
                NeedsViewerInitialization = false;
                await TryInitializeViewerAsync();
            }

            await TryRenderInstanceVisualizationAsync();
        }
        catch (Exception exception)
        {
            ErrorString = $"Could not render instance diagram. {exception.Message}";
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task TryInitializeViewerAsync()
    {
        await JsRuntime.EvalCodeBehindJsScripts(this);
        await InitViewer();
        IsViewerInitialized = true;
    }

    private async Task LoadData()
    {
        _instance = await FlowzerApi.GetProcessInstance(InstanceGuid);
        var data = await FlowzerApi.GetXmlDefinition(_instance.DefinitionId);
        PendingXml = data;
        await ShowVariables(_instance.Tokens);
        LoadSubscriptionOverview();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ShowVariables(List<TokenDto> instanceTokens)
    {
        VariableItems = InstanceTreeViewBuilder.BuildTokenItems(instanceTokens);
        
        await InvokeAsync(StateHasChanged);
    }

    private void LoadSubscriptionOverview()
    {
        if (_instance == null)
            return;

        var subscriptionOverview = InstanceTreeViewBuilder.BuildSubscriptionOverview(_instance);
        foreach (var item in subscriptionOverview)
        {
            item.OnExpandedAsync = OnSubscriptionItemExpanded;
        }

        Items = subscriptionOverview;
    }

    private async Task OnSubscriptionItemExpanded(TreeViewItemExpandedEventArgs e)
    {
        if (_instance == null)
            return;

        if (!InstanceTreeViewBuilder.TryParseSubscriptionCategory(e.CurrentItem.Id, out var category))
        {
            e.CurrentItem.Items = [];
            ErrorDialog($"Unsupported tree item category '{e.CurrentItem.Id}'.");
            return;
        }

        e.CurrentItem.Items = await LoadSubscriptionItems(category);
    }

    private async Task<IReadOnlyList<ITreeViewItem>> LoadSubscriptionItems(InstanceSubscriptionTreeCategory category)
    {
        if (_instance == null)
            return [];

        return category switch
        {
            InstanceSubscriptionTreeCategory.Messages => InstanceTreeViewBuilder.BuildMessageSubscriptionItems(
                await FlowzerApi.GetMessageSubscriptions(_instance.InstanceId),
                _treeItemMappings),
            InstanceSubscriptionTreeCategory.Services => InstanceTreeViewBuilder.BuildServiceSubscriptionItems(
                await FlowzerApi.GetServiceSubscriptions(_instance.InstanceId)),
            InstanceSubscriptionTreeCategory.Signals => InstanceTreeViewBuilder.BuildSignalSubscriptionItems(
                await FlowzerApi.GetSignalSubscriptions(_instance.InstanceId)),
            InstanceSubscriptionTreeCategory.UserTasks => InstanceTreeViewBuilder.BuildUserTaskSubscriptionItems(
                await FlowzerApi.GetUserTasks(_instance.InstanceId)),
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unsupported subscription category.")
        };
    }


    private async Task ShowTokens(List<TokenDto> instanceTokens)
    {
        await ClearTokens();
        try
        {
            foreach (var instanceToken in instanceTokens.Where(x => IsActive(x.State)).GroupBy(x => x.CurrentFlowNodeId))
            {
                await AddToken(instanceToken.Key, instanceToken.Count());
            }
        }
        catch (Exception e)
        {
            ErrorDialog(e.Message);
        }
    }

    private bool IsActive(FlowNodeStateDto state)
    {
        return state == FlowNodeStateDto.Active;
    }

    private async Task AddToken(string? instanceTokenKey, int instanceTokensCount)
    {
        if (string.IsNullOrEmpty(instanceTokenKey) || !IsViewerInitialized)
            return;
        
        try
        {
            _ = await JsRuntime.InvokeAsyncNoneCached<bool>($"{ViewerInteropPath}.addToken", instanceTokenKey, instanceTokensCount);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException($"Error adding token '{instanceTokenKey}'.", e);
        }
    }

    private async Task ClearTokens()
    {
        if (!IsViewerInitialized)
            return;

        await JsRuntime.InvokeVoidAsyncNoneCached($"{ViewerInteropPath}.clearTokens");
    }

    private async Task InitViewer()
    {
        await JsRuntime.InvokeVoidAsyncNoneCached($"{ViewerInteropPath}.initialize");
    }

    private async Task LoadDiagramXml(string xml)
    {
        await JsRuntime.InvokeVoidAsyncNoneCached($"{ViewerInteropPath}.importXml", xml);
        ErrorString = string.Empty;
    }

    private async Task OpenSendRestRequestAsync(RestExampleRequest restData)
    {

        DialogParameters parameters = new()
        {
            Title = $"Send Event",
            Width = "80%",
            TrapFocus = _trapFocus,
            Modal = _modal,
            PreventScroll = false
        };

        var restDialogParams = new RestDialogParams()
        {
            RestExampleRequest = restData
        };
        var dialog =  await DialogService.ShowDialogAsync<RestDialog>(restDialogParams, parameters);
        await dialog.Result;
        await LoadData();

    }

    private bool GetTreeRelatedItem<T>(string id, out T o)
    {
        if (_treeItemMappings.TryGetValue(id, out var obj))
        {
            if (obj is T castedT)
            {
                o = castedT;
                return true;
            }
        }
        o = default!;
        return false;
    }

    private async Task Reload()
    {
        try
        {
            await LoadData();
        }
        catch (Exception exception)
        {
            _instance = null;
            PendingXml = null;
            Items = [];
            VariableItems = [];
            VariableContent = null;
            ErrorString = $"Could not load instance details. {exception.Message}";
        }

        try
        {
            await TryRenderInstanceVisualizationAsync();
        }
        catch (Exception exception)
        {
            ErrorString = $"Could not render instance diagram. {exception.Message}";
        }
    }

    private async Task TryRenderInstanceVisualizationAsync()
    {
        if (!IsViewerInitialized || _instance == null || string.IsNullOrWhiteSpace(PendingXml))
        {
            return;
        }

        var xml = PendingXml;
        PendingXml = null;

        await LoadDiagramXml(xml);
        await ShowTokens(_instance.Tokens);
        ErrorString = string.Empty;
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRetryClick()
    {
        ErrorString = string.Empty;

        if (!IsViewerInitialized)
        {
            NeedsViewerInitialization = true;
            await Reload();
            return;
        }

        await Reload();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeViewerAsync();
    }

    private async Task DisposeViewerAsync()
    {
        try
        {
            await JsRuntime.InvokeVoidAsyncNoneCached($"{ViewerInteropPath}.dispose");
        }
        catch
        {
            // Best effort only: during page teardown the runtime may already be unavailable.
        }
    }
}

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
    private bool IsRefreshing { get; set; }

    [Parameter] public Guid InstanceGuid { get; set; }

    [Inject] public required IJSRuntime JsRuntime { get; set; }
    [Inject] public required FlowzerApi FlowzerApi { get; set; }
    [Inject] public required NavigationManager NavigationManager { get; set; }

    public IEnumerable<ITreeViewItem> VariableItems = [];
    private IEnumerable<ITreeViewItem> Items = [];

    private string? ErrorHeadline { get; set; }
    private string? ErrorDetails { get; set; }
    private string SelectedTokenTitle { get; set; } = "Select a token";
    private string SelectedTokenFlowNodeId { get; set; } = string.Empty;
    private string SelectedTokenVariablesJson { get; set; } = string.Empty;
    private string SelectedTokenOutputJson { get; set; } = string.Empty;
    private bool SelectedTokenHasVariables { get; set; }
    private bool SelectedTokenHasOutputData { get; set; }

    private ITreeViewItem? _currentSelectedToken;
    public ITreeViewItem? CurrentSelectedToken
    {
        get => _currentSelectedToken;
        set
        {
            _currentSelectedToken = value;
            ApplyTokenSelection(_currentSelectedToken?.Id);
        }
    }

    private bool _trapFocus = true;
    private bool _modal = true;
    private readonly Dictionary<string, object> _treeItemMappings = new();

    private string InstanceTitle => _instance == null
        ? "Instance"
        : $"{_instance.RelatedDefinitionName} · {_instance.InstanceId}";
    private string InstanceStateLabel => _instance == null
        ? "Loading"
        : ProcessInstanceStateViewHelper.GetLabel(_instance.State);
    private string InstanceStateToneClass => _instance == null
        ? "status-pill-neutral"
        : ProcessInstanceStateViewHelper.GetToneClass(_instance.State);
    private int TokenCount => _instance?.Tokens.Count ?? 0;
    private int ActiveTokenCount => _instance?.Tokens.Count(token => token.State == FlowNodeStateDto.Active) ?? 0;
    private int WaitingSubscriptionCount => _instance == null
        ? 0
        : _instance.MessageSubscriptionCount +
          _instance.SignalSubscriptionCount +
          _instance.UserTaskSubscriptionCount +
          _instance.ServiceSubscriptionCount;
    private bool HasError => !string.IsNullOrWhiteSpace(ErrorHeadline);

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
            SetError("Could not render the instance diagram", exception.Message);
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
        _treeItemMappings.Clear();
        _instance = await FlowzerApi.GetProcessInstance(InstanceGuid);
        PendingXml = await FlowzerApi.GetXmlDefinition(_instance.DefinitionId);
        await ShowVariables(_instance.Tokens);
        LoadSubscriptionOverview();
        await InvokeAsync(StateHasChanged);
    }

    private async Task ShowVariables(List<TokenDto> instanceTokens)
    {
        VariableItems = InstanceTreeViewBuilder.BuildTokenItems(instanceTokens);
        CurrentSelectedToken = VariableItems.FirstOrDefault();
        await InvokeAsync(StateHasChanged);
    }

    private void ApplyTokenSelection(string? tokenId)
    {
        var token = _instance?.Tokens.FirstOrDefault(candidate => candidate.Id.ToString() == tokenId);
        var selectionDetails = TokenSelectionViewHelper.Create(token);
        SelectedTokenTitle = selectionDetails.Title;
        SelectedTokenFlowNodeId = selectionDetails.FlowNodeId;
        SelectedTokenVariablesJson = selectionDetails.VariablesJson;
        SelectedTokenOutputJson = selectionDetails.OutputJson;
        SelectedTokenHasVariables = selectionDetails.HasVariables;
        SelectedTokenHasOutputData = selectionDetails.HasOutputData;
    }

    private void LoadSubscriptionOverview()
    {
        if (_instance == null)
        {
            Items = [];
            return;
        }

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
            foreach (var instanceToken in instanceTokens.Where(token => token.State == FlowNodeStateDto.Active).GroupBy(token => token.CurrentFlowNodeId))
            {
                await AddToken(instanceToken.Key, instanceToken.Count());
            }
        }
        catch (Exception e)
        {
            ErrorDialog(e.Message);
        }
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
        ClearError();
    }

    private async Task OpenSendRestRequestAsync(RestExampleRequest restData)
    {
        DialogParameters parameters = new()
        {
            Title = "Send Event",
            Width = "80%",
            TrapFocus = _trapFocus,
            Modal = _modal,
            PreventScroll = false
        };

        var restDialogParams = new RestDialogParams()
        {
            RestExampleRequest = restData
        };
        var dialog = await DialogService.ShowDialogAsync<RestDialog>(restDialogParams, parameters);
        await dialog.Result;
        await LoadData();
    }

    private bool GetTreeRelatedItem<T>(string id, out T o)
    {
        if (_treeItemMappings.TryGetValue(id, out var obj) && obj is T castedT)
        {
            o = castedT;
            return true;
        }

        o = default!;
        return false;
    }

    private async Task Reload()
    {
        IsRefreshing = true;
        ClearError();

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
            CurrentSelectedToken = null;
            SetError("Could not load instance details", exception.Message);
        }
        finally
        {
            IsRefreshing = false;
        }

        try
        {
            await TryRenderInstanceVisualizationAsync();
        }
        catch (Exception exception)
        {
            SetError("Could not render the instance diagram", exception.Message);
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
        ClearError();
        await InvokeAsync(StateHasChanged);
    }

    private async Task OnRetryClick()
    {
        ClearError();

        if (!IsViewerInitialized)
        {
            NeedsViewerInitialization = true;
            await Reload();
            return;
        }

        await Reload();
    }

    private void OnOpenWorkflowClick()
    {
        if (_instance == null)
        {
            return;
        }

        NavigationManager.NavigateTo(UriHelper.GetEditDefinitionUrl(_instance.RelatedDefinitionId, _instance.DefinitionId));
    }

    private void SetError(string headline, string details)
    {
        ErrorHeadline = headline;
        ErrorDetails = details;
    }

    private void ClearError()
    {
        ErrorHeadline = null;
        ErrorDetails = null;
    }

    private async Task OnRefreshClickAsync()
    {
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

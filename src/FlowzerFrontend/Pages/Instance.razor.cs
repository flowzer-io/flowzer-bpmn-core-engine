using System.Text.Json;
using FlowzerFrontend.BusinessLogic;
using FlowzerFrontend.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using Microsoft.JSInterop;
using WebApiEngine.Shared;
namespace FlowzerFrontend.Pages;

public partial class Instance
{
    private const string MessagesTreeItemKey = "messages";
    private const string ServiceTreeItemKey = "service";
    private const string SignalTreeItemKey = "singal";
    private const string UserTaskTreeItemKey = "user";

    private ProcessInstanceInfoDto? _instance;

    [Parameter] public Guid InstanceGuid { get; set; }

    [Inject] public required IJSRuntime JsRuntime { get; set; }

    [Inject] public required IDialogService DialogService { get; set; }

    
    [Inject] public required FlowzerApi FlowzerApi { get; set; }

    
    public IEnumerable<ITreeViewItem>? VariableItems = new List<ITreeViewItem>();
    
    public string? VariableContent;

    private IEnumerable<ITreeViewItem>? Items = new List<ITreeViewItem>();
    
    public string ErrorString { get; set; }

    
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
    private ITreeViewItem? _selectedItem;
    private  Dictionary<string, object> _treeItemMappings = new Dictionary<string, object>();
    


    protected override async Task OnInitializedAsync()
    {
        await JsRuntime.EvalCodeBehindJsScripts(this);
        await Reload();
    }

    private async Task LoadData()
    {
        _instance = await FlowzerApi.GetProcessInstance(InstanceGuid);
        var data = await FlowzerApi.GetXmlDefinition(_instance.DefinitionId);
        await LoadDiagramXml(data);
        await ShowTokens(_instance.Tokens);
        await ShowVariables(_instance.Tokens);
        LoadMeesageSubscriptionOverview();
    }

    private async Task ShowVariables(List<TokenDto> instanceTokens)
    {
        var rootTokens = instanceTokens.Where(x => x.ParentTokenId == null || x.ParentTokenId == Guid.Empty).ToList();
        if (rootTokens.Count != 1)
        {
            throw new Exception("There should be exactly one root token");
            return;
        }
        VariableItems =  GetTokenTreeViewItem(instanceTokens, rootTokens);
        
        await InvokeAsync(StateHasChanged);
    }

    private IEnumerable<ITreeViewItem>? GetTokenTreeViewItem(List<TokenDto> allTokens, List<TokenDto> list)
    {
        var result = new List<ITreeViewItem>();
        foreach (var x in list)
        {
            var text = x.CurrentFlowElement.GetValue<string>("Name", null);
            if (string.IsNullOrEmpty(text))
                text = x.CurrentFlowElement.GetValue<string>("Id", "(root token)")!;

            var subTokens = allTokens.Where(y => y.ParentTokenId == x.Id).ToList();
            var subItems = GetTokenTreeViewItem(allTokens, subTokens);
            result.Add(new TreeViewItem()
            {
                Id = x.Id.ToString(),
                Text = text,
                Items = subItems,
                Expanded = true
            });
        }
        return result;
    }

    private void LoadMeesageSubscriptionOverview()
    {
        if (_instance == null)
            return;
        
        var messageSubscriptionsTreeItem = new TreeViewItem()
        {
            Id = MessagesTreeItemKey,
            Text = "Message subscriptions (" + _instance.MessageSubscriptionCount + ")",
            OnExpandedAsync = OnMessageSubscriptionItemExpanded,
        };
        if (_instance.MessageSubscriptionCount > 0)
            messageSubscriptionsTreeItem.Items = TreeViewItem.LoadingTreeViewItems;

        var serviceSubscriptiontTreeItem = new TreeViewItem()
        {
            Id = ServiceTreeItemKey,
            Text = "Service subscriptions (" + _instance.ServiceSubscriptionCount + ")",
            OnExpandedAsync = OnMessageSubscriptionItemExpanded,
        };
        if (_instance.ServiceSubscriptionCount > 0)
            serviceSubscriptiontTreeItem.Items = TreeViewItem.LoadingTreeViewItems;


        var signalSubscriptionTreeItem = new TreeViewItem()
        {
            Id = SignalTreeItemKey,
            Text = "Signal subscriptions (" + _instance.SignalSubscriptionCount + ")",
            OnExpandedAsync = OnMessageSubscriptionItemExpanded,
        };
        if (_instance.SignalSubscriptionCount > 0)
            signalSubscriptionTreeItem.Items = TreeViewItem.LoadingTreeViewItems;


        var userTaskSubscriptionTreeItem = new TreeViewItem()
        {
            Id = UserTaskTreeItemKey,
            Text = "Usertask subscriptions (" + _instance.UserTaskSubscriptionCount + ")",
            OnExpandedAsync = OnMessageSubscriptionItemExpanded,
        };
        if (_instance.UserTaskSubscriptionCount > 0)
            userTaskSubscriptionTreeItem.Items = TreeViewItem.LoadingTreeViewItems;

        Items = new[]
        {
            messageSubscriptionsTreeItem,
            serviceSubscriptiontTreeItem,
            signalSubscriptionTreeItem,
            userTaskSubscriptionTreeItem
        };
    }

    private async Task OnMessageSubscriptionItemExpanded(TreeViewItemExpandedEventArgs e)
    {
        if (_instance == null)
            return;


        e.CurrentItem.Items = e.CurrentItem.Id switch
        {
            MessagesTreeItemKey => (await LoadMessageSubscriptions()).ToList(),
            ServiceTreeItemKey => await LoadServiceSubscriptions(),
            SignalTreeItemKey => await LoadSignalSubscriptions(),
            UserTaskTreeItemKey => await LoadUserTaskSubscriptions(),
            _ => throw new Exception("Unknown item type")
        };

    }

    private async Task<IEnumerable<ITreeViewItem>?> LoadMessageSubscriptions()
    {
        if (_instance == null)
            return null;
        return (await FlowzerApi.GetMessageSubscriptions(_instance.InstanceId)).Select(
            x =>
            {
                var id = "message_" + x.Message.Name;
                _treeItemMappings[id] = x;
                var treeViewItem = new TreeViewItem(id, x.Message.Name);
                return treeViewItem;
            });
    }

    private async Task<IEnumerable<ITreeViewItem>?> LoadServiceSubscriptions()
    {
        if (_instance == null)
            return new List<ITreeViewItem>();
        return (await FlowzerApi.GetServiceSubscriptions(_instance.InstanceId)).Select(
            x =>
            {
                var implementationName = (string)(((IDictionary<string, object>)x.CurrentFlowElement!)!)["Implementation"];
                var treeViewItem = new TreeViewItem(x.Id.ToString(), implementationName);
                return treeViewItem;
            });
    }


    private async Task<IEnumerable<ITreeViewItem>?> LoadSignalSubscriptions()
    {
        if (_instance == null)
            return new List<ITreeViewItem>();
        return (await FlowzerApi.GetSignalSubscriptions(_instance.InstanceId)).Select(
            x =>
            {
                var treeViewItem = new TreeViewItem(x.Signal, x.Signal);
                return treeViewItem;
            });
    }

    private async Task<IEnumerable<ITreeViewItem>?> LoadUserTaskSubscriptions()
    {
        if (_instance == null)
            return new List<ITreeViewItem>();
        
        
        return (await FlowzerApi.GetUserTasks(_instance.InstanceId)).Select(
            x =>
            {
                var implementationName = (string)(((IDictionary<string, object>)x.CurrentFlowElement!)!)["Implementation"];
                var treeViewItem = new TreeViewItem(x.Id.ToString(), implementationName);
                return treeViewItem;
            });
    }


    private async Task ShowTokens(List<TokenDto> instanceTokens)
    {
        await ClearTokens();
        foreach (var instanceToken in instanceTokens.Where(x => IsActive(x.State)).GroupBy(x => x.CurrentFlowNodeId))
        {
            await AddToken(instanceToken.Key, instanceToken.Count());
        }
    }

    private bool IsActive(FlowNodeStateDto state)
    {
        return state == FlowNodeStateDto.Active;
    }

    private async Task AddToken(string? instanceTokenKey, int instanceTokensCount)
    {
        if (instanceTokenKey == null)
            return;
        await JsRuntime.InvokeVoidAsync("addToken", instanceTokenKey, instanceTokensCount);
    }
    private async Task ClearTokens()
    {
        await JsRuntime.InvokeVoidAsync("clearTokens");
    }

    private async Task InitViewer()
    {
        await JsRuntime.InvokeVoidAsync("InitViewer");

    }

    private async Task LoadDiagramXml(string xml)
    {
        await JsRuntime.InvokeVoidAsync("window.bpmnViewer.importXML", xml);
        ErrorString = null;
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
        await InitViewer();
        await LoadData();
    }
}


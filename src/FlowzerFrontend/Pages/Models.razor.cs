using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Models 
{
    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    public IEnumerable<ExtendedBpmnMetaDefinitionDto> SelectedItems { get; set; } = new List<ExtendedBpmnMetaDefinitionDto>(); 
    private List<ExtendedBpmnMetaDefinitionDto> AllModels { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private string? ActionErrorMessage { get; set; }
    private string? ActionInfoMessage { get; set; }
    private string? StartingDefinitionId { get; set; }
    public string? SearchText { get; set; }
    
    public List<Option<SortDirection>> SortDirections = new()
    {
        new Option<SortDirection> { Value = SortDirection.Ascending, Text = "Ascending" },
        new Option<SortDirection> { Value = SortDirection.Descending, Text = "Descending"}
    };
    
    public Option<SortDirection>? SelectedSortDirection { get; set; }
    public string? SelectedSortDirectionString { get; set; }
    public IQueryable<ExtendedBpmnMetaDefinitionDto> Data =>
        ModelListViewHelper
            .ApplyQuery(AllModels, SearchText, SelectedSortDirection?.Value ?? SortDirection.Ascending)
            .AsQueryable();

    protected override void OnInitialized()
    {
        SelectedSortDirection = SortDirections.First();
        SelectedSortDirectionString = SelectedSortDirection.Text;
    }
    
    protected override async Task OnParametersSetAsync()
    {
        await LoadData();
    }

    private async Task OnNewClick()
    {
        var newDefinitionMetaData = await FlowzerApi.CreateEmptyDefinition();
        NavigationManager.NavigateTo(UriHelper.GetEditDefinitionUrl(newDefinitionMetaData.DefinitionId));
        
    }

    private void OnOpenLatestClick(ExtendedBpmnMetaDefinitionDto model)
    {
        ClearActionMessages();
        NavigationManager.NavigateTo(UriHelper.GetEditDefinitionUrl(model.DefinitionId));
    }

    private void OnOpenDeployedClick(ExtendedBpmnMetaDefinitionDto model)
    {
        ClearActionMessages();

        if (!model.DeployedId.HasValue)
        {
            ActionErrorMessage = $"Workflow \"{model.Name}\" is not deployed yet.";
            return;
        }

        NavigationManager.NavigateTo(UriHelper.GetEditDefinitionUrl(model.DefinitionId, model.DeployedId));
    }

    private async Task OnStartInstanceClickAsync(ExtendedBpmnMetaDefinitionDto model)
    {
        ClearActionMessages();

        if (!model.DeployedId.HasValue)
        {
            ActionErrorMessage = $"Workflow \"{model.Name}\" must be deployed before it can be started.";
            return;
        }

        StartingDefinitionId = model.DefinitionId;
        try
        {
            var instance = await FlowzerApi.StartProcessInstance(model.DefinitionId);
            ActionInfoMessage = $"Started instance {instance.InstanceId} for workflow \"{model.Name}\".";
            NavigationManager.NavigateTo(UriHelper.GetShowInstanceUrl(instance.InstanceId));
        }
        catch (Exception exception)
        {
            ActionErrorMessage = $"Could not start workflow \"{model.Name}\". {exception.Message}";
        }
        finally
        {
            StartingDefinitionId = null;
        }
    }

    private bool IsStartActionBusy(ExtendedBpmnMetaDefinitionDto model)
    {
        return string.Equals(StartingDefinitionId, model.DefinitionId, StringComparison.Ordinal);
    }
    
    private async Task LoadData()
    {
        IsLoading = true;
        ClearActionMessages();
        try
        {
            AllModels = (await FlowzerApi.GetAllBpmnMetaDefinitions()).ToList();
            LoadErrorMessage = null;
        }
        catch (Exception exception)
        {
            AllModels = [];
            LoadErrorMessage = $"Could not load models. {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ClearActionMessages()
    {
        ActionErrorMessage = null;
        ActionInfoMessage = null;
    }
}

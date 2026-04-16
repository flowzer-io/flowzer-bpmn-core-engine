using FlowzerFrontend.BusinessLogic;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Models
{
    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    private List<ExtendedBpmnMetaDefinitionDto> AllModels { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private string? ActionErrorMessage { get; set; }
    private string? ActionInfoMessage { get; set; }
    private string? StartingDefinitionId { get; set; }
    public string? SearchText { get; set; }

    public List<Option<SortDirection>> SortDirections =
    [
        new Option<SortDirection> { Value = SortDirection.Ascending, Text = "A → Z" },
        new Option<SortDirection> { Value = SortDirection.Descending, Text = "Z → A" }
    ];

    public Option<SortDirection>? SelectedSortDirection { get; set; }
    public string? SelectedSortDirectionString { get; set; }

    private IReadOnlyList<ExtendedBpmnMetaDefinitionDto> VisibleModels =>
        ModelListViewHelper
            .ApplyQuery(AllModels, SearchText, SelectedSortDirection?.Value ?? SortDirection.Ascending)
            .ToList();

    private int TotalModelCount => AllModels.Count;
    private int DeployedModelCount => AllModels.Count(model => model.DeployedId.HasValue);
    private int DraftModelCount => AllModels.Count(model => !model.DeployedId.HasValue);
    private string ResultLabel => SearchText?.Trim() switch
    {
        { Length: > 0 } => $"Showing {VisibleModels.Count} of {TotalModelCount} workflows",
        _ => $"Showing {VisibleModels.Count} workflows"
    };

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

    private static string FormatTimestamp(DateTime timestamp)
    {
        return timestamp == default
            ? "not yet available"
            : timestamp.ToLocalTime().ToString("g");
    }

    private static string GetLifecycleLabel(ExtendedBpmnMetaDefinitionDto model)
    {
        if (!model.DeployedId.HasValue)
        {
            return "Draft only";
        }

        return model.DeployedVersion == model.LatestVersion
            ? "Latest version is live"
            : $"Live version {model.DeployedVersion}";
    }

    private static string GetLifecycleToneClass(ExtendedBpmnMetaDefinitionDto model)
    {
        if (!model.DeployedId.HasValue)
        {
            return "status-pill-warning";
        }

        return model.DeployedVersion == model.LatestVersion
            ? "status-pill-success"
            : "status-pill-neutral";
    }
}

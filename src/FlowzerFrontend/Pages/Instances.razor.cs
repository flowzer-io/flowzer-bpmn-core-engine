using FlowzerFrontend.BusinessLogic;
using Microsoft.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Instances : ComponentBase
{
    [Parameter] public string? Filter { get; set; }

    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    private List<ProcessInstanceInfoDto> AllInstances { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private InstanceListFilter CurrentFilter { get; set; } = InstanceListFilter.All;
    public string? SearchText { get; set; }

    private IReadOnlyList<ProcessInstanceInfoDto> VisibleInstances =>
        InstanceListViewHelper.ApplyQuery(AllInstances, CurrentFilter, SearchText).ToList();

    public string CurrentFilterLabel => InstanceListFilterHelper.ToDisplayLabel(CurrentFilter);
    private int AllCount => AllInstances.Count;
    private int ActiveCount => CountInstances(InstanceListFilter.Active);
    private int DoneCount => CountInstances(InstanceListFilter.Done);
    private int ErrorCount => CountInstances(InstanceListFilter.Error);
    private string ResultLabel => SearchText?.Trim() switch
    {
        { Length: > 0 } => $"Showing {VisibleInstances.Count} matching instances",
        _ => $"Showing {VisibleInstances.Count} instances"
    };

    protected override async Task OnParametersSetAsync()
    {
        CurrentFilter = InstanceListFilterHelper.ParseOrDefault(Filter);
        await LoadData();
    }

    private async Task LoadData()
    {
        IsLoading = true;
        try
        {
            AllInstances = (await FlowzerApi.GetAllInstances()).ToList();
            LoadErrorMessage = null;
        }
        catch (Exception exception)
        {
            AllInstances = [];
            LoadErrorMessage = $"Could not load instances. {exception.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private int CountInstances(InstanceListFilter filter)
    {
        return InstanceListFilterHelper.Apply(AllInstances, filter).Count();
    }

    private void OnOpenInstanceClick(Guid instanceId)
    {
        NavigationManager.NavigateTo(UriHelper.GetShowInstanceUrl(instanceId));
    }

    private static string GetStateLabel(ProcessInstanceStateDto state)
    {
        return ProcessInstanceStateViewHelper.GetLabel(state);
    }

    private static string GetStateToneClass(ProcessInstanceStateDto state)
    {
        return ProcessInstanceStateViewHelper.GetToneClass(state);
    }
}

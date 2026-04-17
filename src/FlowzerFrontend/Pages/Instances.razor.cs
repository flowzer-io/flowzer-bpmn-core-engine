using FlowzerFrontend.BusinessLogic;
using Microsoft.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Instances : ComponentBase
{
    private string? _searchText;

    [Parameter] public string? Filter { get; set; }

    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    private List<ProcessInstanceInfoDto> AllInstances { get; set; } = [];
    private List<ProcessInstanceInfoDto> VisibleInstances { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private InstanceListFilter CurrentFilter { get; set; } = InstanceListFilter.All;
    private int AllCount { get; set; }
    private int ActiveCount { get; set; }
    private int DoneCount { get; set; }
    private int ErrorCount { get; set; }
    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value;
            RefreshVisibleInstances();
        }
    }

    public string CurrentFilterLabel => InstanceListFilterHelper.ToDisplayLabel(CurrentFilter);
    private bool HasSearchText => !string.IsNullOrWhiteSpace(SearchText);
    private string ResultLabel => SearchText?.Trim() switch
    {
        { Length: > 0 } => $"Showing {VisibleInstances.Count} matching instances",
        _ => $"Showing {VisibleInstances.Count} instances"
    };
    private string EmptyStateTitle => HasSearchText
        ? $"No instances match “{SearchText!.Trim()}”"
        : CurrentFilter == InstanceListFilter.All
            ? "No instances right now"
            : $"No {CurrentFilterLabel.ToLowerInvariant()} instances right now";
    private string EmptyStateDescription => HasSearchText
        ? "Try a different search term or clear the current search to return to the active runtime view."
        : "Start a workflow from the catalog or switch the current runtime filter to inspect other instances.";

    protected override async Task OnParametersSetAsync()
    {
        CurrentFilter = InstanceListFilterHelper.ParseOrDefault(Filter);
        RefreshVisibleInstances();
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
            RefreshViewState();
            IsLoading = false;
        }
    }

    private void RefreshVisibleInstances()
    {
        VisibleInstances = InstanceListViewHelper.ApplyQuery(AllInstances, CurrentFilter, SearchText).ToList();
    }

    private void RefreshMetrics()
    {
        AllCount = AllInstances.Count;
        ActiveCount = InstanceListFilterHelper.Apply(AllInstances, InstanceListFilter.Active).Count();
        DoneCount = InstanceListFilterHelper.Apply(AllInstances, InstanceListFilter.Done).Count();
        ErrorCount = InstanceListFilterHelper.Apply(AllInstances, InstanceListFilter.Error).Count();
    }

    private void RefreshViewState()
    {
        RefreshVisibleInstances();
        RefreshMetrics();
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

    private void ClearSearch()
    {
        SearchText = string.Empty;
    }
}

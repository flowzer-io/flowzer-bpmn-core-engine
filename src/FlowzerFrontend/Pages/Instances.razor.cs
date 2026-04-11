using Microsoft.AspNetCore.Components;
using FlowzerFrontend.BusinessLogic;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Instances : ComponentBase
{
    [Parameter] public string? Filter { get; set; }

    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }
    
    private List<ProcessInstanceInfoDto> AllInstances { get; set; } = [];
    private bool IsLoading { get; set; } = true;
    private string? LoadErrorMessage { get; set; }
    private InstanceListFilter CurrentFilter { get; set; } = InstanceListFilter.All;

    public IQueryable<ProcessInstanceInfoDto> Data =>
        InstanceListFilterHelper.Apply(AllInstances, CurrentFilter).AsQueryable();

    public string CurrentFilterLabel => InstanceListFilterHelper.ToDisplayLabel(CurrentFilter);
    public IEnumerable<ProcessInstanceInfoDto> SelectedItems { get; set; } = new List<ProcessInstanceInfoDto>(); 
    
    
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
}

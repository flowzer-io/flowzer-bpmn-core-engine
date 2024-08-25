using Microsoft.AspNetCore.Components;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Instances : ComponentBase
{
    [Inject]
    public required FlowzerApi FlowzerApi { get; set; }
    
    
    public IQueryable<ProcessInstanceInfoDto>? Data { get; set; } = new List<ProcessInstanceInfoDto>().AsQueryable();
    public IEnumerable<ProcessInstanceInfoDto> SelectedItems { get; set; } = new List<ProcessInstanceInfoDto>(); 
    
    
    protected override async Task OnParametersSetAsync()
    {
        await LoadData();
    }

    private async Task LoadData()
    {
        Data = (await FlowzerApi.GetAllRunningInstances()).AsQueryable();
    }
}
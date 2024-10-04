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

    public bool Expanded { get; set; } = true;
    public bool CheckAll { get; set; } = true;

    public IEnumerable<BpmnMetaDefinitionDto> SelectedItems { get; set; } = new List<BpmnMetaDefinitionDto>(); 
    
    public List<Option<SortDirection>> SortDirections = new()
    {
        new Option<SortDirection> { Value = SortDirection.Ascending, Text = "Ascending" },
        new Option<SortDirection> { Value = SortDirection.Descending, Text = "Descending"}
    };
    
    public Option<SortDirection>? SelectedSortDirection { get; set; }
    public string? SelectedSortDirectionString { get; set; }
    public IQueryable<BpmnMetaDefinitionDto>? Data { get; set; } = new List<BpmnMetaDefinitionDto>().AsQueryable();
    
    
    
    protected override async Task OnParametersSetAsync()
    {
        await LoadData();
    }

    private async Task OnNewClick()
    {
        var newDefinitionMetaData = await FlowzerApi.CreateEmptyDefinition();
        NavigationManager.NavigateTo(UriHelper.GetEditDefinitionUrl(newDefinitionMetaData.DefinitionId));
        
    }
    
    private async Task LoadData()
    {
        Data = (await FlowzerApi.GetAllBpmnMetaDefinitions()).AsQueryable();
    }
}

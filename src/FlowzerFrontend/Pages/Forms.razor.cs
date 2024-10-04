using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using WebApiEngine.Shared;

namespace FlowzerFrontend.Pages;

public partial class Forms : ComponentBase
{
    
    [Inject] public required NavigationManager NavigationManager { get; set; }
    
    [Inject] public required  FlowzerApi FlowzerApi { get; set; }
    
    public IQueryable<FormMetaDataDto> Data { get; set; } = new List<FormMetaDataDto>().AsQueryable();
    public IEnumerable<FormMetaDataDto> SelectedItems { get; set; } = new List<FormMetaDataDto>();


    protected override async Task OnInitializedAsync()
    {
        Data = (await FlowzerApi.GetFormMetaDatas()).AsQueryable();
    }

    private void OnNewClick(MouseEventArgs obj)
    {   
        NavigationManager.NavigateTo(UriHelper.GetNewFormUrl());
    }
}
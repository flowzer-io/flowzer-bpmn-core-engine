using Microsoft.AspNetCore.Components;

namespace FlowzerFrontend.Components;

public partial class HeaderElement : ComponentBase
{
    [Parameter]
    public string Title { get; set; } = "Flowzer";

    [Parameter]
    public RenderFragment? ChildContent { get; set; }
    
    [Parameter] public string? BackLink { get; set; }
    
    [Parameter]
    public RenderFragment? LeftSide { get; set; }

    [Parameter]
    public RenderFragment? RigthSide { get; set; }

}
using Microsoft.AspNetCore.Components;

namespace FlowzerFrontend.Layout;

public partial class LayoutWithLeftMenu: ComponentBase
{
    
    [Parameter] public RenderFragment? MenuContent { get; set; }
    [Parameter] public RenderFragment? MainContent { get; set; }

}
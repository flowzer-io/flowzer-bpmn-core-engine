using Microsoft.AspNetCore.Components;

namespace FlowzerFrontend.Pages;

public partial class FilloutForm : ComponentBase
{
    [Parameter] public FilloutFormParameter? Content { get; set; }

}
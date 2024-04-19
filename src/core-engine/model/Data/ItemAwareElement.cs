using Common;
using Foundation;

namespace Data;

public class ItemAwareElement : BaseElement
{
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}
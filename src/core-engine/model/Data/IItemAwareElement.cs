using Common;
using Foundation;

namespace Data;

public interface IItemAwareElement : IBaseElement
{
    public ItemDefinition? ItemSubjectRef { get; set; }
    public DataState? DataState { get; set; }
}
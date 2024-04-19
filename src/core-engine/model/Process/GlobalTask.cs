using Activities;
using Common;

namespace Process;

public abstract class GlobalTask(string name) : CallableElement(name)
{
    public List<ResourceRole> Resources { get; set; } = [];
}
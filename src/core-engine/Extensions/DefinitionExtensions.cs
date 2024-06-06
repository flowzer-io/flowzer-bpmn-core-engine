namespace core_engine.Extensions;

public static class DefinitionExtensions
{
    public static IEnumerable<Process> GetProcesses(this Definitions definitions)
    {
        return definitions.RootElements.OfType<Process>();
    }
}
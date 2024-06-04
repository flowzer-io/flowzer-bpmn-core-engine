namespace core_engine;

public static class DefinitionExtensions
{
    public static IEnumerable<Process> GetProcesses(this Definitions definitions)
    {
        return definitions.RootElements.OfType<Process>();
    }
}
using FlowzerDemoConsole;

Console.WriteLine("Flowzer BPMN Core Engine – Console-Demo");
Console.WriteLine("======================================");

try
{
    var runner = new DemoScenarioRunner();
    var finalInstance = await runner.RunAsync(Console.Out);

    Console.WriteLine();
    Console.WriteLine($"Finaler Status: {finalInstance.State}");
    return;
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine($"Fehler beim Ausführen der Demo: {ex.Message}");
    Console.WriteLine(ex);
    Environment.ExitCode = 1;
}

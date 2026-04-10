using System.Diagnostics;
using System.Dynamic;
using core_engine.Expression.Feelin;
using FluentAssertions;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;

namespace core_engine_tests;

public class JavaScriptExpressionTest
{
    [Test]
    public void JavaScriptTest1()
    {
        var globals = new ExpandoObject();
        globals.TryAdd("a", "fghjk");
    }
    
    
    [Test]
    public void JavaScriptFeelTest()
    {
        try
        {
            using var engine = new V8ScriptEngine();
            var bundlePath = GetFeelinBundlePath();
            File.Exists(bundlePath).Should().BeTrue($"die FEEL-Bundle-Datei unter {bundlePath} für den Test verfügbar sein muss");

            engine.AddHostObject("host", this);
            engine.AddHostObject("console", new ConsoleWrapper());
            engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            engine.Execute(File.ReadAllText(bundlePath));

            var testObject = new
            {
                name = "Mike",
                daughter = new
                {
                    name = "Lisa"
                }
            };

            engine.AddHostObject("obj", (dynamic)testObject);

            var stopwatch = Stopwatch.StartNew();
            var result = engine.Evaluate("""libfeelin.evaluate("name", obj)""");
            stopwatch.Stop();

            result.Should().Be("Mike");
            TestContext.WriteLine($"FEEL-Auswertung erfolgreich in {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (DllNotFoundException)
        {
            Assert.Ignore("V8-Bibliotheken sind in dieser Umgebung nicht verfügbar.");
        }
    }

    private static string GetFeelinBundlePath()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(FeelinExpressionHandler).Assembly.Location)
                                ?? throw new InvalidOperationException("Das Verzeichnis der core-engine-Assembly konnte nicht ermittelt werden.");

        return Path.Combine(assemblyDirectory, "Expression", "Feelin", "bundle.js");
    }
}

public class ConsoleWrapper
{
    public void Log(string message)
    {
        Console.WriteLine(message);
        TestContext.WriteLine(message);
    }
}

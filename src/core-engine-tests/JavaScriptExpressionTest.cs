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
    public void IsMissingV8Dependency_ShouldReturnTrueForTypeLoadException()
    {
        var exception = new TypeLoadException("Cannot load ClearScript V8 library. Load failure information for ClearScriptV8.linux-x64.so");

        var shouldIgnore = IsMissingV8Dependency(exception);

        shouldIgnore.Should().BeTrue();
    }

    [Test]
    public void IsMissingV8Dependency_ShouldReturnTrueForNestedDllNotFoundException()
    {
        var exception = new InvalidOperationException(
            "Wrapper exception",
            new DllNotFoundException("ClearScriptV8.linux-x64.so konnte nicht geladen werden."));

        var shouldIgnore = IsMissingV8Dependency(exception);

        shouldIgnore.Should().BeTrue();
    }

    [Test]
    public void IsMissingV8Dependency_ShouldReturnFalseForOtherExceptions()
    {
        var exception = new InvalidOperationException("Ein anderer Fehler");

        var shouldIgnore = IsMissingV8Dependency(exception);

        shouldIgnore.Should().BeFalse();
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
        catch (Exception exception) when (IsMissingV8Dependency(exception))
        {
            Assert.Ignore($"V8-Bibliotheken sind in dieser Umgebung nicht verfügbar ({exception.GetType().Name}).");
        }
    }

    internal static bool IsMissingV8Dependency(Exception exception)
    {
        for (var currentException = exception; currentException is not null; currentException = currentException.InnerException!)
        {
            if (currentException is DllNotFoundException)
                return true;

            if (currentException is TypeLoadException &&
                currentException.Message.Contains("ClearScript", StringComparison.OrdinalIgnoreCase))
                return true;

            if (currentException.Message.Contains("ClearScriptV8", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
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

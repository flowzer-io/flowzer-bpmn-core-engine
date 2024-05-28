using System.Diagnostics;
using core_engine;
using Microsoft.ClearScript;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json.Linq;

namespace core_enginge_tests;

public class JavaScriptExpressionTest
{
    [Test]
    public void JavaScriptTest1()
    {
        var globals = new JObject();
        globals.Add("a", "fghjk");

        var t = globals as dynamic;
        TestContext.Out.WriteLine(t.a);

        for (var i = 0; i < 100; i++)
        {
            var handler = new JavaScriptV8ExpressionHandler();
            var sw = new Stopwatch();
            sw.Start();
            var value = handler.GetValue(t , "a");
            sw.Stop();

            TestContext.Out.WriteLine(sw.ElapsedMilliseconds.ToString());
        }
    }

    public string importModule(string modulePath)
    {
        string scriptDirectory = @"/Users/lukasbauhaus/repos/feel/feelin/src";
        string fullModulePath = Path.Combine(scriptDirectory, modulePath);
        return File.ReadAllText(fullModulePath);
    }
    
    [Test]
    public async Task JavsScriptFeelTest()
    {
        string scriptDirectory = @"/Users/lukasbauhaus/repos/feel/feelin/src";

        // Erstelle eine V8-Engine
        using var engine = new V8ScriptEngine() ;
            
        // Lade die Index.js Datei
        string indexPath = Path.Combine(scriptDirectory, "Index.js");

        // Definiere eine Funktion zum Laden von Modulen
        

        //engine.AddHostObject("host", new HostFunctions());
        engine.AddHostObject("host", this);
        engine.AddHostObject("console", new ConsoleWrapper());
        engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
        

        // Lade und führe das Hauptskript aus
        string indexScript = File.ReadAllText(indexPath);
        engine.Execute(indexScript);

        try
        {
            // Beispiel für die Verwendung einer Funktion aus den geladenen Modulen
            engine.Execute(@"
                // Da die importierten Module bereits evaluiert wurden, kannst du ihre Funktionen direkt verwenden
                console.log(unaryTest('1', { '?': 1 })); // true
                // unaryTest('[1..end]', { '?': 1, end: 10 }); // true
            ");
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            
        }
        
        
    }
        
    
}

public class ConsoleWrapper
{
    public void log(string message)
    {
        Console.WriteLine(message);
    }
}
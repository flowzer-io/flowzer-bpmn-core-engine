using System.Diagnostics;
using System.Dynamic;
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
        const string scriptDirectory = @"/Users/lukasbauhaus/repos/feel/feelin/src"; // ToDo: Weg vom absoluten Pfad auf deinem Rechner

        // Erstelle eine V8-Engine
        using var engine = new V8ScriptEngine() ;
            
        // Lade die Index.js Datei
        var indexPath = Path.Combine(scriptDirectory, "/Users/lukasbauhaus/repos/feelin/dist/bundle.js"); // ToDo: Weg vom absoluten Pfad auf deinem Rechner

        // Definiere eine Funktion zum Laden von Modulen
        

        //engine.AddHostObject("host", new HostFunctions());
        engine.AddHostObject("host", this);
        engine.AddHostObject("console", new ConsoleWrapper());
        engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
        
        
        try
        {
            // Lade und führe das Hauptskript aus
            var indexScript = File.ReadAllText(indexPath);
            engine.Execute(indexScript);
            
            for (var i = 0; i < 1000; i++)
            {
                var sw = new Stopwatch();
                sw.Start();

                var x = new
                {
                    name = "Mike",
                    daughter = new
                    {
                        name = "Lisa"
                    }
                };

                engine.AddHostObject("obj", (dynamic)x);

                var ret = engine.Evaluate("""libfeelin.evaluate("name", obj)""");
                TestContext.WriteLine(ret);
                sw.Stop();
                Console.WriteLine(sw.ElapsedMilliseconds);
            }
            

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            
        }
        
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
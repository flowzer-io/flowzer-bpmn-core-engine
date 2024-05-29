using System.Diagnostics;
using System.Dynamic;
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
        var globals = new ExpandoObject();
        globals.TryAdd("a", "fghjk");
    }
    
    
    [Test]
    public async Task JavsScriptFeelTest()
    {
        string scriptDirectory = @"/Users/lukasbauhaus/repos/feel/feelin/src";

        // Erstelle eine V8-Engine
        using var engine = new V8ScriptEngine() ;
            
        // Lade die Index.js Datei
        string indexPath = Path.Combine(scriptDirectory, "/Users/lukasbauhaus/repos/feelin/dist/bundle.js");

        // Definiere eine Funktion zum Laden von Modulen
        

        //engine.AddHostObject("host", new HostFunctions());
        engine.AddHostObject("host", this);
        engine.AddHostObject("console", new ConsoleWrapper());
        engine.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
        
        
        try
        {
            // Lade und führe das Hauptskript aus
            string indexScript = File.ReadAllText(indexPath);
            engine.Execute(indexScript);
            
            for (int i = 0; i < 1000; i++)
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
    public void log(string message)
    {
        Console.WriteLine(message);
        TestContext.WriteLine(message);
    }
}
using System.Diagnostics;
using System.Dynamic;
using Microsoft.ClearScript.V8;
using Newtonsoft.Json.Linq;

string scriptDirectory = @"/Users/lukasbauhaus/repos/feel/feelin/src";

// Erstelle eine V8-Engine
var engine = new V8ScriptEngine() ;
            
// Lade die Index.js Datei
string indexPath = Path.Combine(scriptDirectory, "/Users/lukasbauhaus/repos/feelin/dist/bundle.js");

// Definiere eine Funktion zum Laden von Modulen
        

        
try
{
    System.Threading.Thread.Sleep(2000);
    // Lade und führe das Hauptskript aus
    string indexScript = File.ReadAllText(indexPath);
    engine.Execute(indexScript);
    
    System.Threading.Thread.Sleep(2000);
            
    for (int i = 0; i < 1; i++)
    {
        if (i % 100000 == 0)
        {
            engine.Dispose();
            engine = new V8ScriptEngine();
            engine.Execute(indexScript);
        }

        var x = new
        {
            name = "Mike",
            daughter = new
            {
                name = "Lisa"
            }
        };

        //engine.AddHostObject("obj", (dynamic)x);

        dynamic obj = new ExpandoObject();
        obj.ServiceResult = "Hello World";
        
        engine.AddHostObject("obj", obj);

        var ret = engine.Evaluate("""libfeelin.evaluate("ServiceResult", obj)""");
        Console.WriteLine(ret);
        
    }
    
            

}
catch (Exception e)
{
    Console.WriteLine(e);
            
}
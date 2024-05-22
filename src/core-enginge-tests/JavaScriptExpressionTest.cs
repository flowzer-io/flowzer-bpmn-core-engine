using System.Diagnostics;
using core_engine;
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
}
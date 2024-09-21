using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FlowzerFrontend;

public static class JsHelper
{
    public static async Task EvalCodeBehindJsScripts(this IJSRuntime jsRuntime, ComponentBase blazorFile)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = blazorFile.GetType().FullName + ".razor.js";
        
        
        var manifestResourceStream = assembly.GetManifestResourceStream(resourceName);
        if (manifestResourceStream == null)
            return;

        await using Stream stream = manifestResourceStream;
        using StreamReader reader = new StreamReader(stream);
        var fileContent = await reader.ReadToEndAsync();
        await jsRuntime.InvokeVoidAsync("eval", fileContent);
    }

    public static ValueTask InvokeVoidAsyncNoneCached(this IJSRuntime jsRuntime, params object[] args)
    {
        return jsRuntime.InvokeVoidAsync("callFunctionWithoutCaching", args); 
    }
}
//calls a function with the given path without caching the function reference 
//this is for bypassing the problem that an old reference of window.xxx.function1 is used,
//event is the reference of window.xxx ist updated in the meantime. this is behavior 
//is related to the jsinteropt caching behavior for function references see "findFunction" in blazor.webassembly.js
function callFunctionWithoutCaching(functionPath, ...args) {
    const parts = functionPath.split('.');
    let context = window;

    for (let i = 0; i < parts.length - 1; i++) {
        context = context[parts[i]];
    }

    const functionName = parts[parts.length - 1];
    if (context[functionName] == undefined)
    {
        throw new Error("Function not found: " + functionPath);
    }
    return context[functionName](...args);
}


function executeInIframe(selector,funcName, ...args) {
    const iframe = document.querySelector(selector);
    
    return new Promise((resolve, reject) => {
        // Timeout to prevent waiting indefinitely
        const timeout = setTimeout(() => reject(new Error("No response received")), 1000);
    
        function messageHandler(event) {
            if (event.data.type === "response"){
                clearTimeout(timeout); // Clear the timeout
                window.removeEventListener('message', messageHandler); // Clean up listener
                resolve(event.data.ret); // Resolve the promise with response data    
            }
            
        }
    
        // Listen for response from the iframe
        window.addEventListener('message', messageHandler);
    
        // Send the message to the iframe
        iframe.contentWindow.postMessage({func: funcName, arguments: args}, "*");
    });
}
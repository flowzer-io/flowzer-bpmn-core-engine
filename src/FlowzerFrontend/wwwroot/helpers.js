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
    return context[functionName](...args);
}



function executeInIframe(funcName, ...args) {
    const iframe = document.querySelector("iframe");

    return new Promise((resolve, reject) => {
        // Timeout to prevent waiting indefinitely
        const timeout = setTimeout(() => reject(new Error("No response received")), 1000);
    
        function messageHandler(event) {
            clearTimeout(timeout); // Clear the timeout
            window.removeEventListener('message', messageHandler); // Clean up listener
            resolve(event.data); // Resolve the promise with response data
        }
    
        // Listen for response from the iframe
        window.addEventListener('message', messageHandler);
    
        // Send the message to the iframe
        iframe.contentWindow.postMessage({func: funcName, arguments: args}, "*");
    });
}
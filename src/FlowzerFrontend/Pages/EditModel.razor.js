function isReady(){
    try {
        return (typeof bpmnModeler !== undefined) && (bpmnModeler !== undefined);    
    }
    catch (error) {
        return false;
    }
}

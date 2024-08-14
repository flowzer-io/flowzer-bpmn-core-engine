function isReady(){
    try {
        return (typeof bpmnModeler !== undefined) && (bpmnModeler !== undefined);    
    }
    catch (error) {
        return false;
    }
}


async function importXML(xml)
{
    await bpmnModeler.importXML(xml)
}

 async function saveXML(){
     return await bpmnModeler.saveXML({format: true})
 }

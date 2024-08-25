
async function resetInit(){
    InitCompleted = false;
    console.log("resetInit");
}
async function isReady(){
    try {

        let ret = (typeof InitCompleted !== undefined) && (InitCompleted == true);
        console.log("isReady: " + ret);
        return ret;
    }
    catch (error) {
        return false;
    }
}

async function importXML(xml)
{
    await window.bpmnModeler.importXML(xml);
    console.log("importXML " + xml);
}

 async function saveXML(){
     return await window.bpmnModeler.saveXML({format: true})
 }

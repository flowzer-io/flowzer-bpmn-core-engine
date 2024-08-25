
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
    await bpmnViewer.importXML(xml)
}


function addToken(id, count){
    bpmnViewer.get('overlays').add(id, 'note', {
        position: {
            top: -10,
            left: -10
        },
        html: '<div class="diagram-note" style="background: green; border-radius: 100%; width: 20px; height: 20px; overflow: hidden; font-weigth: bold; text-align: center; color: white;">' + count + '</div>'
    });
}
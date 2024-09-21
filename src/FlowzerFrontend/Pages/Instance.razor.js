


function clearTokens(){
    bpmnViewer.get('overlays').clear();
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
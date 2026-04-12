(function () {
    async function waitForElement(selector) {
        for (let attempt = 0; attempt < 20; attempt += 1) {
            const element = document.querySelector(selector);
            if (element) {
                return element;
            }

            await new Promise(resolve => requestAnimationFrame(resolve));
        }

        throw new Error(`Required BPMN container '${selector}' was not rendered.`);
    }

    function resetCanvas() {
        const canvas = document.querySelector('#js-canvas');
        if (canvas) {
            canvas.innerHTML = '';
        }
    }

    function getViewer() {
        if (!window.bpmnViewer || typeof window.bpmnViewer.importXML !== 'function') {
            throw new Error('BPMN viewer is not initialized.');
        }

        return window.bpmnViewer;
    }

    async function disposeViewer() {
        const existingViewer = window.bpmnViewer;
        if (existingViewer && typeof existingViewer.destroy === 'function') {
            existingViewer.destroy();
        }

        window.bpmnViewer = undefined;
        resetCanvas();
    }

    async function initializeViewer() {
        await waitForElement('#js-canvas');
        await new Promise(resolve => requestAnimationFrame(resolve));

        await disposeViewer();
        await window.InitViewer();

        if (!window.bpmnViewer) {
            throw new Error('BPMN viewer could not be created.');
        }
    }

    async function importXml(xml) {
        if (!xml || !xml.trim()) {
            throw new Error('No BPMN XML was provided.');
        }

        const viewer = getViewer();
        const importResult = await viewer.importXML(xml);
        const canvas = viewer.get('canvas');
        if (canvas && typeof canvas.zoom === 'function') {
            canvas.zoom('fit-viewport');
        }

        return Array.isArray(importResult?.warnings) ? importResult.warnings : [];
    }

    function clearTokens() {
        const viewer = getViewer();
        viewer.get('overlays').clear();
    }

    function addToken(id, count) {
        if (!id) {
            return false;
        }

        const viewer = getViewer();
        const elementRegistry = viewer.get('elementRegistry');
        if (!elementRegistry || !elementRegistry.get(id)) {
            console.warn(`Could not find BPMN element '${id}' for token overlay.`);
            return false;
        }

        viewer.get('overlays').add(id, 'note', {
            position: {
                top: -10,
                left: -10
            },
            html: `<div class="diagram-note" style="background: green; border-radius: 100%; width: 20px; height: 20px; overflow: hidden; font-weight: bold; text-align: center; color: white;">${count}</div>`
        });

        return true;
    }

    window.FlowzerInstanceViewer = {
        addToken,
        clearTokens,
        dispose: disposeViewer,
        importXml,
        initialize: initializeViewer
    };
})();

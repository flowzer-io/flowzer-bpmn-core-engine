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

    function resetContainer(selector) {
        const element = document.querySelector(selector);
        if (element) {
            element.innerHTML = '';
        }
    }

    async function disposeEditor() {
        const existingEditor = window.bpmnModeler;
        if (existingEditor && typeof existingEditor.destroy === 'function') {
            existingEditor.destroy();
        }

        window.bpmnModeler = undefined;
        resetContainer('#js-canvas');
        resetContainer('#js-properties-panel');
    }

    function getEditor() {
        if (!window.bpmnModeler || typeof window.bpmnModeler.importXML !== 'function') {
            throw new Error('BPMN editor is not initialized.');
        }

        return window.bpmnModeler;
    }

    async function initializeEditor() {
        await waitForElement('#js-canvas');
        await waitForElement('#js-properties-panel');
        await new Promise(resolve => requestAnimationFrame(resolve));

        await disposeEditor();
        await window.InitEdit();

        if (!window.bpmnModeler) {
            throw new Error('BPMN editor could not be created.');
        }
    }

    async function importXml(xml) {
        if (!xml || !xml.trim()) {
            throw new Error('No BPMN XML was provided.');
        }

        const editor = getEditor();
        const importResult = await editor.importXML(xml);
        const canvas = editor.get('canvas');
        if (canvas && typeof canvas.zoom === 'function') {
            canvas.zoom('fit-viewport');
        }

        return Array.isArray(importResult?.warnings) ? importResult.warnings : [];
    }

    async function saveXml() {
        const editor = getEditor();
        return await editor.saveXML({ format: true });
    }

    window.FlowzerDefinitionEditor = {
        dispose: disposeEditor,
        importXml,
        initialize: initializeEditor,
        saveXml
    };
})();

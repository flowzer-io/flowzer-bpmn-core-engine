const { test, expect } = require('@playwright/test');
const {
  buildPlainStartXml,
  buildMessageStartUserTaskXml,
  createDefinitionMeta,
  createFormSchema,
  deployDefinition,
  getUserTasks,
  saveForm,
  sendMessage,
  startProcessInstance,
  completeUserTask
} = require('../support/flowzer-api');

function createSuffix(prefix) {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

let runtimeScenarioPromise;
let modelerScenarioPromise;
let directStartScenarioPromise;

async function ensureRuntimeScenario(request) {
  if (runtimeScenarioPromise) {
    return runtimeScenarioPromise;
  }

  runtimeScenarioPromise = (async () => {
    const formKey = createSuffix('ui-smoke-task-form');
    const modelName = createSuffix('UI Smoke Runtime');
    const messageName = createSuffix('UiSmokeStart');
    const definitionId = await createDefinitionMeta(request, {
      name: modelName,
      description: 'Playwright runtime happy-path.'
    });

    await saveForm(request, {
      name: formKey,
      schema: createFormSchema('Approval')
    });

    const deployedDefinition = await deployDefinition(request, {
      xml: buildMessageStartUserTaskXml({
        definitionId,
        messageName,
        formKey,
        userTaskName: 'Review order'
      })
    });

    return {
      definitionId,
      definitionGuid: deployedDefinition.definitionGuid,
      formKey,
      modelName,
      messageName
    };
  })();

  return runtimeScenarioPromise;
}

async function ensureModelerScenario(request) {
  if (modelerScenarioPromise) {
    return modelerScenarioPromise;
  }

  modelerScenarioPromise = (async () => {
    const formKey = createSuffix('ui-smoke-modeler-form');
    const modelName = createSuffix('UI Smoke Modeler');
    const messageName = createSuffix('UiSmokeModelerStart');
    const definitionId = await createDefinitionMeta(request, {
      name: modelName,
      description: 'Playwright modeler hardening path.'
    });

    const deployedDefinition = await deployDefinition(request, {
      xml: buildMessageStartUserTaskXml({
        definitionId,
        messageName,
        formKey,
        userTaskName: 'Review model'
      })
    });

    return {
      definitionId,
      definitionGuid: deployedDefinition.definitionGuid,
      modelName
    };
  })();

  return modelerScenarioPromise;
}

async function ensureDirectStartScenario(request) {
  if (directStartScenarioPromise) {
    return directStartScenarioPromise;
  }

  directStartScenarioPromise = (async () => {
    const modelName = createSuffix('UI Smoke Direct Start');
    const definitionId = await createDefinitionMeta(request, {
      name: modelName,
      description: 'Playwright direct-start workflow path.'
    });

    const deployedDefinition = await deployDefinition(request, {
      xml: buildPlainStartXml({ definitionId })
    });

    return {
      definitionId,
      definitionGuid: deployedDefinition.definitionGuid,
      modelName
    };
  })();

  return directStartScenarioPromise;
}

// Testzweck: Prüft, dass ein per API angelegtes Formular in Liste und Detailansicht des Frontends stabil geladen wird.
test('Formulare aus der API erscheinen in Liste und Detailansicht', async ({ page, request }) => {
  const formName = createSuffix('ui-smoke-form');
  const { formId } = await saveForm(request, {
    name: formName,
    schema: createFormSchema('Customer')
  });

  await page.goto('/forms', { waitUntil: 'networkidle' });

  const formLink = page.getByRole('link', { name: formName });
  await expect(formLink).toBeVisible();
  await formLink.click();

  await expect(page).toHaveURL(new RegExp(`/forms/${formId}$`));
  await expect(page.locator('#edit-form-loading-state')).toHaveCount(0, { timeout: 20_000 });
  await expect(page.locator('#current-file-name')).toHaveText(formName, { timeout: 20_000 });
  await expect(page.locator('iframe[src="/formio"]')).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
});

// Testzweck: Prüft, dass bereitgestellte Modelle im Frontend sichtbar sind und der Diagramm-/Property-Panel-Pfad ohne UI-Fehler lädt.
test('Bereitgestellte Modelle erscheinen im Frontend und öffnen den Diagrammzugriff', async ({ page, request }) => {
  const modelerScenario = await ensureModelerScenario(request);
  const { definitionId, modelName } = modelerScenario;

  await page.goto('/models', { waitUntil: 'networkidle' });

  const definitionLink = page.getByRole('link', { name: modelName });
  await expect(definitionLink).toBeVisible();
  await definitionLink.click();

  await expect(page).toHaveURL(new RegExp(`/definition/${definitionId}$`));
  await expect(page.locator('#current-file-name')).toHaveText(modelName);
  await expect(page.locator('#current-file-version')).toContainText(/\d+\.\d+/);
  await expect(page.locator('#designer-container')).toBeVisible();
  await expect(page.locator('#js-canvas .djs-container')).toBeVisible();
  await expect(page.locator('#js-properties-panel .bio-properties-panel, #js-properties-panel .djs-properties-panel')).toBeVisible();
  await expect(page.getByText('Error :-(')).toHaveCount(0);
  await expect(page.locator('#definition-load-error')).toHaveCount(0);
});

// Testzweck: Prüft, dass der Modeler beim Speichern eine neue Version erzeugt und die Route auf die neue Definitions-GUID umstellt.
test('Der Modeler speichert mit Versionsbezug und aktualisiert die Definitionsroute', async ({ page, request }) => {
  const modelerScenario = await ensureModelerScenario(request);
  const { definitionId, definitionGuid } = modelerScenario;

  await page.goto(`/definition/${definitionId}/${definitionGuid}`, { waitUntil: 'networkidle' });
  await expect(page.locator('#definition-loading-state')).toHaveCount(0, { timeout: 20_000 });
  await expect(page.locator('#js-canvas .djs-container')).toBeVisible();

  const requestPromise = page.waitForRequest(currentRequest => {
    return currentRequest.method() === 'POST' && currentRequest.url().includes('/definition?previousGuid=');
  });

  const responsePromise = page.waitForResponse(currentResponse => {
    return currentResponse.request().method() === 'POST' && currentResponse.url().includes('/definition?previousGuid=');
  });

  await page.getByRole('button', { name: 'Save draft' }).click();

  const saveRequest = await requestPromise;
  expect(saveRequest.url()).toContain(`previousGuid=${definitionGuid}`);

  const saveResponse = await responsePromise;
  expect(saveResponse.ok()).toBeTruthy();

  const savedDefinition = await saveResponse.json();
  const savedDefinitionGuid = savedDefinition.id || savedDefinition.Id;
  expect(savedDefinitionGuid).toBeTruthy();
  expect(savedDefinitionGuid).not.toBe(definitionGuid);

  await expect(page).toHaveURL(new RegExp(`/definition/${definitionId}/${savedDefinitionGuid}$`));
  await expect(page.locator('#blazor-error-ui')).toBeHidden();
  await expect(page.locator('#definition-load-error')).toHaveCount(0);
});

// Testzweck: Prüft, dass die Modellliste explizite Öffnen-/Starten-Aktionen anbietet und ein Direktstart in die Instanzansicht navigiert.
test('Die Modellliste bietet Öffnen und Direktstart für deployte Workflows', async ({ page, request }) => {
  const directStartScenario = await ensureDirectStartScenario(request);
  const { definitionId, definitionGuid, modelName } = directStartScenario;

  await page.goto('/models', { waitUntil: 'networkidle' });

  await expect(page.locator(`#model-open-latest-${definitionId}`)).toBeVisible();
  await expect(page.locator(`#model-open-deployed-${definitionId}`)).toBeVisible();
  await expect(page.locator(`#model-start-instance-${definitionId}`)).toBeVisible();

  await page.locator(`#model-open-deployed-${definitionId}`).click();
  await expect(page).toHaveURL(new RegExp(`/definition/${definitionId}/${definitionGuid}$`));
  await expect(page.locator('#definition-state-badge')).toContainText('Deployed');
  await expect(page.getByRole('button', { name: 'Start instance' })).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();

  await page.goto('/models', { waitUntil: 'networkidle' });
  await page.locator(`#model-start-instance-${definitionId}`).click();

  await expect(page).toHaveURL(/\/instance\/[0-9a-f-]+$/);
  await expect(page.locator('#instance-name')).toContainText(modelName);
  await expect(page.locator('#js-canvas .djs-container')).toBeVisible();
});

// Testzweck: Prüft den UI-Happy-Path von Message-Start über offenen User-Task bis zur abgeschlossenen Instanzansicht.
test('Message-Start-Prozess wandert von offenem Task zu Done Instances', async ({ page, request }) => {
  const runtimeScenario = await ensureRuntimeScenario(request);
  const { modelName, messageName } = runtimeScenario;

  await sendMessage(request, {
    name: messageName,
    variables: {
      customer: 'Ada'
    }
  });

  const userTasks = await getUserTasks(request);
  const userTask = userTasks.find(task => task.definitionMetaName === modelName);
  expect(userTask).toBeTruthy();

  await page.goto('/', { waitUntil: 'networkidle' });
  await expect(page.locator('.user-task-card').filter({ hasText: modelName })).toBeVisible();

  await page.goto('/instances/active', { waitUntil: 'networkidle' });
  const activeInstanceLink = page.getByRole('link', { name: modelName });
  await expect(activeInstanceLink).toBeVisible();
  await activeInstanceLink.click();

  await expect(page).toHaveURL(/\/instance\/[0-9a-f-]+$/);
  await expect(page.locator('#instance-name')).toContainText(modelName);
  await expect(page.locator('#js-canvas .djs-container')).toBeVisible();
  await expect(page.locator('.diagram-note')).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).toBeHidden();

  await completeUserTask(request, userTask, {
    approved: true
  });

  await page.goto('/instances/done', { waitUntil: 'networkidle' });
  await expect(page.getByRole('link', { name: modelName })).toBeVisible();
});

// Testzweck: Prüft, dass der neue Direktstart-Endpunkt auch außerhalb der UI einen manuellen Startpfad für deployte Workflows bietet.
test('Deployte Plain-Start-Workflows lassen sich per API direkt starten', async ({ request }) => {
  const directStartScenario = await ensureDirectStartScenario(request);

  const startedInstance = await startProcessInstance(request, {
    definitionId: directStartScenario.definitionId
  });

  expect(startedInstance.instanceId).toBeTruthy();
  expect(startedInstance.relatedDefinitionId).toBe(directStartScenario.definitionId);
});

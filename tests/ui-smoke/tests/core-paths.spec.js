const { test, expect } = require('@playwright/test');
const {
  buildMessageStartUserTaskXml,
  createDefinitionMeta,
  createFormSchema,
  deployDefinition,
  getUserTasks,
  saveForm,
  sendMessage,
  completeUserTask
} = require('../support/flowzer-api');

function createSuffix(prefix) {
  return `${prefix}-${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
}

let runtimeScenarioPromise;

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

    await deployDefinition(request, {
      xml: buildMessageStartUserTaskXml({
        definitionId,
        messageName,
        formKey,
        userTaskName: 'Review order'
      })
    });

    return {
      definitionId,
      formKey,
      modelName,
      messageName
    };
  })();

  return runtimeScenarioPromise;
}

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

test('Bereitgestellte Modelle erscheinen im Frontend und öffnen den Diagrammzugriff', async ({ page, request }) => {
  const runtimeScenario = await ensureRuntimeScenario(request);
  const { definitionId, modelName } = runtimeScenario;

  await page.goto('/models', { waitUntil: 'networkidle' });

  const definitionLink = page.getByRole('link', { name: modelName });
  await expect(definitionLink).toBeVisible();
  await definitionLink.click();

  await expect(page).toHaveURL(new RegExp(`/definition/${definitionId}$`));
  await expect(page.locator('#current-file-name')).toHaveText(modelName);
  await expect(page.locator('#current-file-version')).toContainText('2.0');
  await expect(page.locator('#designer-container')).toBeVisible();
  await expect(page.getByText('Error :-(')).toHaveCount(0);
});

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
  await expect(page.locator('#blazor-error-ui')).toBeHidden();

  await completeUserTask(request, userTask, {
    approved: true
  });

  await page.goto('/instances/done', { waitUntil: 'networkidle' });
  await expect(page.getByRole('link', { name: modelName })).toBeVisible();
});

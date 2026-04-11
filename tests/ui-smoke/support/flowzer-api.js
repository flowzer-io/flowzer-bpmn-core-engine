const { randomUUID } = require('crypto');

const apiUrl = process.env.FLOWZER_API_URL || 'http://localhost:5182';

function buildApiUrl(pathname) {
  return new URL(pathname, apiUrl).toString();
}

async function readJson(response, description) {
  const bodyText = await response.text();
  if (!response.ok()) {
    throw new Error(`${description} failed with HTTP ${response.status()}: ${bodyText}`);
  }

  return bodyText ? JSON.parse(bodyText) : null;
}

function ensureApiSuccess(payload, description) {
  const isSuccessful = payload?.Successful ?? payload?.successful;
  if (!payload || isSuccessful === false) {
    const errorMessage = payload?.ErrorMessage || payload?.errorMessage || 'Unknown API error.';
    throw new Error(`${description} failed: ${errorMessage}`);
  }

  return payload?.Result ?? payload?.result;
}

function createFormSchema(label) {
  return JSON.stringify({
    components: [
      {
        label,
        key: 'value',
        type: 'textfield',
        input: true
      }
    ]
  });
}

function toSafeId(value) {
  return value.replace(/[^A-Za-z0-9_]/g, '_');
}

function buildMessageStartUserTaskXml({
  definitionId,
  messageName,
  formKey,
  userTaskName = 'Review order'
}) {
  const processId = `Process_${toSafeId(definitionId)}`;
  const messageId = `Message_${toSafeId(definitionId)}`;
  const messageStartEventId = `StartEvent_${toSafeId(definitionId)}`;
  const userTaskId = `Activity_${toSafeId(definitionId)}_Review`;
  const endEventId = `EndEvent_${toSafeId(definitionId)}`;

  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}"
                  targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:message id="${messageId}" name="${messageName}" />
  <bpmn:process id="${definitionId}" isExecutable="true">
    <bpmn:startEvent id="${messageStartEventId}" name="Message Start">
      <bpmn:outgoing>Flow_${processId}_1</bpmn:outgoing>
      <bpmn:messageEventDefinition id="MessageEventDefinition_${processId}" messageRef="${messageId}" />
    </bpmn:startEvent>
    <bpmn:userTask id="${userTaskId}" name="${userTaskName}">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${formKey}" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow_${processId}_1</bpmn:incoming>
      <bpmn:outgoing>Flow_${processId}_2</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:endEvent id="${endEventId}" name="Done">
      <bpmn:incoming>Flow_${processId}_2</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow_${toSafeId(definitionId)}_1" sourceRef="${messageStartEventId}" targetRef="${userTaskId}" />
    <bpmn:sequenceFlow id="Flow_${toSafeId(definitionId)}_2" sourceRef="${userTaskId}" targetRef="${endEventId}" />
  </bpmn:process>
</bpmn:definitions>`;
}

async function saveForm(request, { formId = randomUUID(), name, schema }) {
  const metadataResponse = await request.post(buildApiUrl(`/form/meta/${formId}`), {
    data: {
      formId,
      name
    }
  });
  ensureApiSuccess(await readJson(metadataResponse, 'Saving form metadata'), 'Saving form metadata');

  const formResponse = await request.post(buildApiUrl('/form'), {
    data: {
      formId,
      version: { major: 0, minor: 0 },
      formData: schema
    }
  });

  const formResult = ensureApiSuccess(await readJson(formResponse, 'Saving form content'), 'Saving form content');
  return {
    formId,
    savedFormId: formResult?.id || formResult?.Id,
    version: formResult?.version || formResult?.Version
  };
}

async function createDefinitionMeta(request, { name, description = '' }) {
  const createResponse = await request.get(buildApiUrl('/definition/new'));
  const createdDefinition = await readJson(createResponse, 'Creating definition metadata');
  const definitionId = createdDefinition?.definitionId || createdDefinition?.DefinitionId;

  const updateResponse = await request.put(buildApiUrl('/definition/meta'), {
    data: {
      definitionId,
      name,
      description
    }
  });
  await readJson(updateResponse, 'Updating definition metadata');

  return definitionId;
}

async function deployDefinition(request, { xml }) {

  const definitionResponse = await request.post(buildApiUrl('/definition/deploy'), {
    headers: {
      'content-type': 'text/plain'
    },
    data: xml
  });

  const deployedDefinition = ensureApiSuccess(
    await readJson(definitionResponse, 'Deploying definition'),
    'Deploying definition');

  return {
    definitionId: deployedDefinition?.definitionId || deployedDefinition?.DefinitionId,
    definitionGuid: deployedDefinition.id || deployedDefinition.Id
  };
}

async function sendMessage(request, { name, correlationKey = null, variables = {}, instanceId = null }) {
  const response = await request.post(buildApiUrl('/message'), {
    data: {
      name,
      correlationKey,
      variables,
      instanceId
    }
  });

  ensureApiSuccess(await readJson(response, 'Sending message'), 'Sending message');
}

async function getUserTasks(request) {
  const response = await request.get(buildApiUrl('/usertask'));
  return ensureApiSuccess(await readJson(response, 'Loading user tasks'), 'Loading user tasks');
}

async function completeUserTask(request, userTask, data = {}) {
  const response = await request.post(buildApiUrl('/form/result'), {
    data: {
      flowNodeId: userTask.token.currentFlowNodeId,
      tokenId: userTask.token.id,
      processInstanceId: userTask.processInstanceId,
      data
    }
  });

  ensureApiSuccess(await readJson(response, 'Completing user task'), 'Completing user task');
}

module.exports = {
  buildMessageStartUserTaskXml,
  createDefinitionMeta,
  createFormSchema,
  deployDefinition,
  getUserTasks,
  saveForm,
  sendMessage,
  completeUserTask,
  randomUUID
};

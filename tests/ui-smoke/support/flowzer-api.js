const { randomUUID } = require('crypto');

const apiUrl = process.env.FLOWZER_API_URL || 'http://localhost:5182';
const developmentUserIdHeaderName = 'X-Flowzer-UserId';
const developmentUserId =
  process.env.FLOWZER_DEVELOPMENT_USER_ID ||
  'F70DD76D-D4A3-44D2-8A28-7547E84B7A91';

function buildApiUrl(pathname) {
  return new URL(pathname, apiUrl).toString();
}

function buildRequestOptions(overrides = {}) {
  const mergedHeaders = {
    ...(developmentUserId
      ? { [developmentUserIdHeaderName]: developmentUserId }
      : {}),
    ...(overrides.headers || {})
  };

  return {
    ...overrides,
    headers: mergedHeaders
  };
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
  const safeDefinitionId = toSafeId(definitionId);
  const processId = `Process_${safeDefinitionId}`;
  const messageId = `Message_${toSafeId(definitionId)}`;
  const messageStartEventId = `StartEvent_${safeDefinitionId}`;
  const userTaskId = `Activity_${safeDefinitionId}_Review`;
  const endEventId = `EndEvent_${safeDefinitionId}`;
  const firstFlowId = `Flow_${safeDefinitionId}_1`;
  const secondFlowId = `Flow_${safeDefinitionId}_2`;
  const diagramId = `BPMNDiagram_${safeDefinitionId}`;
  const planeId = `BPMNPlane_${safeDefinitionId}`;

  return `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}"
                  targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:message id="${messageId}" name="${messageName}" />
  <bpmn:process id="${processId}" isExecutable="true">
    <bpmn:startEvent id="${messageStartEventId}" name="Message Start">
      <bpmn:outgoing>${firstFlowId}</bpmn:outgoing>
      <bpmn:messageEventDefinition id="MessageEventDefinition_${processId}" messageRef="${messageId}" />
    </bpmn:startEvent>
    <bpmn:userTask id="${userTaskId}" name="${userTaskName}">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${formKey}" />
      </bpmn:extensionElements>
      <bpmn:incoming>${firstFlowId}</bpmn:incoming>
      <bpmn:outgoing>${secondFlowId}</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:endEvent id="${endEventId}" name="Done">
      <bpmn:incoming>${secondFlowId}</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="${firstFlowId}" sourceRef="${messageStartEventId}" targetRef="${userTaskId}" />
    <bpmn:sequenceFlow id="${secondFlowId}" sourceRef="${userTaskId}" targetRef="${endEventId}" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="${diagramId}">
    <bpmndi:BPMNPlane id="${planeId}" bpmnElement="${processId}">
      <bpmndi:BPMNShape id="${messageStartEventId}_di" bpmnElement="${messageStartEventId}">
        <dc:Bounds x="173" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="${userTaskId}_di" bpmnElement="${userTaskId}">
        <dc:Bounds x="280" y="80" width="100" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="${endEventId}_di" bpmnElement="${endEventId}">
        <dc:Bounds x="460" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="${firstFlowId}_di" bpmnElement="${firstFlowId}">
        <di:waypoint x="209" y="120" />
        <di:waypoint x="280" y="120" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="${secondFlowId}_di" bpmnElement="${secondFlowId}">
        <di:waypoint x="380" y="120" />
        <di:waypoint x="460" y="120" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;
}

async function saveForm(request, { formId = randomUUID(), name, schema }) {
  const metadataResponse = await request.post(buildApiUrl(`/form/meta/${formId}`), buildRequestOptions({
    data: {
      formId,
      name
    }
  }));
  ensureApiSuccess(await readJson(metadataResponse, 'Saving form metadata'), 'Saving form metadata');

  const formResponse = await request.post(buildApiUrl('/form'), buildRequestOptions({
    data: {
      formId,
      version: { major: 0, minor: 0 },
      formData: schema
    }
  }));

  const formResult = ensureApiSuccess(await readJson(formResponse, 'Saving form content'), 'Saving form content');
  return {
    formId,
    savedFormId: formResult?.id || formResult?.Id,
    version: formResult?.version || formResult?.Version
  };
}

async function createDefinitionMeta(request, { name, description = '' }) {
  const createResponse = await request.get(buildApiUrl('/definition/new'), buildRequestOptions());
  const createdDefinition = await readJson(createResponse, 'Creating definition metadata');
  const definitionId = createdDefinition?.definitionId || createdDefinition?.DefinitionId;

  const updateResponse = await request.put(buildApiUrl('/definition/meta'), buildRequestOptions({
    data: {
      definitionId,
      name,
      description
    }
  }));
  await readJson(updateResponse, 'Updating definition metadata');

  return definitionId;
}

async function deployDefinition(request, { xml }) {

  const definitionResponse = await request.post(buildApiUrl('/definition/deploy'), buildRequestOptions({
    headers: {
      'content-type': 'text/plain'
    },
    data: xml
  }));

  const deployedDefinition = ensureApiSuccess(
    await readJson(definitionResponse, 'Deploying definition'),
    'Deploying definition');

  return {
    definitionId: deployedDefinition?.definitionId || deployedDefinition?.DefinitionId,
    definitionGuid: deployedDefinition.id || deployedDefinition.Id
  };
}

async function sendMessage(request, { name, correlationKey = null, variables = {}, instanceId = null }) {
  const response = await request.post(buildApiUrl('/message'), buildRequestOptions({
    data: {
      name,
      correlationKey,
      variables,
      instanceId
    }
  }));

  ensureApiSuccess(await readJson(response, 'Sending message'), 'Sending message');
}

async function getUserTasks(request) {
  const response = await request.get(buildApiUrl('/usertask'), buildRequestOptions());
  return ensureApiSuccess(await readJson(response, 'Loading user tasks'), 'Loading user tasks');
}

async function completeUserTask(request, userTask, data = {}) {
  const response = await request.post(buildApiUrl('/form/result'), buildRequestOptions({
    data: {
      flowNodeId: userTask.token.currentFlowNodeId,
      tokenId: userTask.token.id,
      processInstanceId: userTask.processInstanceId,
      data
    }
  }));

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

import { BpmnModdle } from 'bpmn-moddle';
import zeebeModdle from 'zeebe-bpmn-moddle/resources/zeebe.json';
import { describe, expect, it } from 'vitest';

import { FLOWZER_MODDLE } from './flowzerModdle';

interface ParsedDefinitions {
  rootElements?: Array<{
    flowElements?: Array<{
      extensionElements?: { values?: Array<Record<string, unknown>> };
    }>;
  }>;
}

const DIRECTORY_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
  xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
  id="Definitions_1" targetNamespace="https://flowzer.io/test">
  <bpmn:process id="Process_1" isExecutable="true">
    <bpmn:userTask id="Task_1">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="Urlaubsantrag:1.0" />
        <flowzer:taskAssignment mode="directory"
          assigneeId="10000000-0000-0000-0000-000000000001"
          candidateUserIds="10000000-0000-0000-0000-000000000002"
          candidateGroupIds="20000000-0000-0000-0000-000000000001" />
      </bpmn:extensionElements>
    </bpmn:userTask>
  </bpmn:process>
</bpmn:definitions>`;

const AI_XML = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
  xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"
  id="Definitions_1" targetNamespace="https://flowzer.io/test">
  <bpmn:process id="Process_1" isExecutable="true">
    <bpmn:serviceTask id="Ai_1">
      <bpmn:extensionElements>
        <zeebe:taskDefinition type="flowzer.ai.v1" />
        <flowzer:aiTask contractVersion="1"
          connectionId="118adeb6-65a4-4e57-a03b-d3b0a3300ac9"
          model="model-a" instructionVersion="2"
          maxInputTokens="4096" maxOutputTokens="1024" timeoutSeconds="60">
          <flowzer:instruction>Classify the request.</flowzer:instruction>
          <flowzer:resultSchema>{"type":"object"}</flowzer:resultSchema>
          <flowzer:tool id="flowzer.directory.lookup" version="1" approval="automatic" />
        </flowzer:aiTask>
      </bpmn:extensionElements>
    </bpmn:serviceTask>
  </bpmn:process>
</bpmn:definitions>`;

// Testzweck: Der eigene Moddle-Vertrag muss exakt denselben Namespace und dieselben
// Attributnamen wie der serverseitige Parser verwenden.
describe('Flowzer-Moddle-Vertrag', () => {
  it('beschreibt die versionierte Task-Zuweisung ohne zusätzliche Felder', () => {
    expect(FLOWZER_MODDLE).toMatchObject({
      name: 'Flowzer',
      prefix: 'flowzer',
      uri: 'https://flowzer.io/schema/bpmn/1.0',
    });

    const assignment = FLOWZER_MODDLE.types.find((type) => type.name === 'TaskAssignment');
    expect(assignment).toMatchObject({
      name: 'TaskAssignment',
      superClass: ['Element'],
    });
    expect(assignment?.properties.map((property) => property.name)).toEqual([
      'mode',
      'assigneeId',
      'candidateUserIds',
      'candidateGroupIds',
    ]);
  });

  it('liest und schreibt den KI-Vertrag samt Prompt und Ergebnisschema semantisch unverändert', async () => {
    const moddle = new BpmnModdle({ zeebe: zeebeModdle, flowzer: FLOWZER_MODDLE });
    const parsed = await moddle.fromXML(AI_XML);
    const definitions = parsed.rootElement as unknown as ParsedDefinitions;
    const values = definitions.rootElements?.[0]?.flowElements?.[0]?.extensionElements?.values ?? [];
    const aiTask = values.find((value) => value.$type === 'flowzer:AiTask') as
      | (Record<string, unknown> & {
          instruction?: { body?: string };
          resultSchema?: { body?: string };
          tools?: Array<{ id?: string; version?: string; approval?: string }>;
        })
      | undefined;

    expect(aiTask).toMatchObject({
      contractVersion: '1',
      connectionId: '118adeb6-65a4-4e57-a03b-d3b0a3300ac9',
      model: 'model-a',
      instructionVersion: '2',
      maxInputTokens: '4096',
      maxOutputTokens: '1024',
      timeoutSeconds: '60',
    });
    expect(aiTask?.instruction?.body).toBe('Classify the request.');
    expect(aiTask?.resultSchema?.body).toBe('{"type":"object"}');
    expect(aiTask?.tools).toEqual([
      expect.objectContaining({ id: 'flowzer.directory.lookup', version: '1', approval: 'automatic' }),
    ]);

    const serialized = await moddle.toXML(parsed.rootElement, { format: true });
    expect(serialized.xml).toContain('<flowzer:aiTask');
    expect(serialized.xml).toContain('<flowzer:instruction>Classify the request.</flowzer:instruction>');
    expect(serialized.xml).toContain('<flowzer:resultSchema>{"type":"object"}</flowzer:resultSchema>');
    expect(serialized.xml).toContain(
      '<flowzer:tool id="flowzer.directory.lookup" version="1" approval="automatic" />',
    );
  });

  it('liest und schreibt stabile Verzeichnisreferenzen semantisch unverändert', async () => {
    const moddle = new BpmnModdle({ zeebe: zeebeModdle, flowzer: FLOWZER_MODDLE });
    const parsed = await moddle.fromXML(DIRECTORY_XML);
    const definitions = parsed.rootElement as unknown as ParsedDefinitions;
    const values = definitions.rootElements?.[0]?.flowElements?.[0]?.extensionElements?.values ?? [];
    const assignment = values.find((value) => value.$type === 'flowzer:TaskAssignment');

    expect(assignment).toMatchObject({
      mode: 'directory',
      assigneeId: '10000000-0000-0000-0000-000000000001',
      candidateUserIds: '10000000-0000-0000-0000-000000000002',
      candidateGroupIds: '20000000-0000-0000-0000-000000000001',
    });

    const serialized = await moddle.toXML(parsed.rootElement, { format: true });
    expect(serialized.xml).toContain('xmlns:flowzer="https://flowzer.io/schema/bpmn/1.0"');
    expect(serialized.xml).toContain('<flowzer:taskAssignment');
    expect(serialized.xml).toContain('assigneeId="10000000-0000-0000-0000-000000000001"');
  });

  it('lässt eine vorhandene Legacy-Textzuweisung beim Roundtrip unangetastet', async () => {
    const moddle = new BpmnModdle({ zeebe: zeebeModdle, flowzer: FLOWZER_MODDLE });
    const legacyXml = DIRECTORY_XML
      .replace(/\s+xmlns:flowzer="[^"]+"/, '')
      .replace(/\s*<flowzer:taskAssignment[\s\S]*?\/>/, '\n        <zeebe:assignmentDefinition assignee="anna" candidateGroups="personal" />');

    const parsed = await moddle.fromXML(legacyXml);
    const serialized = await moddle.toXML(parsed.rootElement, { format: true });

    expect(serialized.xml).toContain('<zeebe:assignmentDefinition assignee="anna" candidateGroups="personal" />');
    expect(serialized.xml).not.toContain('flowzer:taskAssignment');
  });
});

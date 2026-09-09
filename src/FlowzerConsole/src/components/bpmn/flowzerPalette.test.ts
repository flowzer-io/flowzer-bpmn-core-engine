import { describe, expect, it } from 'vitest';

import { createAiTaskBusinessObject } from './flowzerPalette';
import type { ModdleElement } from './moddle';

// Testzweck: Die eigene KI-Kachel erzeugt weiterhin einen Standard-BPMN-Service-Task,
// versieht ihn aber sofort mit einem konsistenten, versionierten Flowzer-Vertrag.
describe('Flowzer KI-Palette', () => {
  it('erzeugt Service-Task, reservierten Typ und KI-Grundvertrag gemeinsam', () => {
    const factory = {
      create: (type: string, properties: Record<string, unknown> = {}) => ({ $type: type, ...properties }) as ModdleElement,
    };

    const task = createAiTaskBusinessObject(factory);
    const extensions = task.extensionElements as ModdleElement;
    const values = extensions.values as ModdleElement[];
    const taskDefinition = values.find((value) => value.$type === 'zeebe:TaskDefinition');
    const aiTask = values.find((value) => value.$type === 'flowzer:AiTask')!;

    expect(task.$type).toBe('bpmn:ServiceTask');
    expect(taskDefinition).toMatchObject({ type: 'flowzer.ai.v1' });
    expect(aiTask).toMatchObject({
      contractVersion: '1',
      instructionVersion: '1',
      maxInputTokens: '4096',
      maxOutputTokens: '1024',
      timeoutSeconds: '60',
    });
    expect((aiTask.resultSchema as ModdleElement).body).toBe(
      '{"type":"object","properties":{},"additionalProperties":false}',
    );
    expect(aiTask.$parent).toBe(extensions);
  });
});

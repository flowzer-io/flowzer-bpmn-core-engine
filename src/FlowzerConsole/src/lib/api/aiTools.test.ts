import { describe, expect, it } from 'vitest';

import { normalizeAiTool } from './aiTools';

const TOOL = {
  id: 'flowzer.directory.lookup',
  version: 1,
  name: 'Directory lookup',
  description: 'Reads one bounded entry.',
  inputSchema: '{"type":"object"}',
  outputSchema: '{"type":"object"}',
  allowsPreApproval: false,
  contractHash: 'A'.repeat(64),
};

describe('KI-Werkzeugvertrag', () => {
  // Testzweck: Die numerische Serverdarstellung wird genau einmal am API-Rand in den
  // sprechenden UI-Wert übersetzt.
  it('normalisiert eine bekannte Außenwirkung', () => {
    expect(normalizeAiTool({ ...TOOL, sideEffect: 1 })).toMatchObject({ sideEffect: 'Write' });
  });

  // Testzweck: Eine künftig unbekannte Außenwirkung darf nicht versehentlich wie eine
  // ungefährliche Leseoperation in der Modellierungsoberfläche erscheinen.
  it('lehnt eine unbekannte Außenwirkung ab', () => {
    expect(() => normalizeAiTool({ ...TOOL, sideEffect: 99 })).toThrow('nicht unterstützt');
  });
});

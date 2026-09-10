import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

import { describe, expect, it } from 'vitest';

import {
  ClientFormContractError,
  inspectClientFormContract,
  validateClientFormContract,
} from './formContractClient';

interface VectorExpected {
  outcome: 'accepted' | 'rejected';
  code?: string;
  errors?: Record<string, string[]>;
  output?: Record<string, unknown>;
}

interface FormContractVector {
  id: string;
  purpose: string;
  phase: 'compile' | 'submission';
  profile: 'flowzer.forms/1' | 'flowzer.forms/2' | 'flowzer.forms/3' | 'flowzer.forms/4';
  comparison: 'client-server' | 'server-authoritative';
  schema: Record<string, unknown>;
  schemaPaddingLength?: number;
  context?: Record<string, unknown>;
  input?: Record<string, unknown>;
  expected: VectorExpected;
}

interface Manifest {
  version: number;
  cases: FormContractVector[];
}

const manifest = JSON.parse(readFileSync(
  resolve(process.cwd(), '../../tests/form-contract-vectors/manifest.json'),
  'utf8',
)) as Manifest;

function schemaOf(vector: FormContractVector) {
  return JSON.stringify(vector.schemaPaddingLength
    ? { ...vector.schema, padding: 'x'.repeat(vector.schemaPaddingLength) }
    : vector.schema);
}

describe('gemeinsame Formularvertragsvektoren', () => {
  // Testzweck: Die optionale Versionsangabe behandelt einen leeren Altwert genauso
  // wie der Server als Profil 1, statt ihn durch Number('') irrtümlich abzulehnen.
  it('behandelt eine leere Vertragsversion wie den Server als Profil 1', () => {
    expect(inspectClientFormContract(JSON.stringify({
      flowzer: { contractVersion: '' },
      components: [],
    }))).toEqual({ profile: 'flowzer.forms/1', requiresServer: false });
  });

  // Testzweck: Vitest liest denselben versionierten Vektorkatalog wie .NET und
  // sichert eindeutige, nachvollziehbar beschriebene Fälle ab.
  it('lädt genau einen eindeutig beschriebenen Katalog', () => {
    expect(manifest.version).toBe(1);
    expect(new Set(manifest.cases.map((vector) => vector.id)).size).toBe(manifest.cases.length);
    expect(manifest.cases.every((vector) => vector.purpose.trim().length > 0)).toBe(true);
  });

  // Testzweck: Clientseitig vergleichbare Compile-Regeln melden exakt dieselben
  // stabilen Ablehnungscodes wie der serverseitige Compiler.
  it('prüft die gemeinsamen Compile-Vektoren', () => {
    for (const vector of manifest.cases.filter((candidate) => candidate.phase === 'compile')) {
      expect(vector.comparison, vector.purpose).toBe('client-server');
      try {
        inspectClientFormContract(schemaOf(vector));
        throw new Error(`Vector '${vector.id}' was unexpectedly accepted.`);
      } catch (error) {
        expect(error, vector.purpose).toBeInstanceOf(ClientFormContractError);
        expect((error as ClientFormContractError).code, vector.purpose).toBe(vector.expected.code);
        expect((error as Error).message, vector.purpose).not.toContain('example.invalid');
      }
    }
  });

  // Testzweck: Vergleichbare Eingaberegeln liefern dieselben kanonischen Feldcodes;
  // Directory und Berechnungen bleiben sichtbar als serverautoritativ markiert.
  it('gleicht Submission-Ergebnisse ab und wahrt die Servergrenze', () => {
    for (const vector of manifest.cases.filter((candidate) => candidate.phase === 'submission')) {
      const schema = schemaOf(vector);
      expect(inspectClientFormContract(schema).profile, vector.purpose).toBe(vector.profile);
      const result = validateClientFormContract(schema, vector.input ?? {}, vector.context ?? {});
      if (vector.comparison === 'server-authoritative') {
        expect(result, vector.purpose).toEqual({ authority: 'server' });
        continue;
      }

      expect(result.authority, vector.purpose).toBe('client');
      if (result.authority !== 'client') continue;
      expect(result.outcome, vector.purpose).toBe(vector.expected.outcome);
      if (result.outcome === 'rejected') {
        expect(result.errors, vector.purpose).toEqual(vector.expected.errors);
        expect(JSON.stringify(result.errors), vector.purpose).not.toContain('NICHT_SPIEGELN');
      } else if (vector.expected.output) {
        expect(result.output, vector.purpose).toEqual(vector.expected.output);
      }
    }
  });
});

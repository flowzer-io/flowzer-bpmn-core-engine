import { describe, expect, it } from 'vitest';

import { runtimeMarkers, runtimeNodeStatus, runtimeState } from './runtimeView';

describe('Runtime-Projektion', () => {
  // Testzweck: Jeder bekannte serverseitige Status erhält eine explizite Darstellung;
  // neue Werte fallen sicher auf „unbekannt“ zurück, statt einen falschen Zustand zu zeigen.
  it('normalisiert alle Runtime-Statuswerte mit unbekanntem Fallback', () => {
    expect([0, 1, 2, 3, 99].map(runtimeNodeStatus)).toEqual([
      'active', 'completed', 'cancelled', 'failed', 'unknown',
    ]);
    expect(runtimeState(1)).toBe('Active');
    expect(runtimeState(99)).toBe('Unknown');
  });

  // Testzweck: Parallele Knotenmarkierungen bleiben vollständig erhalten und nur
  // aktive Knoten erzeugen Tokenpunkte; es wird keine lineare Schrittzahl berechnet.
  it('erzeugt Marker für parallele Laufzeitknoten', () => {
    const projection = {
      instanceId: 'instance-1', definitionId: 'definition-1', processId: 'Process_1',
      state: 2 as const, snapshotAtUtc: '2026-09-09T10:00:00Z', diagramXml: '<definitions />',
      events: [],
      nodes: [
        { flowNodeId: 'A', status: 0 as const, tokenCount: 1 },
        { flowNodeId: 'B', status: 2 as const, tokenCount: 2 },
        { flowNodeId: 'C', status: 3 as const, tokenCount: 1 },
      ],
    };

    expect(runtimeMarkers(projection)).toEqual({
      markers: { A: 'active', B: 'cancelled', C: 'failed' },
      activeNodeIds: ['A'],
    });
  });
});

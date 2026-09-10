import { describe, expect, it } from 'vitest';

import { createAiConnectionBody, normalizeAiConnection, updateAiConnectionBody } from './aiConnections';

describe('KI-Verbindungsvertrag', () => {
  // Testzweck: Numerische .NET-Enums werden am API-Rand stabil in sprechende UI-Werte
  // uebersetzt; die Oberflaeche verteilt keine Zahlenkonvention in ihre Komponenten.
  it('normalisiert Provider und Verarbeitungsort', () => {
    expect(normalizeAiConnection({
      id: 'connection-1',
      name: 'Lokal',
      provider: 1,
      location: 1,
      baseAddress: 'http://127.0.0.1:11434/v1',
      defaultModel: 'local-example',
      enabled: true,
      ready: true,
      revision: 3,
      updatedAtUtc: '2026-09-09T16:00:00Z',
    })).toMatchObject({ provider: 'OpenAiCompatible', location: 'Local' });
  });

  // Testzweck: Auswahlliterale werden als stabile numerische Enumwerte gesendet, die der
  // bestehende .NET-JSON-Vertrag akzeptiert.
  it('serialisiert Provider und Verarbeitungsort bei Create', () => {
    expect(createAiConnectionBody({
      name: 'Anthropic',
      provider: 'Anthropic',
      location: 'Cloud',
      defaultModel: 'claude-example',
      secretReference: 'env:FLOWZER_AI_ANTHROPIC',
    })).toMatchObject({ provider: 2, location: 0 });
  });

  // Testzweck: Eine leere Secret-Eingabe wird bei Updates nicht als Wert uebertragen;
  // insbesondere muss die UI dafuer keine gespeicherte Referenz vom Server zuruecklesen.
  it('laesst eine unveraenderte Secret-Referenz beim Update aus', () => {
    const body = updateAiConnectionBody({
      expectedRevision: 4,
      name: 'OpenAI',
      provider: 'OpenAi',
      location: 'Cloud',
      defaultModel: 'gpt-example',
      secretReference: '   ',
    });

    expect(body).toMatchObject({ provider: 0, location: 0 });
    expect(body.secretReference).toBeUndefined();
  });
});

import { describe, expect, it } from 'vitest';

import {
  ANY_FILTER,
  UNKNOWN_VERSION,
  matchesInstanceFilter,
  normalizeInstanceFilter,
  versionFilterOptions,
  workflowFilterOptions,
} from './instanceFilter';

const urlaub = {
  instanceId: 'instance-1',
  relatedDefinitionId: 'urlaub',
  relatedDefinitionName: 'Urlaubsantrag',
  definitionVersion: { major: 1, minor: 0 },
};
const urlaubNeu = { ...urlaub, instanceId: 'instance-2', definitionVersion: { major: 2, minor: 3 } };
const urlaubVerwaist = { ...urlaub, instanceId: 'instance-3', definitionVersion: null };
const reise = {
  ...urlaub,
  instanceId: 'instance-4',
  relatedDefinitionId: 'reise',
  relatedDefinitionName: 'Änderungsantrag',
};

describe('Auswahllisten der Instanzfilter', () => {
  // Testzweck: Die Liste nennt jeden Workflow einmal und in der Reihenfolge, in der man
  // ihn sucht — alphabetisch nach deutschem Alphabet, damit Umlaute nicht hinten landen.
  it('führt jeden vorkommenden Workflow einmal und nach Namen sortiert', () => {
    const options = workflowFilterOptions([urlaub, reise, urlaubNeu]);

    expect(options.map((option) => option.value)).toEqual(['reise', 'urlaub']);
    expect(options[1]?.count).toBe(2);
  });

  // Testzweck: Ohne gewählten Workflow bedeutet „v1.0“ bei jedem Workflow etwas anderes.
  // Eine gemeinsame Versionsliste wäre deshalb sinnlos.
  it('bietet ohne Workflow keine Versionen an', () => {
    expect(versionFilterOptions([urlaub, reise], ANY_FILTER)).toEqual([]);
  });

  // Testzweck: Nach einem Deployment sucht der Betrieb zuerst die neueste Version. Instanzen
  // ohne auffindbare Definition gehören ans Ende, bleiben aber wählbar — genau sie werden gesucht.
  it('sortiert die Versionen eines Workflows neueste zuerst und Unbekanntes zuletzt', () => {
    const options = versionFilterOptions([urlaub, urlaubVerwaist, urlaubNeu, reise], 'urlaub');

    expect(options.map((option) => option.value)).toEqual(['2.3', '1.0', UNKNOWN_VERSION]);
    expect(options[0]?.label).toBe('v2.3');
    expect(options[2]?.label).toMatch(/v\?/);
  });
});

describe('Anwenden der Instanzfilter', () => {
  // Testzweck: Wer nach einer Version filtert, will auch die Instanzen finden, deren
  // Definition gelöscht wurde — sie sind der eigentliche Anlass, hier nachzusehen.
  it('trennt Instanzen ohne bekannte Version von den versionierten', () => {
    const filter = { workflowId: 'urlaub', versionKey: UNKNOWN_VERSION };

    expect(matchesInstanceFilter(urlaubVerwaist, filter, '')).toBe(true);
    expect(matchesInstanceFilter(urlaub, filter, '')).toBe(false);
  });

  // Testzweck: Workflow- und Versionsfilter wirken zusammen mit der Suche; sonst zeigte die
  // Liste Zeilen, die nur einer der drei Einschränkungen genügen.
  it('verlangt Workflow, Version und Suchbegriff zugleich', () => {
    const filter = { workflowId: 'urlaub', versionKey: '1.0' };

    expect(matchesInstanceFilter(urlaub, filter, 'urlaub')).toBe(true);
    expect(matchesInstanceFilter(urlaub, filter, 'reise')).toBe(false);
    expect(matchesInstanceFilter(urlaubNeu, filter, '')).toBe(false);
    expect(matchesInstanceFilter(reise, { workflowId: 'urlaub', versionKey: ANY_FILTER }, '')).toBe(false);
  });

  // Testzweck: Wechselt der Workflow oder verschwindet eine Version beim Neuladen, bliebe
  // sonst eine Einschränkung aktiv, die in keiner Auswahlliste mehr steht und niemand abwählen kann.
  it('klemmt eine Version, die es zum Workflow nicht gibt', () => {
    const instances = [urlaub, reise];

    expect(normalizeInstanceFilter({ workflowId: 'reise', versionKey: '2.3' }, instances)).toEqual({
      workflowId: 'reise',
      versionKey: ANY_FILTER,
    });
    expect(normalizeInstanceFilter({ workflowId: 'geloescht', versionKey: '1.0' }, instances)).toEqual({
      workflowId: ANY_FILTER,
      versionKey: ANY_FILTER,
    });
  });
});

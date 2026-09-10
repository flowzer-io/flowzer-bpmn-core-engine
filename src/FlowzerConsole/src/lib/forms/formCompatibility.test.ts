import { describe, expect, it } from 'vitest';

import type { FormCompatibilityItemDto } from '@/lib/api/types';

import { describeCompatibilityIssue, incompatibleCountByForm } from './formCompatibility';

const item = (formId: string, compatible: boolean): FormCompatibilityItemDto => ({
  formId,
  formName: formId,
  source: 'published',
  compatible,
});

describe('Formular-Kompatibilitätsanzeige', () => {
  // Testzweck: Der Migrationsfilter zählt nur inkompatible Versionen und kann mehrere
  // betroffene Fassungen desselben Formulars als einen Listenstatus zusammenfassen.
  it('gruppiert ausschließlich migrationsbedürftige Einträge', () => {
    const grouped = incompatibleCountByForm([
      item('a', false), item('a', false), item('a', true), item('b', true), item('c', false),
    ]);

    expect([...grouped.entries()]).toEqual([['a', 2], ['c', 1]]);
  });

  // Testzweck: Bekannte Codes erhalten eine verständliche Meldung; manipulierte freie
  // Servertexte werden nicht ungefiltert in die Formularpflege gespiegelt.
  it('übersetzt nur sichere kanonische Fehlercodes', () => {
    expect(describeCompatibilityIssue('schema.script')).toContain('Custom-JavaScript');
    expect(describeCompatibilityIssue('<script>alert(1)</script>')).toBe(
      'benötigt Migration (unbekannter Code)',
    );
  });
});

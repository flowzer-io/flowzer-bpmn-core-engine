import { describe, expect, it, vi } from 'vitest';

import {
  clearPublicPackageScope,
  createSessionScope,
  registerPublicPackageScopeCleanup,
} from './sessionScope';

describe('öffentlicher Paket-Sitzungsscope', () => {
  // Testzweck: Jede erfolgreiche Browser-Sitzung erhält einen frischen, undurchsichtigen
  // Zufallswert statt einer Personen- oder Tokenkennung für öffentliche Query-Keys.
  it('erzeugt pro Aufruf einen neuen zufälligen Scope', () => {
    const first = createSessionScope();
    const second = createSessionScope();

    expect(first).not.toBe(second);
    expect(first).toMatch(/^[0-9a-f-]{36}$/i);
  });

  // Testzweck: Beim Ende einer Sitzung werden ausschließlich deren öffentlichen
  // Paketdaten entfernt, ohne den restlichen Console-Query-Cache anzutasten.
  it('meldet den beendeten Scope an den registrierten Cache-Cleanup', () => {
    const cleanup = vi.fn();
    const unregister = registerPublicPackageScopeCleanup(cleanup);

    clearPublicPackageScope('session-old');

    expect(cleanup).toHaveBeenCalledWith('session-old');
    unregister();
  });
});

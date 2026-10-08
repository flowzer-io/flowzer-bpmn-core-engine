import { afterEach, expect, it, vi } from 'vitest';
import { initializeOpaqueFormioRuntime } from './opaqueFormioRuntime';

afterEach(() => vi.unstubAllGlobals());

// Testzweck: Der isolierte Renderer fragt niemals die Form.io-Browseridentität ab
// und aktiviert keine frei ausführbaren Formularskripte als Sandbox-Workaround.
it('entfernt SDK-Identitätszugriffe nur im opaque Frame', () => {
  vi.stubGlobal('origin', 'null');
  const getUser = vi.fn(() => { throw new Error('Cookiezugriff'); });
  const getToken = vi.fn(() => { throw new Error('Storagezugriff'); });
  const runtime = { Evaluator: { noeval: false }, getUser, getToken,
    libraries: {} as Record<string, { ready: Promise<unknown> }> };
  initializeOpaqueFormioRuntime(runtime);
  expect(runtime.getUser()).toBeNull();
  expect(runtime.getToken()).toBe('');
  expect(getUser).not.toHaveBeenCalled();
  expect(getToken).not.toHaveBeenCalled();
  expect(runtime.Evaluator.noeval).toBe(true);
  expect(runtime.libraries.flatpickr?.ready).toBeInstanceOf(Promise);
  expect(runtime.libraries['flatpickr-css']?.ready).toBeInstanceOf(Promise);
  expect(runtime.libraries['flatpickr-de']?.ready).toBeInstanceOf(Promise);
});

// Testzweck: Eine versehentliche Initialisierung im normalen Console-/Host-Bundle
// darf dessen Identitäts- und Auswertungsverhalten nicht verändern.
it('lehnt nicht isolierte Dokumente ohne SDK-Änderung ab', () => {
  vi.stubGlobal('origin', 'https://console.example');
  const runtime = { Evaluator: { noeval: false }, getUser: vi.fn(), getToken: vi.fn(), libraries: {} };
  expect(() => initializeOpaqueFormioRuntime(runtime)).toThrow('isolierte Sandbox');
  expect(runtime.Evaluator.noeval).toBe(false);
  expect(vi.isMockFunction(runtime.getUser)).toBe(true);
  expect(vi.isMockFunction(runtime.getToken)).toBe(true);
});

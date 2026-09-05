import { describe, expect, it } from 'vitest';

import { sanitiseReturnTo } from './oidc';

describe('sanitiseReturnTo', () => {
  // Testzweck: Nach der Anmeldung geht es nur an eigene Adressen weiter. Ein Ziel wie
  // //evil.example beginnt mit einem Schraegstrich, verlaesst aber den Origin.
  it.each([
    ['/instances', '/instances'],
    ['/workflows?search=x', '/workflows?search=x'],
    ['//evil.example', '/'],
    ['/\\\\evil.example', '/'],
    ['https://evil.example/', '/'],
    ['javascript:alert(1)', '/'],
    [undefined, '/'],
    ['', '/'],
  ])('%s wird zu %s', (input, expected) => {
    expect(sanitiseReturnTo(input as string | undefined)).toBe(expected);
  });
});

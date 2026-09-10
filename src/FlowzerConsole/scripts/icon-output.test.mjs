import { describe, expect, it } from 'vitest';

import { escapeSingleQuotedLiteral } from './icon-output.mjs';

describe('Icon-Ausgabe', () => {
  // Testzweck: SVG-Inhalte dürfen weder Backslashes noch Anführungs- oder
  // Zeilensteuerzeichen aus dem generierten TypeScript-String ausbrechen lassen.
  it('maskiert alle relevanten Zeichen eines einfachen String-Literals', () => {
    expect(escapeSingleQuotedLiteral("path\\name'\r\n\u2028\u2029"))
      .toBe("path\\\\name\\'\\r\\n\\u2028\\u2029");
  });
});

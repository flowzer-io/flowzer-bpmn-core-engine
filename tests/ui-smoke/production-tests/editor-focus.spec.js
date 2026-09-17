const { test, expect } = require('@playwright/test');

// Synthetische Autorenentwürfe: echter Form.io-Builder, aber keine produktiven Schreibzugriffe.
async function mockAuthoring(page) {
  const formId = '11111111-1111-4111-8111-111111111111';
  const schema = {
    display: 'form',
    components: [
      { type: 'textfield', key: 'reason', label: 'Begründung', input: true },
      { type: 'textfield', key: 'second', label: 'Zweites Feld', input: true },
    ],
  };
  const draft = { formId, revision: 1, hasDraft: true, formData: JSON.stringify(schema) };
  await page.route('**/config.json', route => route.fulfill({ json: { apiBaseUrl: '/api', bffEnabled: true, accent: 'iris' } }));
  await page.route('**/bff/session', route => route.fulfill({ json: { id: 'author', name: 'Autor', capabilities: ['access', 'modeler'] } }));
  await page.route('**/bff/csrf', route => route.fulfill({ json: { requestToken: 'synthetic', headerName: 'X-CSRF' } }));
  await page.route('**/api/**', route => {
    const path = new URL(route.request().url()).pathname;
    let result = [];
    if (path === '/api/form/meta') result = [{ formId, name: 'Editor-Smoke' }];
    if (path === '/api/form/folders') result = [];
    if (path.endsWith('/draft')) result = draft;
    return route.fulfill({ json: { successful: true, result } });
  });
}

/**
 * Fokussiert ein Element so, wie eine Bibliothek es tut, und liest seinen Rahmen aus.
 *
 * Der Tastendruck davor ist der Kern der Prüfung: Erst danach hält der Browser die Tastatur
 * für die aktuelle Bedienart und zeigt `:focus-visible` auch bei programmatischem Fokus — genau
 * die Lage, in der jemand ein Feld ausfüllt und Form.io anschließend einen Baustein fokussiert.
 */
async function outlineAfterProgrammaticFocus(page, selector) {
  await page.keyboard.press('Tab');
  return page.evaluate((target) => {
    const element = document.querySelector(target);
    if (!element) return { missing: true };
    element.focus();
    const style = getComputedStyle(element);
    return {
      focusVisible: element.matches(':focus-visible'),
      outlineStyle: style.outlineStyle,
      outlineWidth: style.outlineWidth,
      outlineColor: style.outlineColor,
    };
  }, selector);
}

async function openEditor(page) {
  await mockAuthoring(page);
  await page.goto('/forms');
  await page.getByRole('button', { name: 'Bearbeiten', exact: true }).click();
  await expect(page.locator('.formio-builder input[name="data[reason]"]')).toBeVisible();
}

// Testzweck: Form.io fokussiert seine Bausteine selbst, obwohl sie mit der Tastatur nicht
// erreichbar sind. Der Tastatur-Fokusrahmen der Konsole legte sich dadurch als breiter Rahmen
// quer über den Editor und blieb stehen, bis der Fokus zufällig weiterwanderte.
test('Ein nur programmatisch fokussierter Editorbaustein bekommt keinen Tastatur-Fokusrahmen', async ({ page }) => {
  test.setTimeout(60_000);
  await openEditor(page);

  const component = page.locator('.formio-builder .builder-component').first();
  await expect(component).toHaveAttribute('tabindex', '-1');

  const outline = await outlineAfterProgrammaticFocus(page, '.formio-builder .builder-component');

  expect(outline.missing).toBeUndefined();
  expect(outline.outlineStyle).toBe('none');
});

// Testzweck: Der Fokusrahmen darf nur dort verschwinden, wo niemand mit der Tastatur hinkommt.
// Die Palette nebenan traegt tabindex="0" und ist anspringbar — dort muss sichtbar bleiben,
// wo man gerade steht, sonst waere der Editor ohne Maus nicht mehr bedienbar.
test('Mit der Tastatur erreichbare Bausteine behalten ihren Fokusrahmen', async ({ page }) => {
  test.setTimeout(60_000);
  await openEditor(page);

  const palette = page.locator('.formio-builder .formcomponent[tabindex="0"]').first();
  await expect(palette).toBeVisible();

  const accent = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--accent').trim());
  const outline = await outlineAfterProgrammaticFocus(page, '.formio-builder .formcomponent[tabindex="0"]');

  expect(outline.outlineStyle).toBe('solid');
  expect(outline.outlineWidth).toBe('2px');
  // Die Akzentfarbe steht als Hex in den Tokens, der Browser liefert sie als rgb().
  const [red, green, blue] = [1, 3, 5].map(offset => parseInt(accent.slice(offset, offset + 2), 16));
  expect(outline.outlineColor).toBe(`rgb(${red}, ${green}, ${blue})`);
});

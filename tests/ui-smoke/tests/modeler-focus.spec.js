const { test, expect } = require('@playwright/test');

const {
  buildPlainStartXml,
  createDefinitionMeta,
  deployDefinition,
  randomUUID,
} = require('../support/flowzer-api');

/** Die Zeichenfläche von bpmn-js; sie trägt `tabindex="0"` und ist per Tastatur bedienbar. */
const CANVAS = '.bpmn-surface .djs-container svg';

async function oeffneModeler(page, request) {
  const definitionId = await createDefinitionMeta(request, { name: `Fokus ${randomUUID().slice(0, 8)}` });
  await deployDefinition(request, { xml: buildPlainStartXml({ definitionId }) });
  const startEventId = `StartEvent_${definitionId.replace(/[^A-Za-z0-9_]/g, '_')}`;

  await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
  const startEvent = page.locator(`.bpmn-surface .djs-shape[data-element-id="${startEventId}"]`);
  await expect(startEvent).toBeVisible();
  return startEvent;
}

function canvasOutline(page) {
  return page.evaluate((selector) => {
    const canvas = document.querySelector(selector);
    if (!canvas) return { missing: true };
    const style = getComputedStyle(canvas);
    return {
      focused: canvas === document.activeElement,
      outlineStyle: style.outlineStyle,
      outlineColor: style.outlineColor,
    };
  }, CANVAS);
}

// Testzweck: Ein Klick ins Diagramm gibt der Zeichenfläche den Fokus. Der Browser rahmte sie
// daraufhin über ihre volle Größe ein — unter macOS in Systemblau — und der Rahmen blieb
// stehen, bis der Fokus zufällig weiterwanderte. Für eine Mausbedienung sagt er nichts aus.
test('Ein Klick auf ein Element rahmt nicht die ganze Zeichenfläche ein', async ({ page, request }) => {
  const startEvent = await oeffneModeler(page, request);

  await startEvent.click();
  await expect(page.locator('.djs-context-pad')).toBeVisible();

  const outline = await canvasOutline(page);

  expect(outline.missing).toBeUndefined();
  expect(outline.focused).toBe(true);
  expect(outline.outlineStyle).toBe('none');
});

// Testzweck: Die Zeichenfläche lässt sich mit den Pfeiltasten bedienen. Wer sie per Tastatur
// ansteuert, muss sehen, dass sie den Fokus hat — sonst wäre das Diagramm ohne Maus blind.
test('Mit der Tastatur angesteuert zeigt die Zeichenfläche ihren Fokus', async ({ page, request }) => {
  await oeffneModeler(page, request);

  const accent = await page.evaluate(() =>
    getComputedStyle(document.documentElement).getPropertyValue('--accent').trim());
  // Erst nach einem Tastendruck hält der Browser die Tastatur für die aktuelle Bedienart.
  await page.keyboard.press('Tab');
  await page.evaluate((selector) => document.querySelector(selector).focus(), CANVAS);

  const outline = await canvasOutline(page);
  const [red, green, blue] = [1, 3, 5].map(offset => parseInt(accent.slice(offset, offset + 2), 16));

  expect(outline.outlineStyle).toBe('solid');
  expect(outline.outlineColor).toBe(`rgb(${red}, ${green}, ${blue})`);
});

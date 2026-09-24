const { test, expect } = require('@playwright/test');

const { createDefinitionMeta, deployDefinition, saveForm, randomUUID } = require('../support/flowzer-api');

/**
 * Datumsfelder im Startdialog (Issue #368).
 *
 * Der Befund: Der aufgeklappte Kalender stand als Block im Dialoginhalt, war breiter als die
 * Feldspalte, und der Dialog bekam eine waagerechte Bildlaufleiste. Beim Aufklappen rollte er
 * seitlich mit — die Beschriftungen waren links abgeschnitten. Wochentage und Monat standen
 * auf Englisch.
 *
 * Der Browser meldet hier bewusst Deutsch: Die Konsole richtet die Sprache der Formulare nach
 * der Browsersprache (src/lib/locale.ts). Playwright gibt sonst en-US vor.
 */
test.use({ locale: 'de-DE' });

const LETZTER_TAG = 'Letzter Urlaubstag';
const ERSTER_TAG = 'Erster Urlaubstag';
const GRUND = 'Grund des Urlaubs';

const DEUTSCHE_WOCHENTAGE = ['Mo', 'Di', 'Mi', 'Do', 'Fr', 'Sa', 'So'];
const DEUTSCHE_MONATE = [
  'Januar', 'Februar', 'März', 'April', 'Mai', 'Juni',
  'Juli', 'August', 'September', 'Oktober', 'November', 'Dezember'
];

function datumsfeld(key, label, { mitUhrzeit = false } = {}) {
  return {
    type: 'datetime',
    key,
    label,
    input: true,
    enableDate: true,
    enableTime: mitUhrzeit,
    timePicker: { showMeridian: false },
    format: mitUhrzeit ? 'dd.MM.yyyy HH:mm' : 'dd.MM.yyyy',
    allowInput: true
  };
}

/**
 * Das Formular aus dem Befund: zwei Datumsfelder nebeneinander, darunter der Grund.
 *
 * Nebeneinander ist der Kern — erst in der rechten Spalte ragte der Kalender auf dem Desktop
 * über den Dialog hinaus. Auf Telefonbreite bleiben die Spalten im Startdialog nebeneinander
 * (die Umbruchregel in formio.css gilt nur für Form.ios eigenen Dialog); schon der ganze
 * Inhaltsbereich ist dort schmaler als der Kalender.
 */
function urlaubsformular({ mitUhrzeit = false, grundPflicht = false } = {}) {
  return {
    display: 'form',
    components: [
      {
        type: 'columns',
        key: 'zeitraum',
        input: false,
        columns: [
          { width: 6, size: 'md', components: [datumsfeld('ersterTag', ERSTER_TAG, { mitUhrzeit })] },
          { width: 6, size: 'md', components: [datumsfeld('letzterTag', LETZTER_TAG, { mitUhrzeit })] }
        ]
      },
      { type: 'textfield', key: 'grund', label: GRUND, input: true, validate: { required: grundPflicht } }
    ]
  };
}

/** Legt den Workflow „Urlaubsantrag“ mit dem Urlaubsformular als Startformular an. */
async function seedUrlaubsantrag(request, formularOptionen) {
  const formularName = `Urlaubsantrag ${randomUUID().slice(0, 8)}`;
  await saveForm(request, { name: formularName, schema: JSON.stringify(urlaubsformular(formularOptionen)) });

  const definitionId = await createDefinitionMeta(request, { name: 'Urlaubsantrag' });
  const s = definitionId.replace(/[^A-Za-z0-9_]/g, '_');
  const xml = `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                  xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                  xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                  xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                  xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                  id="${definitionId}" targetNamespace="http://bpmn.io/schema/bpmn">
  <bpmn:process id="Process_${s}" isExecutable="true">
    <bpmn:startEvent id="Start_${s}" name="Antrag stellen">
      <bpmn:extensionElements>
        <zeebe:formDefinition formKey="${formularName}" />
      </bpmn:extensionElements>
      <bpmn:outgoing>F1_${s}</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:sequenceFlow id="F1_${s}" sourceRef="Start_${s}" targetRef="End_${s}" />
    <bpmn:endEvent id="End_${s}" name="Fertig">
      <bpmn:incoming>F1_${s}</bpmn:incoming>
    </bpmn:endEvent>
  </bpmn:process>
  <bpmndi:BPMNDiagram id="D_${s}">
    <bpmndi:BPMNPlane id="P_${s}" bpmnElement="Process_${s}">
      <bpmndi:BPMNShape id="Start_${s}_di" bpmnElement="Start_${s}">
        <dc:Bounds x="173" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_${s}_di" bpmnElement="End_${s}">
        <dc:Bounds x="332" y="102" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="F1_${s}_di" bpmnElement="F1_${s}">
        <di:waypoint x="209" y="120" />
        <di:waypoint x="332" y="120" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>`;
  await deployDefinition(request, { xml });

  return { definitionId };
}

async function oeffneStartdialog(page, definitionId) {
  await page.goto(`/workflows/${encodeURIComponent(definitionId)}`);
  await page.getByRole('button', { name: 'Starten', exact: true }).click();

  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog.getByText(GRUND)).toBeVisible();
  return dialog;
}

/**
 * Wartet, bis alle endlichen Animationen im Dialog durchgelaufen sind — die Einblendung des
 * Dialogs und die des Kalenders (flatpickr schiebt ihn 300 ms lang von oben herein). Vorher
 * stimmen die gemessenen Lagen nicht. Endlose Animationen (Ladekreisel) zählen nicht.
 */
async function warteAufAnimationen(page) {
  await page.locator('[role="dialog"]').evaluate((dialog) => Promise.all(
    dialog.getAnimations({ subtree: true })
      .filter((animation) => animation.effect?.getComputedTiming().endTime !== Infinity)
      .map((animation) => animation.finished.catch(() => undefined))
  ));
}

/**
 * Klappt den Kalender des Feldes auf und wartet, bis er steht.
 *
 * Wiederholt, weil Form.io flatpickr nachlädt und die Entwicklungsfassung das Formular wegen
 * React StrictMode zweimal aufbaut — der Kalender des ersten Anlaufs verschwindet wieder.
 * Gesucht wird auf der ganzen Seite: Wo der Kalender im DOM hängt, ist Sache der Konsole.
 */
async function oeffneKalender(page, feld) {
  const kalender = page.locator('.flatpickr-calendar.open');

  await expect(async () => {
    if ((await kalender.count()) === 0) {
      await feld.click({ timeout: 2000 });
    }
    await expect(kalender, 'Der Kalender klappt nicht auf.').toBeVisible({ timeout: 2000 });
    await warteAufAnimationen(page);
    await expect(kalender).toHaveCount(1, { timeout: 500 });
  }).toPass({ timeout: 25_000 });

  return kalender;
}

/** Schließt den Startdialog und prüft, dass weder Kalender noch Kalenderebene zurückbleiben. */
async function schliesseUndPruefeAufraeumen(page, dialog) {
  await dialog.getByRole('button', { name: 'Abbrechen', exact: true }).click();
  await expect(dialog).toHaveCount(0);
  await expect(page.locator('.flowzer-calendar-layer'), 'Die Kalenderebene bleibt nach dem Dialog stehen.').toHaveCount(0);
  await expect(page.locator('.flatpickr-calendar'), 'Ein Kalender bleibt nach dem Dialog stehen.').toHaveCount(0);
}

/** Misst Dialog, Bildlaufflächen und Kalender in einem Zug. */
async function vermesse(page) {
  return page.evaluate(({ labels }) => {
    const dialog = document.querySelector('[role="dialog"]');
    const kalender = document.querySelector('.flatpickr-calendar.open');
    const rahmen = (element) => {
      const r = element.getBoundingClientRect();
      return { left: r.left, top: r.top, right: r.right, bottom: r.bottom, width: r.width, height: r.height };
    };

    const scrollbereiche = [dialog, ...dialog.querySelectorAll('*')]
      .filter((element) => ['auto', 'scroll'].includes(getComputedStyle(element).overflowX))
      .map((element) => ({
        klasse: String(element.className),
        scrollWidth: element.scrollWidth,
        clientWidth: element.clientWidth,
        scrollLeft: element.scrollLeft
      }));

    const beschriftungen = labels.map((text) => {
      const label = [...dialog.querySelectorAll('label')].find((l) => l.textContent.trim().startsWith(text));
      return { text, rahmen: label ? rahmen(label) : null };
    });

    return {
      viewport: { width: document.documentElement.clientWidth, height: window.innerHeight },
      seite: { scrollWidth: document.documentElement.scrollWidth, clientWidth: document.documentElement.clientWidth },
      dialog: rahmen(dialog),
      scrollbereiche,
      beschriftungen,
      kalender: kalender ? rahmen(kalender) : null,
      kalenderImDialog: kalender ? dialog.contains(kalender) : false
    };
  }, { labels: [ERSTER_TAG, LETZTER_TAG, GRUND] });
}

async function kalenderTexte(kalender) {
  const wochentage = (await kalender.locator('.flatpickr-weekday').allTextContents())
    .map((text) => text.trim())
    .slice(0, 7);
  const monatsauswahl = kalender.locator('select.flatpickr-monthDropdown-months option:checked');
  const monat = (await monatsauswahl.count()) > 0
    ? (await monatsauswahl.textContent()).trim()
    : (await kalender.locator('.cur-month').textContent()).trim();
  return { wochentage, monat };
}

for (const viewport of [
  { name: 'desktop', width: 1280, height: 800 },
  { name: 'mobil', width: 375, height: 812 }
]) {
  // Testzweck: Der Kalender eines Datumsfelds im Startdialog steht vollständig im Bild, ohne
  // dass der Dialog seitlich rollt oder Beschriftungen abschneidet, spricht Deutsch und nimmt
  // einen Tag per Klick an — auf Desktop und Telefonbreite (Issue #368).
  test(`Datepicker im Startdialog bei ${viewport.width}px`, async ({ page, request }, testInfo) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    const { definitionId } = await seedUrlaubsantrag(request);

    const dialog = await oeffneStartdialog(page, definitionId);
    const feld = dialog.getByRole('textbox', { name: LETZTER_TAG });
    const kalender = await oeffneKalender(page, feld);

    await page.screenshot({ path: testInfo.outputPath(`datepicker-${viewport.name}.png`) });
    const mass = await vermesse(page);
    await testInfo.attach('vermessung', { body: JSON.stringify(mass, null, 2), contentType: 'application/json' });

    expect(mass.kalenderImDialog, 'Der Kalender hängt außerhalb des Dialogs und wäre dort gesperrt.').toBe(true);
    for (const bereich of mass.scrollbereiche) {
      expect(bereich.scrollWidth, `Bildlauffläche „${bereich.klasse}“ läuft waagerecht über.`)
        .toBeLessThanOrEqual(bereich.clientWidth);
      expect(bereich.scrollLeft, `Bildlauffläche „${bereich.klasse}“ ist seitlich verrollt.`).toBe(0);
    }
    expect(mass.seite.scrollWidth, 'Die Seite läuft waagerecht über.').toBeLessThanOrEqual(mass.seite.clientWidth);

    for (const { text, rahmen } of mass.beschriftungen) {
      expect(rahmen, `Beschriftung „${text}“ fehlt.`).not.toBeNull();
      expect(rahmen.left, `Beschriftung „${text}“ ist links abgeschnitten.`).toBeGreaterThanOrEqual(mass.dialog.left);
    }

    expect(mass.kalender.left, 'Der Kalender ragt links aus dem Bild.').toBeGreaterThanOrEqual(0);
    expect(mass.kalender.top, 'Der Kalender ragt oben aus dem Bild.').toBeGreaterThanOrEqual(0);
    expect(mass.kalender.right, 'Der Kalender ragt rechts aus dem Bild.').toBeLessThanOrEqual(mass.viewport.width);
    expect(mass.kalender.bottom, 'Der Kalender ragt unten aus dem Bild.').toBeLessThanOrEqual(mass.viewport.height);

    const texte = await kalenderTexte(kalender);
    expect(texte.wochentage, 'Die Wochentage sind nicht deutsch oder die Woche beginnt nicht am Montag.')
      .toEqual(DEUTSCHE_WOCHENTAGE);
    expect(DEUTSCHE_MONATE, `Der Monat „${texte.monat}“ ist nicht deutsch.`).toContain(texte.monat);

    // Die Randtage gehören zum Nachbarmonat und würden umblättern; der zehnte Tag ist sicher.
    await kalender
      .locator('.flatpickr-day:not(.prevMonthDay):not(.nextMonthDay):not(.flatpickr-disabled)')
      .nth(9)
      .click();
    await expect(feld, 'Der angeklickte Tag steht nicht im Eingabefeld.').toHaveValue(/^10\.\d{2}\.\d{4}$/);
    await expect(dialog, 'Der Klick in den Kalender hat den Startdialog geschlossen.').toBeVisible();

    await schliesseUndPruefeAufraeumen(page, dialog);
  });
}

// Testzweck: Die Uhrzeitfelder des Kalenders lassen sich im Startdialog fokussieren und per
// Tastatur stellen. Die Fokusfalle des Dialogs holte den Fokus sonst sofort zurück, sobald der
// Kalender außerhalb des Dialoginhalts hinge.
test('Uhrzeit im Datepicker des Startdialogs ist per Tastatur einstellbar', async ({ page, request }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  const { definitionId } = await seedUrlaubsantrag(request, { mitUhrzeit: true });

  const dialog = await oeffneStartdialog(page, definitionId);
  const feld = dialog.getByRole('textbox', { name: LETZTER_TAG });
  const kalender = await oeffneKalender(page, feld);

  await kalender
    .locator('.flatpickr-day:not(.prevMonthDay):not(.nextMonthDay):not(.flatpickr-disabled)')
    .nth(9)
    .click();
  const stunde = kalender.locator('input.flatpickr-hour');
  await stunde.click();
  await expect(stunde, 'Die Stunde behält den Fokus nicht.').toBeFocused();
  await stunde.fill('14');
  await stunde.press('Tab');

  await expect(feld, 'Die eingegebene Stunde kommt nicht im Feld an.').toHaveValue(/^10\.\d{2}\.\d{4} 14:\d{2}$/);
  await expect(dialog, 'Die Eingabe im Kalender hat den Startdialog geschlossen.').toBeVisible();

  // Mit Uhrzeit bleibt der Kalender nach der Eingabe offen und liegt über dem Dialogfuß.
  // Escape aus der Uhrzeit heraus schließt nur ihn; Wert und Dialog bleiben.
  await page.keyboard.press('Escape');
  await expect(kalender, 'Escape aus der Uhrzeit schließt den Kalender nicht.').toHaveCount(0);
  await expect(feld).toHaveValue(/^10\.\d{2}\.\d{4} 14:\d{2}$/);

  await schliesseUndPruefeAufraeumen(page, dialog);
});

// Testzweck: Escape bei offenem Kalender schließt nur den Kalender; Dialog und Eingaben bleiben.
// Erst das zweite Escape schließt den Startdialog (Issue #370). Radix wertet Escape schon in der
// Capture-Phase aus und schloss sonst den ganzen Dialog samt getipptem Grund.
test('Escape schließt erst den Kalender, dann den Startdialog', async ({ page, request }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  const { definitionId } = await seedUrlaubsantrag(request);

  const dialog = await oeffneStartdialog(page, definitionId);
  const grund = dialog.getByRole('textbox', { name: GRUND });
  await grund.fill('Familienbesuch');
  const feld = dialog.getByRole('textbox', { name: LETZTER_TAG });
  const kalender = await oeffneKalender(page, feld);

  await page.keyboard.press('Escape');

  await expect(kalender, 'Escape hat den Kalender nicht geschlossen.').toHaveCount(0);
  await expect(dialog, 'Escape hat mit dem Kalender den ganzen Startdialog geschlossen.').toBeVisible();
  await expect(grund, 'Die Eingabe im Startdialog ging verloren.').toHaveValue('Familienbesuch');

  await page.keyboard.press('Escape');

  await expect(dialog, 'Das zweite Escape schließt den Startdialog nicht.').toHaveCount(0);
});

// Testzweck: Die Prüfmeldungen des Formulars erscheinen in einem deutschen Browser auf Deutsch.
// Form.io bringt die Übersetzung selbst mit; ohne `language` zeigte der Startdialog
// „Grund des Urlaubs is required“.
test('Pflichtfeldmeldung im Startdialog ist deutsch', async ({ page, request }) => {
  const { definitionId } = await seedUrlaubsantrag(request, { grundPflicht: true });

  const dialog = await oeffneStartdialog(page, definitionId);
  await dialog.getByRole('button', { name: 'Starten', exact: true }).click();

  await expect(dialog.getByText(`${GRUND} ist erforderlich`).first()).toBeVisible();
  await expect(dialog.getByText(/is required/)).toHaveCount(0);
});

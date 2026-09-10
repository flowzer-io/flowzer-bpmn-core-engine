const { test, expect } = require('@playwright/test');

for (const viewport of [{ width: 1440, height: 960 }, { width: 390, height: 844 }]) {
  // Testzweck: Die bewusst reduzierte API-Antwort bleibt auf Desktop und Mobil nutzbar;
  // keine technische Nachladeanfrage umgeht die Projektion. Echte Rechte prüft die JWT-API-Suite.
  test(`Vorgangsübersicht ohne Diagnosedaten bei ${viewport.width}px`, async ({ page }, testInfo) => {
    await page.setViewportSize(viewport);
    const instanceId = '7c9388cc-233f-4f81-bcae-276b84531437';
    const technicalRequests = [];
    page.on('request', request => {
      if (/\/subscription\/|\/definition\/[^/]+\/xml/.test(request.url())) technicalRequests.push(request.url());
    });
    await page.route(`**/api/instance/${instanceId}`, route => route.fulfill({
      json: { successful: true, result: {
        instanceId, definitionId: 'a6c16d51-8d1b-4410-8c4b-b5288d794822', relatedDefinitionId: 'urlaub',
        relatedDefinitionName: 'Urlaubsantrag – September', state: 'Waiting', canInspect: false,
        startedAt: '2026-09-08T10:00:00Z', tokens: [], userTaskSubscriptionCount: 1,
        messageSubscriptionCount: 0, signalSubscriptionCount: 0, serviceSubscriptionCount: 0,
      } },
    }));
    await page.goto(`/instances/${instanceId}`);
    await expect(page.getByRole('heading', { name: 'Urlaubsantrag – September' })).toBeVisible();
    await expect(page.getByText('Vorgangsübersicht', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Zu meinen Aufgaben' })).toBeVisible();
    await expect(page.getByRole('tab', { name: 'Variablen' })).toHaveCount(0);
    await expect(page.locator('.bpmn-surface')).toHaveCount(0);
    expect(technicalRequests).toEqual([]);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await page.screenshot({ path: testInfo.outputPath(`overview-${viewport.width}.png`), fullPage: true });
  });
}

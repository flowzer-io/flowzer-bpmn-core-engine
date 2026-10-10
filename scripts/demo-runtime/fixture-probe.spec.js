// Testzweck: Tatsächliche Playwright-Worker-/Browser-/Context-Fixtures statt nur Mockbindung.
// Eigener TLS-Listener, ausschließlich synthetische Seiten; keine Container oder IdPs.
const { test, expect } = require('./restricted-test');
const { certificate, listener } = require('./egress-probe');
let tls;let requests=0;let firstProxy;
test.beforeAll(async()=> {
  tls=await listener(certificate(),8443,(_request,response)=> {
    requests+=1;response.writeHead(200,{'Content-Type':'text/html'});response.end('<title>fixture-probe</title>');
  });
});
test.afterAll(async()=> { if(tls) await tls.close(); });
for (const round of [1,2]) {
  test('Eigener Workerproxy vermittelt Context '+round,async({page,_egress,browserName})=> {
    // Testzweck: Beide echten Contexts verwenden denselben eigenen Workertransport ohne Fixturezyklus.
    expect(browserName).toBe('chromium');
    if(firstProxy) expect(_egress.server).toBe(firstProxy);else firstProxy=_egress.server;
    const before=requests;
    expect((await page.goto('https://flowzer.test:8443',{timeout:15000})).status()).toBe(200);
    expect(await page.title()).toBe('fixture-probe');expect(requests).toBeGreaterThan(before);
    expect(_egress.blocked()).toBe(0);
  });
}

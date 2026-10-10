// Hermetische Fixture-Abnahme mit denselben geschlossenen Transport-/Reporterregeln.
module.exports={outputDir:require('node:path').join(require('node:path').dirname(process.env.FLOWZER_RUNTIME_REPORT),'test-results'),testDir:'.',testMatch:'fixture-probe.spec.js',workers:1,fullyParallel:false,retries:0,
  timeout:30000,reporter:[[require.resolve('./safe-reporter')]],
  use:{browserName:'chromium',ignoreHTTPSErrors:true,serviceWorkers:'block',trace:'off',screenshot:'off',video:'off'}};

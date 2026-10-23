// Test only: opens a generated track map (mapa_*.html) in headless Chromium and fails on any script error.
// Usage: node check_map.js <mapa.html>   (needs Playwright; in cloud sessions it is preinstalled globally)
const path = require('path');
let pw;
try { pw = require('playwright'); } catch (e) { pw = require(path.join(require('child_process').execSync('npm root -g').toString().trim(), 'playwright')); }
(async () => {
  const b = await pw.chromium.launch();
  const p = await b.newPage({ viewport: { width: 1400, height: 900 } });
  const errs = [];
  p.on('pageerror', e => errs.push(e.message));
  p.on('console', m => { if (m.type() === 'error') errs.push(m.text()); });
  await p.goto('file://' + path.resolve(process.argv[2]));
  await p.waitForTimeout(800);
  const laps = await p.$eval('#lap', s => s.options.length);
  const charts = await p.$$eval('#charts .chart', c => c.length);
  await b.close();
  console.log('map: ' + laps + ' laps, ' + charts + ' charts, errors: ' + JSON.stringify(errs));
  process.exit(errs.length || !laps ? 1 : 0);
})();
